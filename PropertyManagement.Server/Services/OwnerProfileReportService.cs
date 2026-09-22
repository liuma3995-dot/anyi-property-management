using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Dapper;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;
using PropertyManagement.Server.Infrastructure;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 业主档案 → 导出 PDF（CHG-v1.2.0-13，负责人 2026-09-21）。
    ///
    /// 需求：业主档案页面工具栏新增「导出 PDF」，内容 = **本年度缴费概况 + 缴费明细记录**；
    ///      原「导出 Excel（业主档案表格）」不变，两条通道并存。
    ///
    /// 口径（与页面概况同源，避免两处各算一套）：
    ///   · 年度 = 账单到期日（due_at）所属年度；
    ///   · 账单归属三类缴费对象：房产账单（按房产的业主关系）、车位账单（按车位绑定业主，
    ///     未绑业主时回落车位绑定房产的业主）、业主直缴账单（owner_id 即业主本人）——
    ///     与 CHG-v1.2.0-11 的概况修复同口径；
    ///   · 文件落 DbConfig.ExportDirectory，写 t_report_log（report_type = owner_profile）留痕，
    ///     客户端按导出记录 ID 走既有 /reports/files/{id} 下载。
    /// </summary>
    public class OwnerProfileReportService
    {
        private const string ReportType = "owner_profile";

        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IBaseInfoRepository _baseInfo;
        private readonly IFinanceRepository _finance;
        private readonly AuditService _audit;

        public OwnerProfileReportService()
            : this(new SqliteConnectionFactory(), new SqlBaseInfoRepository(), new SqlFinanceRepository(), new AuditService())
        {
        }

        public OwnerProfileReportService(IDbConnectionFactory connectionFactory, IBaseInfoRepository baseInfo,
            IFinanceRepository finance, AuditService audit)
        {
            _connectionFactory = connectionFactory;
            _baseInfo = baseInfo;
            _finance = finance;
            _audit = audit;
        }

        /// <summary>导出业主档案 PDF（年度缴费概况 + 账单明细 + 收款明细），返回导出留痕记录。</summary>
        public ReportLogDto Export(int ownerId, int? year, string operatorName)
        {
            if (ownerId <= 0) { throw ApiException.ValidationFailed("请指定要导出的业主"); }
            int statYear = year.HasValue && year.Value > 2000 ? year.Value : DateTime.Today.Year;

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                OwnerDto owner = _baseInfo.GetOwnerWithYearStats(connection, ownerId, statYear);
                if (owner == null) { throw ApiException.NotFound("业主不存在或已删除"); }

                List<OwnerBillRow> bills = QueryBills(connection, ownerId, statYear);
                List<OwnerPaymentRow> payments = QueryPayments(connection, ownerId, statYear);

                string period = DateTime.Now.ToString("yyyyMMddHHmmss");
                string fileName = "owner_profile_" + ownerId + "_" + period + ".pdf";
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);
                WritePdf(filePath, owner, statYear, bills, payments);

                var log = new ReportLogDto
                {
                    ReportType = ReportType,
                    Period = statYear + "-" + ownerId,
                    Format = ExportFormat.Pdf,
                    FilePath = filePath
                };
                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    log.Id = _finance.InsertReportLog(connection, transaction, log);
                    transaction.Commit();
                }

                _audit.Write("OWNER_PROFILE_EXPORT", "owner", ownerId.ToString(),
                    "业主档案导出 PDF：" + owner.Name + "，" + statYear + " 年度，账单 " + bills.Count +
                    " 张 / 收款 " + payments.Count + " 笔，文件 " + fileName,
                    null, operatorName, "基础信息", "成功");

                return _finance.GetReportLog(connection, log.Id) ?? log;
            }
        }

        /// <summary>
        /// 导出**全部业主**的年度缴费汇总明细（CHG-v1.2.0-17，负责人 2026-09-21：
        /// 「只支持单业主，需要增加导出全部业主缴费明细记录的选项」）。
        ///
        /// CHG-v1.2.0-19（负责人 2026-09-21 第 2 轮口径）：**只保留「一、全部业主汇总缴费明细」一段**，
        /// 移除「二、逐户明细」—— 逐户的账单/收款明细仍由「导出 PDF（单业主）」提供，两份文件分工不重复；
        /// 顺带把体积与耗时压到与户数线性（万级户数不再生成成百上千页）。
        /// 口径与单业主导出一致（账期起始日年度 + 三类缴费对象归属）。
        /// </summary>
        public ReportLogDto ExportAll(int? year, string operatorName)
        {
            int statYear = year.HasValue && year.Value > 2000 ? year.Value : DateTime.Today.Year;

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                // CHG-v1.2.0-19：汇总明细只需「每户年度金额」——一次批量统计（不再逐户查账单/收款，消除 N+1）
                List<OwnerDto> owners = _baseInfo.QueryOwnersWithYearStats(connection,
                    new BaseInfoQueryRequest { PageIndex = 1, PageSize = 100000 }, statYear, out int _)
                    ?? new List<OwnerDto>();
                // CHG-v1.2.0-20：业主名下房产（楼栋/房号）——同样一次批量查询
                Dictionary<int, List<string>> paths = _baseInfo.QueryOwnerPropertyPaths(connection, owners.Select(o => o.Id));
                var items = owners.Select(o => new OwnerSection
                {
                    Owner = o,
                    PropertyText = FormatPropertyPaths(paths.ContainsKey(o.Id) ? paths[o.Id] : null)
                }).ToList();

                string period = DateTime.Now.ToString("yyyyMMddHHmmss");
                string fileName = "owner_profile_all_" + period + ".pdf";
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);
                WriteAllPdf(filePath, statYear, items);

                var log = new ReportLogDto
                {
                    ReportType = ReportType,
                    Period = statYear + "-ALL",
                    Format = ExportFormat.Pdf,
                    FilePath = filePath
                };
                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    log.Id = _finance.InsertReportLog(connection, transaction, log);
                    transaction.Commit();
                }

                _audit.Write("OWNER_PROFILE_EXPORT_ALL", "owner", "all",
                    "业主档案导出 PDF（全部业主）：" + statYear + " 年度，" + items.Count + " 户，文件 " + fileName,
                    null, operatorName, "基础信息", "成功");

                return _finance.GetReportLog(connection, log.Id) ?? log;
            }
        }

        /// <summary>汇总明细的一行（CHG-v1.2.0-19：只保留业主与其年度金额）。</summary>
        private sealed class OwnerSection
        {
            public OwnerDto Owner { get; set; }

            /// <summary>名下房产「楼栋/房号」（CHG-v1.2.0-20）：多处用「、」连接，超过 3 处显示「等 N 处」。</summary>
            public string PropertyText { get; set; }
        }

        /// <summary>房产路径列文本（CHG-v1.2.0-20）：无房产显示「—」；超过 3 处折叠为「前 3 处 等 N 处」。</summary>
        private static string FormatPropertyPaths(List<string> paths)
        {
            if (paths == null || paths.Count == 0) { return "—"; }
            if (paths.Count <= 3) { return string.Join("、", paths); }
            return string.Join("、", paths.Take(3)) + " 等 " + paths.Count + " 处";
        }

        /// <summary>
        /// 业主归属条件（三类缴费对象统一口径）：房产账单按房产的业主关系、车位账单按车位绑定业主
        /// （未绑业主时回落车位绑定房产的业主）、业主直缴账单按 owner_id。三处条件与被引用的子查询一致。
        /// </summary>
        private const string OwnerScopeSql =
            "(" +
            " (b.property_id IS NOT NULL AND EXISTS (SELECT 1 FROM t_owner_property_rel r " +
            "   WHERE r.property_id = b.property_id AND r.owner_id = @ownerId AND r.del_flag = 0)) " +
            " OR (b.parking_id IS NOT NULL AND (" +
            "   EXISTS (SELECT 1 FROM t_parking_space pk WHERE pk.id = b.parking_id AND pk.del_flag = 0 AND pk.owner_id = @ownerId) " +
            "   OR EXISTS (SELECT 1 FROM t_parking_space pk2 JOIN t_owner_property_rel r2 " +
            "     ON r2.property_id = pk2.property_id AND r2.del_flag = 0 " +
            "     WHERE pk2.id = b.parking_id AND pk2.del_flag = 0 AND r2.owner_id = @ownerId))) " +
            " OR (b.owner_id = @ownerId AND b.property_id IS NULL AND b.parking_id IS NULL)" +
            ")";

        /// <summary>缴费对象展示文本（楼栋/单元/房号 ｜ 车位编号 ｜ 业主直缴）。</summary>
        private static readonly string ObjectTextSql =
            "COALESCE(" +
            "(SELECT " + SqlAddress.BuildingUnitRoom("bld.building_no", "u.unit_no", "p.room_no") + " " +
            " FROM t_property p LEFT JOIN t_unit u ON u.id = p.unit_id " +
            " LEFT JOIN t_building bld ON bld.id = COALESCE(u.building_id, p.building_id) " +
            " WHERE p.id = b.property_id AND p.del_flag = 0), " +
            "(SELECT ps.space_no FROM t_parking_space ps WHERE ps.id = b.parking_id AND ps.del_flag = 0), " +
            "NULLIF(b.payer_name, ''), '业主直缴')";

        /// <summary>本年度账单明细（应缴侧）。</summary>
        private static List<OwnerBillRow> QueryBills(IDbConnection connection, int ownerId, int year)
        {
            return connection.Query<OwnerBillRow>(
                "SELECT b.id AS BillId, b.amount AS Amount, b.paid_amount AS PaidAmount, b.status AS Status, " +
                "b.due_at AS DueAt, COALESCE(ci.name, '') AS ItemName, " +
                "COALESCE(c.start_date, '') AS CycleStart, COALESCE(c.end_date, '') AS CycleEnd, " +
                ObjectTextSql + " AS ObjectText " +
                "FROM t_bill b LEFT JOIN t_charge_item ci ON ci.id = b.charge_item_id " +
                "LEFT JOIN t_billing_cycle c ON c.id = b.cycle_id " +
                "WHERE b.del_flag = 0 AND " + YearFilterSql + " = @year AND " + OwnerScopeSql + " " +
                "ORDER BY b.due_at, b.id",
                new { ownerId, year = year.ToString() }).ToList();
        }

        /// <summary>本年度收款明细（实缴侧，仅正常收款，与收支明细流水口径一致）。</summary>
        private static List<OwnerPaymentRow> QueryPayments(IDbConnection connection, int ownerId, int year)
        {
            return connection.Query<OwnerPaymentRow>(
                "SELECT p.id AS PaymentId, p.paid_at AS PaidAt, p.amount AS Amount, p.pay_method AS PayMethod, " +
                "COALESCE(NULLIF(p.batch_no, ''), r.receipt_no, '') AS BizNo, " +
                "COALESCE(ci.name, '') AS ItemName, " +
                "COALESCE(c.start_date, '') AS CycleStart, COALESCE(c.end_date, '') AS CycleEnd, " +
                ObjectTextSql + " AS ObjectText " +
                "FROM t_payment p LEFT JOIN t_receipt r ON r.payment_id = p.id " +
                "LEFT JOIN t_bill b ON b.id = p.bill_id " +
                "LEFT JOIN t_charge_item ci ON ci.id = b.charge_item_id " +
                "LEFT JOIN t_billing_cycle c ON c.id = b.cycle_id " +
                "WHERE p.status = 0 AND b.del_flag = 0 AND " + YearFilterSql + " = @year AND " + OwnerScopeSql + " " +
                "ORDER BY p.paid_at, p.id",
                new { ownerId, year = year.ToString() }).ToList();
        }

        /// <summary>
        /// 年度归属口径（CHG-v1.2.0-16）：账期起始日所在年度，无账期时回落到期日 —— 与仪表盘 / 业主概况同源。
        /// </summary>
        private const string YearFilterSql = "COALESCE(strftime('%Y', c.start_date), strftime('%Y', b.due_at))";

        // ==================== PDF 绘制 ====================

        private static void WritePdf(string filePath, OwnerDto owner, int year,
            List<OwnerBillRow> bills, List<OwnerPaymentRow> payments)
        {
            PdfFontSupport.Ensure();
            var titleFont = new XFont("SimHei", 15, XFontStyleEx.Bold);
            var headFont = new XFont("SimHei", 10, XFontStyleEx.Bold);
            var bodyFont = new XFont("SimHei", 9, XFontStyleEx.Regular);
            var smallFont = new XFont("SimHei", 8, XFontStyleEx.Regular);

            using (var document = new PdfDocument())
            {
                using (var canvas = new PdfCanvas(document))
                {
                    canvas.Draw("安怡物业 · 业主缴费概况与缴费明细", titleFont, 36);
                    canvas.Y += 22;
                    canvas.Draw("业主：" + owner.Name + "（YZ-" + owner.Id.ToString("D3") + "）　电话：" +
                                (string.IsNullOrWhiteSpace(owner.Phone) ? "—" : owner.Phone) +
                                "　年度：" + year + " 年度　导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        bodyFont, 36);
                    canvas.Y += 22;

                    // 一、本年度缴费概况
                    canvas.Draw("一、本年度缴费概况", headFont, 36);
                    canvas.Y += 16;
                    string rate = owner.YearReceivable == 0m
                        ? "—"
                        : Math.Round(owner.YearPaid / owner.YearReceivable * 100m, 1).ToString("0.0") + "%";
                    canvas.Draw("应缴合计：" + owner.YearReceivable.ToString("0.00") + " 元　　" +
                                "已缴合计：" + owner.YearPaid.ToString("0.00") + " 元　　" +
                                "当前欠费：" + owner.CurrentArrear.ToString("0.00") + " 元　　" +
                                "收缴率：" + rate,
                        bodyFont, 36);
                    canvas.Y += 22;

                    // 二、本年度账单明细（应缴侧）
                    string[] billHeads = { "账单期间", "收费项目", "缴费对象", "应缴", "已缴", "欠费", "状态", "到期日" };
                    double[] billXs = { 36, 136, 231, 321, 366, 411, 456, 494 };
                    double[] billW = { 98, 93, 88, 43, 43, 43, 36, 52 };
                    canvas.Section("二、本年度账单明细（" + bills.Count + " 张）", "二、本年度账单明细（续）",
                        headFont, smallFont, billHeads, billXs);
                    foreach (OwnerBillRow bill in bills)
                    {
                        canvas.Ensure(60, smallFont, "二、本年度账单明细（续）", billHeads, billXs);
                        string[] cells =
                        {
                            CycleText(bill.CycleStart, bill.CycleEnd), bill.ItemName, bill.ObjectText,
                            bill.Amount.ToString("0.00"), bill.PaidAmount.ToString("0.00"),
                            (bill.Amount - bill.PaidAmount).ToString("0.00"), BillStatusText(bill.Status),
                            Text(bill.DueAt)
                        };
                        for (int i = 0; i < cells.Length; i++)
                        {
                            canvas.Draw(FitToWidth(canvas.Gfx, cells[i] ?? string.Empty, bodyFont, billW[i]), bodyFont, billXs[i]);
                        }
                        canvas.Y += 13;
                    }
                    if (bills.Count == 0) { canvas.Draw("（本年度无账单）", bodyFont, 36); canvas.Y += 13; }
                    canvas.Y += 10;

                    // 三、本年度收款明细（实缴侧）
                    string[] payHeads = { "收款日期", "缴费对象", "收费项目", "账单期间", "收款金额", "方式", "收款流水号" };
                    double[] payXs = { 36, 114, 196, 288, 384, 430, 470 };
                    double[] payW = { 76, 80, 90, 94, 44, 38, 90 };
                    canvas.Section("三、本年度收款明细（" + payments.Count + " 笔）", "三、本年度收款明细（续）",
                        headFont, smallFont, payHeads, payXs);
                    foreach (OwnerPaymentRow pay in payments)
                    {
                        canvas.Ensure(60, smallFont, "三、本年度收款明细（续）", payHeads, payXs);
                        string[] cells =
                        {
                            Text(pay.PaidAt), pay.ObjectText, pay.ItemName,
                            CycleText(pay.CycleStart, pay.CycleEnd), pay.Amount.ToString("0.00"),
                            PayMethodText(pay.PayMethod), pay.BizNo
                        };
                        for (int i = 0; i < cells.Length; i++)
                        {
                            canvas.Draw(FitToWidth(canvas.Gfx, cells[i] ?? string.Empty, bodyFont, payW[i]), bodyFont, payXs[i]);
                        }
                        canvas.Y += 13;
                    }
                    if (payments.Count == 0) { canvas.Draw("（本年度无收款记录）", bodyFont, 36); canvas.Y += 13; }
                    canvas.Y += 12;

                    canvas.Draw("口径说明：1) 年度按「账期起始日」所属年度统计（无账期时按到期日），与仪表盘同口径；2) 账单归属含三类缴费对象 —— " +
                                "房产账单（按房产业主关系）、车位账单（按车位绑定业主）、业主直缴账单；" +
                                "3) 欠费 = 账单应收 − 已收（未结清账单）。",
                        smallFont, 36);
                }
                document.Save(filePath);
            }
        }

        /// <summary>全部业主 PDF（CHG-v1.2.0-17）：汇总表 + 逐户明细（概况 / 账单 / 收款），页满翻页。</summary>
        private static void WriteAllPdf(string filePath, int year, List<OwnerSection> items)
        {
            PdfFontSupport.Ensure();
            var titleFont = new XFont("SimHei", 15, XFontStyleEx.Bold);
            var headFont = new XFont("SimHei", 10, XFontStyleEx.Bold);
            var bodyFont = new XFont("SimHei", 9, XFontStyleEx.Regular);
            var smallFont = new XFont("SimHei", 8, XFontStyleEx.Regular);

            using (var document = new PdfDocument())
            {
                using (var canvas = new PdfCanvas(document))
                {
                    canvas.Draw("安怡物业 · 全部业主缴费概况与缴费明细", titleFont, 36);
                    canvas.Y += 22;
                    canvas.Draw("年度：" + year + " 年度　共 " + items.Count + " 户　导出时间：" +
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), bodyFont, 36);
                    canvas.Y += 20;

                    // 一、全部业主汇总缴费明细（逐户一行；CHG-v1.2.0-19：本模板只保留这一段；
                    // CHG-v1.2.0-20：新增「楼栋/房号」列 —— 与「收支明细流水」同格式 1号楼/1单元/101）
                    string[] sumHeads = { "业主", "楼栋/房号", "标识", "电话", "应缴", "已缴", "欠费", "收缴率" };
                    double[] sumXs = { 36, 152, 256, 302, 370, 414, 458, 502 };
                    double[] sumW = { 116, 104, 46, 68, 44, 44, 44, 42 };
                    canvas.Section("一、全部业主汇总缴费明细（" + items.Count + " 户）",
                        "一、全部业主汇总缴费明细（续）",
                        headFont, smallFont, sumHeads, sumXs);
                    foreach (OwnerSection it in items)
                    {
                        canvas.Ensure(60, smallFont, "一、全部业主汇总缴费明细（续）", sumHeads, sumXs);
                        string[] cells =
                        {
                            it.Owner.Name, it.PropertyText ?? "—", "YZ-" + it.Owner.Id.ToString("D3"),
                            string.IsNullOrWhiteSpace(it.Owner.Phone) ? "—" : it.Owner.Phone,
                            it.Owner.YearReceivable.ToString("0.00"), it.Owner.YearPaid.ToString("0.00"),
                            it.Owner.CurrentArrear.ToString("0.00"), RateText(it.Owner)
                        };
                        for (int i = 0; i < cells.Length; i++)
                        {
                            canvas.Draw(FitToWidth(canvas.Gfx, cells[i] ?? string.Empty, bodyFont, sumW[i]), bodyFont, sumXs[i]);
                        }
                        canvas.Y += 13;
                    }
                    if (items.Count == 0) { canvas.Draw("（没有业主档案）", bodyFont, 36); canvas.Y += 13; }
                    canvas.Y += 14;
                    canvas.Draw("口径说明：1) 年度按「账期起始日」所属年度统计（无账期时按到期日），与仪表盘同口径；" +
                                "2) 账单归属含房产 / 车位 / 业主直缴三类缴费对象；3) 欠费 = 账单应收 − 已收（未结清账单）；" +
                                "4) 逐户的账单与收款逐笔明细，请用「导出 PDF（单业主）」。",
                        smallFont, 36);
                }
                document.Save(filePath);
            }
        }

        /// <summary>收缴率文本（应收为 0 时显示 —）。</summary>
        private static string RateText(OwnerDto owner)
        {
            return owner.YearReceivable == 0m
                ? "—"
                : (Math.Round(owner.YearPaid / owner.YearReceivable * 100m, 1).ToString("0.0") + "%");
        }

        /// <summary>
        /// PDF 画布（CHG-v1.2.0-13）：页满自动翻页并重画表头。
        /// 说明：XGraphics 与页绑定，翻页必须整体替换 —— 用对象持有，避免「ref 参数换页后原引用失效」。
        /// </summary>
        private sealed class PdfCanvas : IDisposable
        {
            private readonly PdfDocument _document;
            private PdfPage _page;
            public double Y { get; set; }
            public XGraphics Gfx { get; private set; }

            public PdfCanvas(PdfDocument document)
            {
                _document = document;
                AddPage();
            }

            private void AddPage()
            {
                _page = _document.AddPage();
                _page.Size = PdfSharp.PageSize.A4;
                Gfx = XGraphics.FromPdfPage(_page);
                Y = 30;
            }

            public void Draw(string text, XFont font, double x)
            {
                Gfx.DrawString(text ?? string.Empty, font, XBrushes.Black, x, Y);
            }

            public void Section(string title, string continuedTitle, XFont headFont, XFont smallFont,
                string[] heads, double[] xs)
            {
                Ensure(50, smallFont, continuedTitle, heads, xs);
                Draw(title, headFont, 36);
                Y += 16;
                DrawRow(smallFont, heads, xs);
                Y += 14;
            }

            /// <summary>剩余空间不足时翻页，并在新页重画续表标题与表头。</summary>
            public void Ensure(double need, XFont headFont, string continuedTitle, string[] heads, double[] xs)
            {
                if (Y <= _page.Height.Point - need) { return; }
                Gfx.Dispose();
                AddPage();
                Draw(continuedTitle, headFont, 36);
                Y += 16;
                DrawRow(headFont, heads, xs);
                Y += 14;
            }

            /// <summary>画一行（表头/数据行共用；供外层「全部业主 PDF」逐户表格复用）。</summary>
            public void DrawRow(XFont font, string[] cells, double[] xs)
            {
                for (int i = 0; i < cells.Length && i < xs.Length; i++)
                {
                    Gfx.DrawString(cells[i] ?? string.Empty, font, XBrushes.Black, xs[i], Y);
                }
            }

            public void Dispose()
            {
                if (Gfx != null) { Gfx.Dispose(); Gfx = null; }
            }
        }

        private static string FitToWidth(XGraphics gfx, string text, XFont font, double width)
        {
            if (string.IsNullOrEmpty(text)) { return string.Empty; }
            if (gfx.MeasureString(text, font).Width <= width) { return text; }
            string cut = text;
            while (cut.Length > 1 && gfx.MeasureString(cut + "…", font).Width > width) { cut = cut.Substring(0, cut.Length - 1); }
            return cut + "…";
        }

        private static string Text(string value) { return string.IsNullOrWhiteSpace(value) ? "—" : value; }

        private static string Text(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "—";
        }

        private static string CycleText(string start, string end)
        {
            // 版面口径（CHG-v1.2.0-13）：同年账期缩写为 2026.01.01~12.31，避免列宽不足被截断
            DateTime s, e;
            bool okS = DateTime.TryParse(start, out s);
            bool okE = DateTime.TryParse(end, out e);
            if (!okS && !okE) { return "—"; }
            if (okS && okE && s.Year == e.Year)
            {
                return s.ToString("yyyy.MM.dd") + "~" + e.ToString("MM.dd");
            }
            return (okS ? s.ToString("yyyy.MM.dd") : start) + "~" + (okE ? e.ToString("yyyy.MM.dd") : end);
        }

        private static string PayMethodText(PayMethod method)
        {
            switch (method)
            {
                case PayMethod.Cash: return "现金";
                case PayMethod.WeChat: return "微信";
                case PayMethod.BankTransfer: return "银行转账";
                case PayMethod.Pos: return "POS";
                default: return "转账";
            }
        }

        private static string BillStatusText(BillStatus status)
        {
            switch (status)
            {
                case BillStatus.Partial: return "部分缴";
                case BillStatus.Overdue: return "逾期";
                case BillStatus.Paid: return "已缴";
                case BillStatus.Reversed: return "已冲正";
                default: return "待缴";
            }
        }

        internal class OwnerBillRow
        {
            public int BillId { get; set; }
            public decimal Amount { get; set; }
            public decimal PaidAmount { get; set; }
            public BillStatus Status { get; set; }
            public DateTime? DueAt { get; set; }
            public string ItemName { get; set; }
            public string CycleStart { get; set; }
            public string CycleEnd { get; set; }
            public string ObjectText { get; set; }
        }

        internal class OwnerPaymentRow
        {
            public int PaymentId { get; set; }
            public DateTime? PaidAt { get; set; }
            public decimal Amount { get; set; }
            public PayMethod PayMethod { get; set; }
            public string BizNo { get; set; }
            public string ItemName { get; set; }
            public string CycleStart { get; set; }
            public string CycleEnd { get; set; }
            public string ObjectText { get; set; }
        }
    }
}
