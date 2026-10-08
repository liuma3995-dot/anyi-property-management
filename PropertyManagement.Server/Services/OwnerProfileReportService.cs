using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
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

        /// <summary>
        /// CHG-v1.4.0-14（负责人 2026-10-08 第 6 轮反馈「全部业主 Excel 表格模板与 PDF 不一致」）：
        /// 全部业主 Excel —— 工作表①「缴费概况」与「导出全部 PDF」的汇总表**同列同顺序**（含合计行），
        /// CHG-v1.4.0-15（第 7 轮反馈「缴费概况和明细 Excel 缺少账单明细与收款明细」）：补工作表②「账单明细」、
        /// 工作表③「收款明细」（每户逐笔，列与「导出 PDF（单业主）」同口径 + 前置「业主」列），
        /// 工作表④「业主档案」保留原「导出 Excel（业主档案表格）」的 9 列，原有能力不丢。
        /// 口径与单业主 / 全部业主 PDF 完全一致（往年账期但本年度有收款的账单同样计入）。
        /// </summary>
        public ReportLogDto ExportAllExcel(int? year, string operatorName)
        {
            int statYear = year.HasValue && year.Value > 2000 ? year.Value : DateTime.Today.Year;

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                List<OwnerDto> owners = _baseInfo.QueryOwnersWithYearStats(connection,
                    new BaseInfoQueryRequest { PageIndex = 1, PageSize = 100000 }, statYear, out int _)
                    ?? new List<OwnerDto>();
                Dictionary<int, List<string>> paths = _baseInfo.QueryOwnerPropertyPaths(connection, owners.Select(o => o.Id));
                var items = owners.Select(o => new OwnerSection
                {
                    Owner = o,
                    PropertyText = FormatPropertyPaths(paths.ContainsKey(o.Id) ? paths[o.Id] : null)
                }).ToList();
                // CHG-v1.4.0-15：全量逐笔明细（一次查询，不按户循环）
                List<OwnerBillRow> bills = QueryAllBills(connection, statYear);
                List<OwnerPaymentRow> payments = QueryAllPayments(connection, statYear);

                string period = DateTime.Now.ToString("yyyyMMddHHmmss");
                string fileName = "owner_profile_all_" + period + ".xlsx";
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);
                WriteAllExcel(filePath, statYear, items, bills, payments);

                var log = new ReportLogDto
                {
                    ReportType = ReportType,
                    Period = statYear + "-ALL",
                    Format = ExportFormat.Excel,
                    FilePath = filePath
                };
                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    log.Id = _finance.InsertReportLog(connection, transaction, log);
                    transaction.Commit();
                }

                _audit.Write("OWNER_PROFILE_EXPORT_ALL", "owner", "all",
                    "业主档案导出 Excel（全部业主）：" + statYear + " 年度，" + items.Count + " 户 / 账单 " +
                    bills.Count + " 张 / 收款 " + payments.Count + " 笔，文件 " + fileName,
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
                // CHG-v1.4.0-12（负责人 2026-10-08 第 2 轮反馈）：明细口径与「缴费概况」对齐 ——
                // 除本年账期账单外，还要列出**往年账期但本年度有收款/冲减**的账单（往年旧账单本年缴费的场景），
                // 否则业主只能看到概况、看不到对应的账单明细。
                "WHERE b.del_flag = 0 AND (" + YearFilterSql + " = @year OR EXISTS (" +
                "  SELECT 1 FROM t_payment p2 WHERE p2.bill_id = b.id AND p2.status = 0 " +
                "    AND strftime('%Y', p2.paid_at) = @year) OR EXISTS (" +
                "  SELECT 1 FROM t_payment_refund rf2 WHERE rf2.bill_id = b.id AND rf2.del_flag = 0 " +
                "    AND strftime('%Y', rf2.created_at) = @year)) " + "AND " + OwnerScopeSql + " " +
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
                // CHG-v1.4.0-12：收款明细改为按**收款发生年度**归属（原按账单账期年度，导致「往年账单本年收款」
                // 在收款明细里彻底消失）；与收支明细流水 / 财务报表的实收口径一致。
                "WHERE p.status = 0 AND b.del_flag = 0 AND strftime('%Y', p.paid_at) = @year AND " + OwnerScopeSql + " " +
                "ORDER BY p.paid_at, p.id",
                new { ownerId, year = year.ToString() }).ToList();
        }

        /// <summary>
        /// CHG-v1.4.0-15：「账单 ↔ 业主」归属对（与单业主 <see cref="OwnerScopeSql"/> 同口径的
        /// 三类缴费对象：房产按业主关系、车位按绑定业主并回落车位所属房产的业主、业主直缴按 owner_id）。
        /// 全部业主逐笔明细用一次查询取全量，避免按户循环（N+1）。
        /// </summary>
        private const string OwnerScopePairsCte =
            "WITH pairs AS (" +
            " SELECT b.id AS BillId, r.owner_id AS OwnerId FROM t_bill b" +
            "   JOIN t_owner_property_rel r ON r.property_id = b.property_id AND r.del_flag = 0" +
            "   WHERE b.property_id IS NOT NULL" +
            " UNION" +
            " SELECT b.id, pk.owner_id FROM t_bill b JOIN t_parking_space pk ON pk.id = b.parking_id" +
            "   WHERE b.parking_id IS NOT NULL AND pk.del_flag = 0 AND pk.owner_id IS NOT NULL" +
            " UNION" +
            " SELECT b.id, r2.owner_id FROM t_bill b" +
            "   JOIN t_parking_space pk2 ON pk2.id = b.parking_id AND pk2.del_flag = 0" +
            "   JOIN t_owner_property_rel r2 ON r2.property_id = pk2.property_id AND r2.del_flag = 0" +
            "   WHERE b.parking_id IS NOT NULL" +
            " UNION" +
            " SELECT b.id, b.owner_id FROM t_bill b" +
            "   WHERE b.owner_id IS NOT NULL AND b.property_id IS NULL AND b.parking_id IS NULL" +
            ") ";

        /// <summary>全部业主本年度账单明细（应缴侧，含业主归属；口径与单业主明细一致）。</summary>
        private static List<OwnerBillRow> QueryAllBills(IDbConnection connection, int year)
        {
            return connection.Query<OwnerBillRow>(
                OwnerScopePairsCte +
                "SELECT p.OwnerId AS OwnerId, b.id AS BillId, b.amount AS Amount, b.paid_amount AS PaidAmount, " +
                "b.status AS Status, b.due_at AS DueAt, COALESCE(ci.name, '') AS ItemName, " +
                "COALESCE(c.start_date, '') AS CycleStart, COALESCE(c.end_date, '') AS CycleEnd, " +
                ObjectTextSql + " AS ObjectText " +
                "FROM t_bill b JOIN pairs p ON p.BillId = b.id " +
                "LEFT JOIN t_charge_item ci ON ci.id = b.charge_item_id " +
                "LEFT JOIN t_billing_cycle c ON c.id = b.cycle_id " +
                "WHERE b.del_flag = 0 AND (" + YearFilterSql + " = @year OR EXISTS (" +
                "  SELECT 1 FROM t_payment p2 WHERE p2.bill_id = b.id AND p2.status = 0 " +
                "    AND strftime('%Y', p2.paid_at) = @year) OR EXISTS (" +
                "  SELECT 1 FROM t_payment_refund rf2 WHERE rf2.bill_id = b.id AND rf2.del_flag = 0 " +
                "    AND strftime('%Y', rf2.created_at) = @year)) " +
                "ORDER BY p.OwnerId, b.due_at, b.id",
                new { year = year.ToString() }).ToList();
        }

        /// <summary>全部业主本年度收款明细（实缴侧，按收款发生年度归属，含业主归属）。</summary>
        private static List<OwnerPaymentRow> QueryAllPayments(IDbConnection connection, int year)
        {
            return connection.Query<OwnerPaymentRow>(
                OwnerScopePairsCte +
                "SELECT p.OwnerId AS OwnerId, pay.id AS PaymentId, pay.paid_at AS PaidAt, pay.amount AS Amount, " +
                "pay.pay_method AS PayMethod, COALESCE(NULLIF(pay.batch_no, ''), r.receipt_no, '') AS BizNo, " +
                "COALESCE(ci.name, '') AS ItemName, COALESCE(c.start_date, '') AS CycleStart, " +
                "COALESCE(c.end_date, '') AS CycleEnd, " + ObjectTextSql + " AS ObjectText " +
                "FROM t_payment pay JOIN pairs p ON p.BillId = pay.bill_id " +
                "LEFT JOIN t_receipt r ON r.payment_id = pay.id " +
                "LEFT JOIN t_bill b ON b.id = pay.bill_id " +
                "LEFT JOIN t_charge_item ci ON ci.id = b.charge_item_id " +
                "LEFT JOIN t_billing_cycle c ON c.id = b.cycle_id " +
                "WHERE pay.status = 0 AND b.del_flag = 0 AND strftime('%Y', pay.paid_at) = @year " +
                "ORDER BY p.OwnerId, pay.paid_at, pay.id",
                new { year = year.ToString() }).ToList();
        }

        /// <summary>
        /// 年度归属口径（CHG-v1.2.0-16）：账期起始日所在年度，无账期时回落到期日 —— 与仪表盘 / 业主概况同源。
        /// </summary>
        private const string YearFilterSql = "COALESCE(strftime('%Y', c.start_date), strftime('%Y', b.due_at))";

        // ==================== PDF 绘制 ====================

        /// <summary>
        /// CHG-v1.4.0-13（负责人 2026-10-08 第 2 轮反馈「业主档案导出 Excel 看不出账单/收款明细」）：
        /// 导出该业主的**缴费概况 + 账单明细 + 收款明细** Excel（三个工作表），与 PDF 同口径同数据源
        /// （往年账期但在本年度收款/冲减的账单同样列入，收款按收款发生年度归属）。
        /// CHG-v1.4.0-14：三张工作表的表头 / 小节标题 / 口径说明与「导出 PDF（单业主）」逐行对齐
        /// （表格结构一致，Excel 另含 合计 行）。全部业主的「业主档案表格」仍在「导出 Excel」的第二张工作表。
        /// </summary>
        public ReportLogDto ExportExcel(int ownerId, int? year, string operatorName)
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
                string fileName = "owner_profile_" + ownerId + "_" + period + ".xlsx";
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);
                WriteExcel(filePath, owner, statYear, bills, payments);

                var log = new ReportLogDto
                {
                    ReportType = ReportType,
                    Period = statYear + "-" + ownerId,
                    Format = ExportFormat.Excel,
                    FilePath = filePath
                };
                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    log.Id = _finance.InsertReportLog(connection, transaction, log);
                    transaction.Commit();
                }

                _audit.Write("OWNER_PROFILE_EXPORT", "owner", ownerId.ToString(),
                    "业主档案导出 Excel：" + owner.Name + "，" + statYear + " 年度，账单 " + bills.Count +
                    " 张 / 收款 " + payments.Count + " 笔，文件 " + fileName,
                    null, operatorName, "基础信息", "成功");

                return _finance.GetReportLog(connection, log.Id) ?? log;
            }
        }

        /// <summary>
        /// CHG-v1.4.0-14（负责人 2026-10-08 第 6 轮反馈「该业主 Excel 表格模板与 PDF 不一致」）：
        /// 该业主 Excel 与「导出 PDF（单业主）」**同节同列同顺序**，两处可直接逐行核对：
        /// 一、缴费概况（统计项 / 金额·比率）→ 二、本年度账单明细（含合计）→ 三、本年度收款明细（含合计）→ 口径说明。
        /// </summary>
        private static void WriteExcel(string filePath, OwnerDto owner, int year,
            List<OwnerBillRow> bills, List<OwnerPaymentRow> payments)
        {
            using (var workbook = new XLWorkbook())
            {
                // 一、缴费概况（与 PDF「一、本年度缴费概况」同一张两列表）
                var sheet = workbook.Worksheets.Add("缴费概况");
                sheet.Cell(1, 1).Value = "安怡物业 · 业主缴费概况与缴费明细";
                sheet.Cell(1, 1).Style.Font.Bold = true;
                sheet.Cell(2, 1).Value = "业主：" + owner.Name + "（YZ-" + owner.Id.ToString("D3") + "）　电话：" + PhoneText(owner.Phone);
                sheet.Cell(3, 1).Value = "年度：" + year + " 年度　导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                sheet.Cell(4, 1).Value = StatScopeLine;
                sheet.Cell(4, 1).Style.Font.Italic = true;
                sheet.Cell(6, 1).Value = "一、本年度缴费概况";
                sheet.Cell(6, 1).Style.Font.Bold = true;

                string[] statHeads = { "统计项", "金额 / 比率" };
                const int statHeadRow = 7;
                for (int i = 0; i < statHeads.Length; i++)
                {
                    sheet.Cell(statHeadRow, i + 1).Value = statHeads[i];
                    sheet.Cell(statHeadRow, i + 1).Style.Font.Bold = true;
                }
                // CHG-v1.4.0-16：金额列写**数值**（原来写文本 → Excel 里是「文本型数字」，不能直接求和、
                // 单元格带绿三角），并按 PDF 口径保留两位小数；收缴率仍是百分比文本。
                string[] statLabels = { "应缴合计（含往年结转）", "已缴合计（含补缴往年）", "当前欠费", "收缴率" };
                decimal[] statValues = { owner.YearReceivable, owner.YearPaid, owner.CurrentArrear };
                for (int i = 0; i < statLabels.Length; i++)
                {
                    sheet.Cell(statHeadRow + 1 + i, 1).Value = statLabels[i];
                    IXLCell valueCell = sheet.Cell(statHeadRow + 1 + i, 2);
                    if (i < statValues.Length)
                    {
                        valueCell.Value = statValues[i];
                        valueCell.Style.NumberFormat.Format = "0.00";
                    }
                    else
                    {
                        valueCell.Value = RateText(owner);
                    }
                }
                sheet.Column(1).Width = 26;
                sheet.Column(2).Width = 16;

                int noteRow = statHeadRow + statLabels.Length + 2;
                for (int i = 0; i < ScopeNotesSingle.Length; i++)
                {
                    sheet.Cell(noteRow + i, 1).Value = ScopeNotesSingle[i];
                }

                // 二、账单明细（应缴侧）
                var billSheet = workbook.Worksheets.Add("账单明细");
                billSheet.Cell(1, 1).Value = "二、本年度账单明细（" + bills.Count + " 张）";
                billSheet.Cell(1, 1).Style.Font.Bold = true;
                string[] billHeads = { "账单期间", "收费项目", "缴费对象", "应缴", "已缴", "欠费", "状态", "到期日" };
                int rb = 2;
                for (int i = 0; i < billHeads.Length; i++)
                {
                    billSheet.Cell(rb, i + 1).Value = billHeads[i];
                    billSheet.Cell(rb, i + 1).Style.Font.Bold = true;
                }
                rb++;
                foreach (OwnerBillRow bill in bills)
                {
                    billSheet.Cell(rb, 1).Value = CycleText(bill.CycleStart, bill.CycleEnd);
                    billSheet.Cell(rb, 2).Value = bill.ItemName ?? string.Empty;
                    billSheet.Cell(rb, 3).Value = bill.ObjectText ?? string.Empty;
                    billSheet.Cell(rb, 4).Value = bill.Amount;
                    billSheet.Cell(rb, 5).Value = bill.PaidAmount;
                    billSheet.Cell(rb, 6).Value = bill.Amount - bill.PaidAmount;
                    billSheet.Cell(rb, 7).Value = BillStatusText(bill.Status);
                    billSheet.Cell(rb, 8).Value = Text(bill.DueAt);
                    rb++;
                }
                if (bills.Count > 0)
                {
                    billSheet.Cell(rb, 1).Value = "合计";
                    billSheet.Cell(rb, 4).Value = bills.Sum(x => x.Amount);
                    billSheet.Cell(rb, 5).Value = bills.Sum(x => x.PaidAmount);
                    billSheet.Cell(rb, 6).Value = bills.Sum(x => x.Amount - x.PaidAmount);
                    for (int c = 1; c <= billHeads.Length; c++) { billSheet.Cell(rb, c).Style.Font.Bold = true; }
                }
                for (int c = 1; c <= billHeads.Length; c++) { billSheet.Column(c).Width = 16; }

                // 三、收款明细（实缴侧）
                var paySheet = workbook.Worksheets.Add("收款明细");
                paySheet.Cell(1, 1).Value = "三、本年度收款明细（" + payments.Count + " 笔）";
                paySheet.Cell(1, 1).Style.Font.Bold = true;
                string[] payHeads = { "收款日期", "缴费对象", "收费项目", "账单期间", "收款金额", "方式", "收款流水号" };
                int rp = 2;
                for (int i = 0; i < payHeads.Length; i++)
                {
                    paySheet.Cell(rp, i + 1).Value = payHeads[i];
                    paySheet.Cell(rp, i + 1).Style.Font.Bold = true;
                }
                rp++;
                foreach (OwnerPaymentRow pay in payments)
                {
                    paySheet.Cell(rp, 1).Value = Text(pay.PaidAt);
                    paySheet.Cell(rp, 2).Value = pay.ObjectText ?? string.Empty;
                    paySheet.Cell(rp, 3).Value = pay.ItemName ?? string.Empty;
                    paySheet.Cell(rp, 4).Value = CycleText(pay.CycleStart, pay.CycleEnd);
                    paySheet.Cell(rp, 5).Value = pay.Amount;
                    paySheet.Cell(rp, 6).Value = PayMethodText(pay.PayMethod);
                    paySheet.Cell(rp, 7).Value = pay.BizNo ?? string.Empty;
                    rp++;
                }
                if (payments.Count > 0)
                {
                    paySheet.Cell(rp, 1).Value = "合计";
                    paySheet.Cell(rp, 5).Value = payments.Sum(x => x.Amount);
                    for (int c = 1; c <= payHeads.Length; c++) { paySheet.Cell(rp, c).Style.Font.Bold = true; }
                }
                for (int c = 1; c <= payHeads.Length; c++) { paySheet.Column(c).Width = 18; }

                workbook.SaveAs(filePath);
            }
        }

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
                                PhoneText(owner.Phone) +
                                "　年度：" + year + " 年度　导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        bodyFont, 36);
                    canvas.Y += 18;
                    canvas.Draw(StatScopeLine, smallFont, 36);
                    canvas.Y += 20;

                    // 一、本年度缴费概况（CHG-v1.4.0-14：由整行文本改为两列表，与 Excel「一、缴费概况」同表头同顺序）
                    canvas.Draw("一、本年度缴费概况", headFont, 36);
                    canvas.Y += 16;
                    double[] statXs = { 36, 210 };
                    canvas.DrawRow(smallFont, new[] { "统计项", "金额 / 比率" }, statXs);
                    canvas.Y += 13;
                    foreach (string[] stat in StatRows(owner))
                    {
                        canvas.DrawRow(bodyFont, stat, statXs);
                        canvas.Y += 13;
                    }
                    canvas.Y += 10;

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
                    else
                    {
                        // CHG-v1.4.0-14：补合计行（与 Excel「二、账单明细」一致）
                        canvas.Ensure(60, smallFont, "二、本年度账单明细（续）", billHeads, billXs);
                        string[] totals =
                        {
                            "合计", string.Empty, string.Empty,
                            bills.Sum(x => x.Amount).ToString("0.00"),
                            bills.Sum(x => x.PaidAmount).ToString("0.00"),
                            bills.Sum(x => x.Amount - x.PaidAmount).ToString("0.00"),
                            string.Empty, string.Empty
                        };
                        for (int i = 0; i < totals.Length; i++)
                        {
                            canvas.Draw(FitToWidth(canvas.Gfx, totals[i], bodyFont, billW[i]), bodyFont, billXs[i]);
                        }
                        canvas.Y += 13;
                    }
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
                    else
                    {
                        // CHG-v1.4.0-14：补合计行（与 Excel「三、收款明细」一致）
                        canvas.Ensure(60, smallFont, "三、本年度收款明细（续）", payHeads, payXs);
                        string[] totals =
                        {
                            "合计", string.Empty, string.Empty, string.Empty,
                            payments.Sum(x => x.Amount).ToString("0.00"), string.Empty, string.Empty
                        };
                        for (int i = 0; i < totals.Length; i++)
                        {
                            canvas.Draw(FitToWidth(canvas.Gfx, totals[i], bodyFont, payW[i]), bodyFont, payXs[i]);
                        }
                        canvas.Y += 13;
                    }
                    canvas.Y += 12;

                    // 口径说明（CHG-v1.4.0-14：与 Excel 概况页同文，逐行输出避免超宽截断）
                    foreach (string note in ScopeNotesSingle)
                    {
                        canvas.Draw(note, smallFont, 36);
                        canvas.Y += 13;
                    }
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
                    // CHG-v1.4.0-02：口径说明随表头输出（应缴含往年结转、已缴含本年补缴往年）
                    // CHG-v1.4.0-14：口径行独立成行（与「导出 Excel（全部业主）」概况页同排版，避免超宽）
                    canvas.Draw("年度：" + year + " 年度　共 " + items.Count + " 户　导出时间：" +
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), bodyFont, 36);
                    canvas.Y += 15;
                    canvas.Draw(StatScopeLine, smallFont, 36);
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
                    else
                    {
                        // CHG-v1.4.0-14：补合计行（与「导出 Excel（全部业主）」工作表①同列同顺序）
                        canvas.Ensure(60, smallFont, "一、全部业主汇总缴费明细（续）", sumHeads, sumXs);
                        decimal receivable = items.Sum(x => x.Owner.YearReceivable);
                        decimal paid = items.Sum(x => x.Owner.YearPaid);
                        string[] totals =
                        {
                            "合计", string.Empty, string.Empty, string.Empty,
                            receivable.ToString("0.00"), paid.ToString("0.00"),
                            items.Sum(x => x.Owner.CurrentArrear).ToString("0.00"),
                            receivable == 0m ? "—" : Math.Round(paid / receivable * 100m, 1).ToString("0.0") + "%"
                        };
                        for (int i = 0; i < totals.Length; i++)
                        {
                            canvas.Draw(FitToWidth(canvas.Gfx, totals[i], bodyFont, sumW[i]), bodyFont, sumXs[i]);
                        }
                        canvas.Y += 13;
                    }
                    canvas.Y += 12;
                    // 口径说明（CHG-v1.4.0-14：与 Excel 概况页同文）
                    foreach (string note in ScopeNotesAll)
                    {
                        canvas.Draw(note, smallFont, 36);
                        canvas.Y += 13;
                    }
                }
                document.Save(filePath);
            }
        }

        /// <summary>
        /// 全部业主 Excel（CHG-v1.4.0-14）：工作表①「一、缴费概况」= 与「导出全部 PDF」同列同顺序的汇总表（含合计行）；
        /// 工作表②「账单明细」/ ③「收款明细」= 每户逐笔（CHG-v1.4.0-15，列与单业主 PDF 同口径 + 前置「业主」列）；
        /// 工作表④「业主档案」= 原「导出 Excel（业主档案表格）」的 9 列，供档案核对。
        /// </summary>
        private static void WriteAllExcel(string filePath, int year, List<OwnerSection> items,
            List<OwnerBillRow> bills, List<OwnerPaymentRow> payments)
        {
            // 明细行的「业主」列文本（业主姓名 + 编号，避免同名业主分不清）
            var ownerNames = items.ToDictionary(
                x => x.Owner.Id,
                x => (x.Owner.Name ?? string.Empty) + "（YZ-" + x.Owner.Id.ToString("D3") + "）");
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("缴费概况");
                sheet.Cell(1, 1).Value = "安怡物业 · 全部业主缴费概况与缴费明细";
                sheet.Cell(1, 1).Style.Font.Bold = true;
                sheet.Cell(2, 1).Value = "年度：" + year + " 年度　共 " + items.Count + " 户　导出时间：" +
                                         DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                sheet.Cell(3, 1).Value = StatScopeLine;
                sheet.Cell(3, 1).Style.Font.Italic = true;

                string[] sumHeads = { "业主", "楼栋/房号", "标识", "电话", "应缴", "已缴", "欠费", "收缴率" };
                sheet.Cell(5, 1).Value = "一、全部业主汇总缴费明细（" + items.Count + " 户）";
                sheet.Cell(5, 1).Style.Font.Bold = true;
                const int sumHeadRow = 6;
                for (int i = 0; i < sumHeads.Length; i++)
                {
                    sheet.Cell(sumHeadRow, i + 1).Value = sumHeads[i];
                    sheet.Cell(sumHeadRow, i + 1).Style.Font.Bold = true;
                }
                int r = sumHeadRow + 1;
                foreach (OwnerSection it in items)
                {
                    sheet.Cell(r, 1).Value = it.Owner.Name;
                    sheet.Cell(r, 2).Value = string.IsNullOrWhiteSpace(it.PropertyText) ? "—" : it.PropertyText;
                    sheet.Cell(r, 3).Value = "YZ-" + it.Owner.Id.ToString("D3");
                    sheet.Cell(r, 4).Value = PhoneText(it.Owner.Phone);
                    sheet.Cell(r, 5).Value = it.Owner.YearReceivable;
                    sheet.Cell(r, 6).Value = it.Owner.YearPaid;
                    sheet.Cell(r, 7).Value = it.Owner.CurrentArrear;
                    sheet.Cell(r, 8).Value = RateText(it.Owner);
                    r++;
                }
                if (items.Count > 0)
                {
                    decimal receivable = items.Sum(x => x.Owner.YearReceivable);
                    decimal paid = items.Sum(x => x.Owner.YearPaid);
                    sheet.Cell(r, 1).Value = "合计";
                    sheet.Cell(r, 5).Value = receivable;
                    sheet.Cell(r, 6).Value = paid;
                    sheet.Cell(r, 7).Value = items.Sum(x => x.Owner.CurrentArrear);
                    sheet.Cell(r, 8).Value = receivable == 0m
                        ? "—"
                        : Math.Round(paid / receivable * 100m, 1).ToString("0.0") + "%";
                    for (int c = 1; c <= sumHeads.Length; c++) { sheet.Cell(r, c).Style.Font.Bold = true; }
                    r++;
                }
                for (int i = 0; i < ScopeNotesAll.Length; i++)
                {
                    sheet.Cell(r + 1 + i, 1).Value = ScopeNotesAll[i];
                }
                sheet.Column(1).Width = 14;
                sheet.Column(2).Width = 26;
                sheet.Column(3).Width = 10;
                sheet.Column(4).Width = 16;
                for (int c = 5; c <= sumHeads.Length; c++) { sheet.Column(c).Width = 12; }

                // 工作表②：账单明细（每户逐笔；列 = 单业主 PDF 账单明细 + 前置「业主」）
                var billSheet = workbook.Worksheets.Add("账单明细");
                billSheet.Cell(1, 1).Value = "二、本年度账单明细（" + bills.Count + " 张）";
                billSheet.Cell(1, 1).Style.Font.Bold = true;
                string[] billHeads = { "业主", "账单期间", "收费项目", "缴费对象", "应缴", "已缴", "欠费", "状态", "到期日" };
                for (int i = 0; i < billHeads.Length; i++)
                {
                    billSheet.Cell(2, i + 1).Value = billHeads[i];
                    billSheet.Cell(2, i + 1).Style.Font.Bold = true;
                }
                int rb = 3;
                foreach (OwnerBillRow bill in bills)
                {
                    billSheet.Cell(rb, 1).Value = OwnerLabel(ownerNames, bill.OwnerId);
                    billSheet.Cell(rb, 2).Value = CycleText(bill.CycleStart, bill.CycleEnd);
                    billSheet.Cell(rb, 3).Value = bill.ItemName ?? string.Empty;
                    billSheet.Cell(rb, 4).Value = bill.ObjectText ?? string.Empty;
                    billSheet.Cell(rb, 5).Value = bill.Amount;
                    billSheet.Cell(rb, 6).Value = bill.PaidAmount;
                    billSheet.Cell(rb, 7).Value = bill.Amount - bill.PaidAmount;
                    billSheet.Cell(rb, 8).Value = BillStatusText(bill.Status);
                    billSheet.Cell(rb, 9).Value = Text(bill.DueAt);
                    rb++;
                }
                if (bills.Count > 0)
                {
                    billSheet.Cell(rb, 1).Value = "合计";
                    billSheet.Cell(rb, 5).Value = bills.Sum(x => x.Amount);
                    billSheet.Cell(rb, 6).Value = bills.Sum(x => x.PaidAmount);
                    billSheet.Cell(rb, 7).Value = bills.Sum(x => x.Amount - x.PaidAmount);
                    for (int c = 1; c <= billHeads.Length; c++) { billSheet.Cell(rb, c).Style.Font.Bold = true; }
                }
                // 列宽：与表头/内容宽度匹配（「楼栋/单元/房号」「账单期间」最长，各留余量）
                double[] billWidths = { 22, 18, 18, 20, 12, 12, 12, 10, 14 };
                for (int c = 1; c <= billHeads.Length; c++) { billSheet.Column(c).Width = billWidths[c - 1]; }

                // 工作表③：收款明细（每户逐笔；列 = 单业主 PDF 收款明细 + 前置「业主」）
                var paySheet = workbook.Worksheets.Add("收款明细");
                paySheet.Cell(1, 1).Value = "三、本年度收款明细（" + payments.Count + " 笔）";
                paySheet.Cell(1, 1).Style.Font.Bold = true;
                string[] payHeads = { "业主", "收款日期", "缴费对象", "收费项目", "账单期间", "收款金额", "方式", "收款流水号" };
                for (int i = 0; i < payHeads.Length; i++)
                {
                    paySheet.Cell(2, i + 1).Value = payHeads[i];
                    paySheet.Cell(2, i + 1).Style.Font.Bold = true;
                }
                int rp = 3;
                foreach (OwnerPaymentRow pay in payments)
                {
                    paySheet.Cell(rp, 1).Value = OwnerLabel(ownerNames, pay.OwnerId);
                    paySheet.Cell(rp, 2).Value = Text(pay.PaidAt);
                    paySheet.Cell(rp, 3).Value = pay.ObjectText ?? string.Empty;
                    paySheet.Cell(rp, 4).Value = pay.ItemName ?? string.Empty;
                    paySheet.Cell(rp, 5).Value = CycleText(pay.CycleStart, pay.CycleEnd);
                    paySheet.Cell(rp, 6).Value = pay.Amount;
                    paySheet.Cell(rp, 7).Value = PayMethodText(pay.PayMethod);
                    paySheet.Cell(rp, 8).Value = pay.BizNo ?? string.Empty;
                    rp++;
                }
                if (payments.Count > 0)
                {
                    paySheet.Cell(rp, 1).Value = "合计";
                    paySheet.Cell(rp, 6).Value = payments.Sum(x => x.Amount);
                    for (int c = 1; c <= payHeads.Length; c++) { paySheet.Cell(rp, c).Style.Font.Bold = true; }
                }
                double[] payWidths = { 22, 14, 20, 18, 18, 12, 10, 18 };
                for (int c = 1; c <= payHeads.Length; c++) { paySheet.Column(c).Width = payWidths[c - 1]; }

                // 工作表④：业主档案（原「导出 Excel（业主档案表格）」口径，功能不丢）
                var archive = workbook.Worksheets.Add("业主档案");
                string[] archiveHeads = { "业主编号", "姓名", "证件类型", "证件号", "联系电话", "入住日期", "常住地址", "紧急联系人", "状态" };
                for (int i = 0; i < archiveHeads.Length; i++)
                {
                    archive.Cell(1, i + 1).Value = archiveHeads[i];
                    archive.Cell(1, i + 1).Style.Font.Bold = true;
                }
                int ra = 2;
                foreach (OwnerSection it in items)
                {
                    OwnerDto o = it.Owner;
                    archive.Cell(ra, 1).Value = o.Id;
                    archive.Cell(ra, 2).Value = o.Name ?? string.Empty;
                    archive.Cell(ra, 3).Value = IdCardTypeText(o.IdCardType);
                    archive.Cell(ra, 4).Value = o.IdCard ?? string.Empty;
                    archive.Cell(ra, 5).Value = o.Phone ?? string.Empty;
                    archive.Cell(ra, 6).Value = o.CheckInDate.HasValue ? o.CheckInDate.Value.ToString("yyyy-MM-dd") : string.Empty;
                    archive.Cell(ra, 7).Value = o.ResidentAddress ?? string.Empty;
                    archive.Cell(ra, 8).Value = ((o.EmergencyContactName ?? string.Empty) + " " +
                                                 (o.EmergencyContactPhone ?? string.Empty)).Trim();
                    archive.Cell(ra, 9).Value = o.Status == OwnerStatus.Living ? "在住" : "搬离";
                    ra++;
                }
                for (int c = 1; c <= archiveHeads.Length; c++) { archive.Column(c).Width = 16; }

                workbook.SaveAs(filePath);
            }
        }

        /// <summary>证件类型文案（与业主档案列表口径一致）。</summary>
        private static string IdCardTypeText(OwnerIdCardType type)
        {
            switch (type)
            {
                case OwnerIdCardType.Passport: return "护照";
                case OwnerIdCardType.Hukou: return "户口簿";
                case OwnerIdCardType.Other: return "其他";
                default: return "身份证";
            }
        }

        // ========== CHG-v1.4.0-14：Excel 与 PDF 共用同一套标题 / 表头 / 口径文案，保证两份模板逐行一致 ==========

        /// <summary>抬头口径行（Excel 与 PDF 同文）。</summary>
        private const string StatScopeLine = "口径：应缴含往年结转、已缴含补缴往年；收款明细按收款发生年度归属";

        /// <summary>单业主「缴费概况」四行（Excel 与 PDF 同表头同顺序）。</summary>
        private static string[][] StatRows(OwnerDto owner)
        {
            return new[]
            {
                new[] { "应缴合计（含往年结转）", owner.YearReceivable.ToString("0.00") },
                new[] { "已缴合计（含补缴往年）", owner.YearPaid.ToString("0.00") },
                new[] { "当前欠费", owner.CurrentArrear.ToString("0.00") },
                new[] { "收缴率", RateText(owner) }
            };
        }

        /// <summary>单业主报表口径说明（Excel 与 PDF 同文）。</summary>
        private static readonly string[] ScopeNotesSingle =
        {
            "口径说明：1) 年度按「账期起始日」所属年度统计（无账期时按到期日），与仪表盘同口径；",
            "2) 账单归属含三类缴费对象 —— 房产账单（按房产业主关系）、车位账单（按车位绑定业主）、业主直缴账单；",
            // 全角「－」(U+FF0D)：半角减号「−」(U+2212) 在 SimHei 子集缺字（PDF 渲染成方框）
            "3) 欠费 = 账单应收 － 已收（未结清账单）。"
        };

        /// <summary>全部业主报表口径说明（Excel 与 PDF 同文）。</summary>
        private static readonly string[] ScopeNotesAll =
        {
            "口径说明：1) 年度按「账期起始日」所属年度统计（无账期时按到期日），与仪表盘同口径；",
            "2) 账单归属含房产 / 车位 / 业主直缴三类缴费对象；",
            "3) 欠费 = 账单应收 － 已收（未结清账单）；",
            "4) 逐户的账单与收款逐笔明细，请用「导出 PDF / 导出 Excel（缴费明细）」。"
        };

        /// <summary>电话列文本（空值统一为 —）。</summary>
        private static string PhoneText(string phone)
        {
            return string.IsNullOrWhiteSpace(phone) ? "—" : phone;
        }

        /// <summary>明细行的「业主」列文本（业主已删除等取不到时回落 —）。</summary>
        private static string OwnerLabel(Dictionary<int, string> names, int ownerId)
        {
            string label;
            return names != null && names.TryGetValue(ownerId, out label) ? label : "—";
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
            /// <summary>CHG-v1.4.0-15：全部业主明细用 —— 该账单归属的业主主键（单业主查询不填）。</summary>
            public int OwnerId { get; set; }
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
            /// <summary>CHG-v1.4.0-15：全部业主明细用 —— 该收款归属的业主主键（单业主查询不填）。</summary>
            public int OwnerId { get; set; }
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
