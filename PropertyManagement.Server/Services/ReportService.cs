using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Contract.PhoneBook;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 报表服务（D4-6，UC-FIN-009/010/012，BR-FIN-09）：
    /// 收支明细流水（只读）、月/季财务报表、Excel（ClosedXML）/PDF（PDFsharp）导出留痕 t_report_log。
    /// 新依赖均为 netstandard2.0/net461 兼容资产（Win7 红线）。
    /// </summary>
    public class ReportService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IFinanceRepository _finance;
        private readonly IChargeStandardRepository _standards;
        private readonly IPhoneBookRepository _phoneBook;
        private readonly AuditService _audit;

        public ReportService()
            : this(new SqliteConnectionFactory(), new SqlFinanceRepository(), new SqlChargeStandardRepository(),
                   new SqlPhoneBookRepository(), new AuditService())
        {
        }

        public ReportService(IDbConnectionFactory connectionFactory, IFinanceRepository finance, AuditService audit)
            : this(connectionFactory, finance, new SqlChargeStandardRepository(), new SqlPhoneBookRepository(), audit)
        {
        }

        /// <summary>
        /// CHG-v1.4.0-14（负责人 2026-10-08 第 6 轮反馈「导出 PDF 模板缺少项目里所包含的规格明细」）：
        /// 收费项目清单 PDF 需要展开价目表规格，追加「价目表仓储」依赖；
        /// 旧的三参构造保留并内部回落默认实现，既有调用与测试不受影响。
        /// </summary>
        public ReportService(IDbConnectionFactory connectionFactory, IFinanceRepository finance,
            IChargeStandardRepository standards, AuditService audit)
            : this(connectionFactory, finance, standards, new SqlPhoneBookRepository(), audit)
        {
        }

        /// <summary>
        /// CHG-v1.4.1-08（负责人 2026-10-10「电话条目维护：新增导出 PDF / Excel，模板要有备注」）：
        /// 追加「电话簿仓储」依赖用于取数；旧的三个构造保留并内部回落默认实现，既有调用不受影响。
        /// </summary>
        public ReportService(IDbConnectionFactory connectionFactory, IFinanceRepository finance,
            IChargeStandardRepository standards, IPhoneBookRepository phoneBook, AuditService audit)
        {
            _connectionFactory = connectionFactory;
            _finance = finance;
            _standards = standards ?? new SqlChargeStandardRepository();
            _phoneBook = phoneBook ?? new SqlPhoneBookRepository();
            _audit = audit;
        }

        public PageResult<LedgerEntryDto> QueryLedger(LedgerQueryRequest query)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _finance.QueryLedger(connection, query ?? new LedgerQueryRequest());
            }
        }

        public FinancialReportDto GetFinancialReport(FinancialReportQueryRequest query)
        {
            FinancialReportWindow window = ResolveWindow(query);

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                FinancialReportDto report = _finance.BuildFinancialReport(connection, window);
                report.Period = query != null && !string.IsNullOrWhiteSpace(query.Period)
                    ? query.Period
                    : report.Period;
                report.ComparePeriod = query == null ? null : query.ComparePeriod;
                return report;
            }
        }

        public ReportLogDto ExportReport(ReportExportRequest request)
        {
            if (request == null || request.Query == null)
            {
                throw ApiException.BadRequest("报表导出参数不能为空");
            }

            FinancialReportWindow window = ResolveWindow(request.Query);
            bool annualWindow = window.From.Month == 1 && window.From.Day == 1 &&
                                window.To.Month == 12 && window.To.Day == 31 &&
                                window.From.Year == window.To.Year;

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                FinancialReportDto report = _finance.BuildFinancialReport(connection, window);

                string fileName = "financial_" + request.Query.Period.Replace("-", "").Replace("Q", "q") +
                    "_" + DateTime.Now.ToString("yyyyMMddHHmmss") +
                    (request.Format == ExportFormat.Excel ? ".xlsx" : ".pdf");
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);

                if (request.Format == ExportFormat.Excel)
                {
                    ExportExcel(filePath, request.Query.Period, report, annualWindow);
                }
                else
                {
                    ExportPdf(filePath, request.Query.Period, report, annualWindow);
                }

                var log = new ReportLogDto
                {
                    ReportType = "financial",
                    Period = request.Query.Period,
                    Format = request.Format,
                    FilePath = filePath
                };

                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    log.Id = _finance.InsertReportLog(connection, transaction, log);
                    transaction.Commit();
                }

                ReportLogDto saved = _finance.GetReportLog(connection, log.Id);
                if (saved != null)
                {
                    return saved;
                }

                _audit.Write("REPORT_EXPORT", "report_log", log.Id.ToString(),
                    "报表导出：" + request.Query.Period + "，" + request.Format + "，文件 " + fileName);
                return log;
            }
        }

        /// <summary>收支明细流水导出上限（一次导出最多 5000 行，避免导出把界面拖死）。</summary>
        private const int LedgerExportMaxRows = 5000;

        /// <summary>
        /// CHG-v1.1.2-05：收支明细流水导出（Excel / PDF）。
        /// 原口径下「导出」只弹提示让用户去财务报表生成总表 —— 现按当前筛选条件导出**明细流水**，
        /// 复用财务报表同一条导出通道（ClosedXML / PDFsharp + PdfFontSupport），并写 t_report_log 留痕。
        /// </summary>
        public ReportLogDto ExportLedger(LedgerExportRequest request)
        {
            if (request == null)
            {
                throw ApiException.BadRequest("流水导出参数不能为空");
            }

            LedgerQueryRequest query = request.Query ?? new LedgerQueryRequest();
            query.PageIndex = 1;
            query.PageSize = LedgerExportMaxRows;

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                PageResult<LedgerEntryDto> page = _finance.QueryLedger(connection, query);
                List<LedgerEntryDto> items = page.Items ?? new List<LedgerEntryDto>();

                string period = DateTime.Now.ToString("yyyyMMddHHmmss");
                string fileName = "ledger_" + period + (request.Format == ExportFormat.Excel ? ".xlsx" : ".pdf");
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);

                if (request.Format == ExportFormat.Excel)
                {
                    ExportLedgerExcel(filePath, items);
                }
                else
                {
                    ExportLedgerPdf(filePath, items);
                }

                var log = new ReportLogDto
                {
                    ReportType = "ledger",
                    Period = period,
                    Format = request.Format,
                    FilePath = filePath
                };
                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    log.Id = _finance.InsertReportLog(connection, transaction, log);
                    transaction.Commit();
                }

                _audit.Write("LEDGER_EXPORT", "report_log", log.Id.ToString(),
                    "收支明细流水导出：" + items.Count + " 条，" + request.Format + "，文件 " + fileName,
                    result: "Success");

                ReportLogDto saved = _finance.GetReportLog(connection, log.Id);
                return saved ?? log;
            }
        }

        private const int ArrearDetailExportMaxRows = 5000;

        /// <summary>
        /// 收款登记「应缴明细」导出 PDF（CHG-v1.2.0-32，负责人 2026-09-22）：
        /// 口径 = **当前所选缴费对象的全部应缴明细**（含已结清未清理的记录，与页面表格一致），
        /// 供用户在「清理（删除）已结清记录」之前先导出归档 ——
        /// 既满足归档留痕，又满足列表记录管理。
        /// 列与页面一致：账单号 / 缴费对象 / 收费项目 / 账单期间 / 应收 / 已收 / 未收 / 状态；
        /// 文末汇总（合计应收 / 已收 / 未收、已结清与未结清笔数）。
        /// 文件写 t_report_log（report_type = arrear_detail）并记审计，下载走 /reports/files/{id}。
        /// 本导出**只读**：不改动任何账单或资金数据。
        /// </summary>
        public ReportLogDto ExportArrearDetails(ArrearDetailExportRequest request, string operatorName = null)
        {
            request = request ?? new ArrearDetailExportRequest();
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                var query = new BillQueryRequest
                {
                    PayerOwnerId = request.PayerOwnerId,
                    PayerName = string.IsNullOrWhiteSpace(request.PayerName) ? null : request.PayerName.Trim(),
                    OwnerId = request.OwnerId,
                    PropertyId = request.PropertyId,
                    PageIndex = 1,
                    PageSize = ArrearDetailExportMaxRows
                };
                // 草稿不可收款、也不在应缴明细表格里 → 导出与页面同口径
                List<BillListItemDto> items = (_finance.QueryBills(connection, query).Items ?? new List<BillListItemDto>())
                    .Where(x => x.Status != BillStatus.Draft)
                    .OrderBy(x => x.DueAt).ThenBy(x => x.Id)
                    .ToList();

                string payer = string.IsNullOrWhiteSpace(request.PayerDisplay)
                    ? "—"
                    : request.PayerDisplay.Trim();
                string period = DateTime.Now.ToString("yyyyMMddHHmmss");
                string fileName = "arrear_detail_" + period + ".pdf";
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);
                ExportArrearDetailsPdf(filePath, items, payer);

                var log = new ReportLogDto
                {
                    ReportType = "arrear_detail",
                    Period = period,
                    Format = ExportFormat.Pdf,
                    FilePath = filePath
                };
                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    log.Id = _finance.InsertReportLog(connection, transaction, log);
                    transaction.Commit();
                }

                _audit.Write("ARREAR_DETAIL_EXPORT", "report_log", log.Id.ToString(),
                    "收款登记应缴明细导出 PDF：缴费对象 " + payer + "，共 " + items.Count + " 笔，文件 " + fileName,
                    userName: operatorName, module: "财务收费", result: "成功");

                ReportLogDto saved = _finance.GetReportLog(connection, log.Id);
                return saved ?? log;
            }
        }

        /// <summary>
        /// 应缴明细 PDF 绘制（A4 纵向，长名单自动翻页并重画表头）。
        /// 列宽按 A4 可打印宽度（595.28 - 左右各 40pt = 515pt）排布，单元格按实际量宽裁剪，
        /// 避免「单元被遮挡 / 文字被裁掉」（本仓多轮反馈的高频问题）。
        /// </summary>
        private static void ExportArrearDetailsPdf(string filePath, List<BillListItemDto> items, string payer)
        {
            PdfFontSupport.Ensure();

            var titleFont = new XFont("SimHei", 15, XFontStyleEx.Bold);
            var headerFont = new XFont("SimHei", 9, XFontStyleEx.Bold);
            var bodyFont = new XFont("SimHei", 8, XFontStyleEx.Regular);
            var smallFont = new XFont("SimHei", 7.5, XFontStyleEx.Regular);

            // 列宽合计 492pt（xa[0]=40 → 最右边界 532pt，仍在右边距 555pt 之内）
            string[] headers = { "账单号", "缴费对象", "收费项目", "账单期间", "应收", "已收", "未收", "状态" };
            double[] xs = { 40, 86, 178, 264, 356, 406, 456, 506 };
            double[] widths = { 46, 92, 86, 92, 50, 50, 50, 34 };
            const double TableRight = 540;

            decimal totalAmount = items.Sum(x => x.Amount);
            decimal totalPaid = items.Sum(x => x.PaidAmount);
            decimal totalUnpaid = items.Sum(x => x.Amount - x.PaidAmount);
            int settledCount = items.Count(x => x.PaidAmount >= x.Amount);
            int unsettledCount = items.Count - settledCount;

            using (var document = new PdfDocument())
            {
                PdfPage page = null;
                XGraphics gfx = null;
                double y = 0;

                try
                {
                    foreach (BillListItemDto item in items)
                    {
                        if (gfx == null || y > page.Height.Point - 60)
                        {
                            if (gfx != null) { gfx.Dispose(); gfx = null; }
                            page = document.AddPage();
                            page.Size = PdfSharp.PageSize.A4;
                            gfx = XGraphics.FromPdfPage(page);
                            y = 40;

                            if (document.PageCount == 1)
                            {
                                gfx.DrawString("安怡物业 · 收款登记应缴明细", titleFont, XBrushes.Black, 40, y);
                                y += 18;
                                gfx.DrawString("缴费对象：" + payer +
                                               "　导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                                               "　共 " + items.Count + " 笔（已结清 " + settledCount + " / 未结清 " + unsettledCount + "）",
                                    smallFont, XBrushes.Black, 40, y);
                                y += 12;
                                gfx.DrawString("合计：应收 ￥" + totalAmount.ToString("N2") + "　已收 ￥" + totalPaid.ToString("N2") +
                                               "　未收 ￥" + totalUnpaid.ToString("N2"),
                                    smallFont, XBrushes.Black, 40, y);
                                y += 12;
                                gfx.DrawString("说明：本表为归档留存件；清理（删除）已结清记录只从「应缴明细」列表移除，" +
                                               "账单与收款、退款、财务报表、收支明细流水、业主档案缴费概况均保留。",
                                    smallFont, XBrushes.Black, 40, y);
                                y += 16;
                            }
                            else
                            {
                                gfx.DrawString("安怡物业 · 收款登记应缴明细（续）　缴费对象：" + payer,
                                    headerFont, XBrushes.Black, 40, y);
                                y += 16;
                            }

                            for (int i = 0; i < headers.Length; i++)
                            {
                                gfx.DrawString(headers[i], headerFont, XBrushes.Black, xs[i], y);
                            }
                            y += 4;
                            gfx.DrawLine(XPens.Gray, xs[0], y, TableRight, y);
                            y += 11;
                        }

                        string[] cells =
                        {
                            "BILL-" + item.Id.ToString("D4"),
                            ArrearObjectText(item),
                            item.ChargeItemName ?? string.Empty,
                            string.IsNullOrWhiteSpace(item.CyclePeriod) ? "—" : item.CyclePeriod.Trim().Replace(" ", string.Empty),
                            // 全角「￥」：SimHei 含该字形；半角 ¥(U+00A5) 在 PDF 里会缺字（渲染成方框）
                            "￥" + item.Amount.ToString("N2"),
                            "￥" + item.PaidAmount.ToString("N2"),
                            "￥" + (item.Amount - item.PaidAmount).ToString("N2"),
                            ArrearBillStatusText(item)
                        };
                        for (int i = 0; i < cells.Length; i++)
                        {
                            gfx.DrawString(FitToWidth(gfx, cells[i], bodyFont, widths[i]),
                                bodyFont, XBrushes.Black, xs[i], y);
                        }
                        y += 14;
                    }

                    if (items.Count == 0)
                    {
                        page = document.AddPage();
                        page.Size = PdfSharp.PageSize.A4;
                        gfx = XGraphics.FromPdfPage(page);
                        y = 40;
                        gfx.DrawString("安怡物业 · 收款登记应缴明细", titleFont, XBrushes.Black, 40, y);
                        y += 18;
                        gfx.DrawString("缴费对象：" + payer, smallFont, XBrushes.Black, 40, y);
                        y += 20;
                        gfx.DrawString("该缴费对象当前没有应缴明细记录。", bodyFont, XBrushes.Black, 40, y);
                    }
                    else
                    {
                        if (y > page.Height.Point - 50)
                        {
                            gfx.Dispose();
                            gfx = null;
                            page = document.AddPage();
                            page.Size = PdfSharp.PageSize.A4;
                            gfx = XGraphics.FromPdfPage(page);
                            y = 40;
                        }
                        y += 4;
                        gfx.DrawLine(XPens.Gray, xs[0], y, TableRight, y);
                        y += 11;
                        // 合计行整行起排（不分列对齐）：金额串长于单列宽度，分列绘制会互相压字
                        gfx.DrawString("合计：应收 ￥" + totalAmount.ToString("N2") +
                                       "　已收 ￥" + totalPaid.ToString("N2") +
                                       "　未收 ￥" + totalUnpaid.ToString("N2"),
                            headerFont, XBrushes.Black, xs[0], y);
                    }

                    document.Save(filePath);
                }
                finally
                {
                    if (gfx != null) { gfx.Dispose(); }
                }
            }
        }

        /// <summary>
        /// 应缴明细「缴费对象」展示（与收款登记表格同口径）：
        /// 车位 → 「车位 X」；业主直缴（无房产/车位）→ 「业主直缴」；自定义缴费对象 → 手工填写的名称；
        /// 房产 → 「楼栋 房号」（无房号时回落房产编号）。
        /// </summary>
        private static string ArrearObjectText(BillListItemDto item)
        {
            if (item == null) { return "—"; }
            if (!string.IsNullOrWhiteSpace(item.PayerName)) { return item.PayerName.Trim(); }
            if (item.ParkingId.HasValue)
            {
                return string.IsNullOrEmpty(item.SpaceNo) ? "车位" : "车位 " + item.SpaceNo;
            }
            if (item.OwnerId.HasValue && !item.PropertyId.HasValue) { return "业主直缴"; }
            string building = string.IsNullOrEmpty(item.BuildingNo) ? string.Empty : item.BuildingNo + " ";
            string room = string.IsNullOrEmpty(item.RoomNo) ? (item.PropertyNo ?? "—") : item.RoomNo;
            string text = (building + room).Trim();
            return string.IsNullOrEmpty(text) ? "—" : text;
        }

        /// <summary>应缴明细「状态」（与收款登记表格同口径，已结清以金额为准）。</summary>
        private static string ArrearBillStatusText(BillListItemDto item)
        {
            if (item == null) { return "—"; }
            if (item.PaidAmount >= item.Amount) { return "已结清"; }
            switch (item.Status)
            {
                case BillStatus.Partial: return "部分缴";
                case BillStatus.Overdue: return "逾期";
                case BillStatus.Draft: return "草稿";
                default: return "未缴";
            }
        }

        /// <summary>
        /// CHG-v1.1.2-33：收费项目清单导出（PDF / Excel）。
        /// 复用财务报表同一条导出通道（ClosedXML / PDFsharp + PdfFontSupport），并写 t_report_log 留痕。
        /// Excel：列 = 项目编号 / 收费标准 / 类别 / 缴费对象 / 规格 / 默认单价 / 状态（与列表页一致）；
        /// PDF（CHG-v1.4.0-14）：项目行 + 其下展开的**规格明细**（规格 / 适用条件 / 单价 / 计算规则 / 计费周期 / 状态）。
        /// </summary>
        public ReportLogDto ExportChargeItems(ChargeItemExportRequest request)
        {
            request = request ?? new ChargeItemExportRequest();
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                List<ChargeItemDto> items = _finance.ListChargeItems(connection, request.Keyword, request.Category)
                    ?? new List<ChargeItemDto>();
                // CHG-v1.4.0-14：一次性取齐涉及到的收费标准（含规格与变量名快照），供 PDF 展开规格明细
                Dictionary<int, ChargeStandardDto> standards = LoadStandardsForItems(connection, items);

                string period = DateTime.Now.ToString("yyyyMMddHHmmss");
                string fileName = "charge_items_" + period + (request.Format == ExportFormat.Excel ? ".xlsx" : ".pdf");
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);

                if (request.Format == ExportFormat.Excel)
                {
                    ExportChargeItemsExcel(filePath, items);
                }
                else
                {
                    ExportChargeItemsPdf(filePath, items, standards);
                }

                var log = new ReportLogDto
                {
                    ReportType = "charge_item",
                    Period = period,
                    Format = request.Format,
                    FilePath = filePath
                };
                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    log.Id = _finance.InsertReportLog(connection, transaction, log);
                    transaction.Commit();
                }

                _audit.Write("CHARGE_ITEM_EXPORT", "report_log", log.Id.ToString(),
                    "收费项目清单导出：" + items.Count + " 条，" + request.Format + "，文件 " + fileName,
                    result: "Success");

                ReportLogDto saved = _finance.GetReportLog(connection, log.Id);
                return saved ?? log;
            }
        }

        /// <summary>
        /// CHG-v1.2.0-25：支出登记明细导出（PDF）。
        /// 口径与页面一致 —— 导出**当前筛选条件**下的支出明细（关键字 / 类别 / 状态），
        /// 文件写 t_report_log（report_type = expense）并经 /reports/files/{id} 下载。
        /// 列：支出编号 / 日期 / 类别 / 摘要 / 金额 / 收款方 / 状态；文末追加合计行（不含已删除）。
        /// </summary>
        public ReportLogDto ExportExpenses(ExpenseExportRequest request)
        {
            request = request ?? new ExpenseExportRequest();
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                List<ExpenseDto> items = _finance.ListExpensesForExport(connection, request)
                    ?? new List<ExpenseDto>();

                string period = DateTime.Now.ToString("yyyyMMddHHmmss");
                string fileName = "expenses_" + period + (request.Format == ExportFormat.Excel ? ".xlsx" : ".pdf");
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);

                if (request.Format == ExportFormat.Excel)
                {
                    ExportExpensesExcel(filePath, items);
                }
                else
                {
                    ExportExpensesPdf(filePath, items, request);
                }

                var log = new ReportLogDto
                {
                    ReportType = "expense",
                    Period = period,
                    Format = request.Format,
                    FilePath = filePath
                };
                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    log.Id = _finance.InsertReportLog(connection, transaction, log);
                    transaction.Commit();
                }

                _audit.Write("EXPENSE_EXPORT", "report_log", log.Id.ToString(),
                    "支出登记明细导出：" + items.Count + " 条，" + request.Format + "，文件 " + fileName,
                    result: "Success");

                ReportLogDto saved = _finance.GetReportLog(connection, log.Id);
                return saved ?? log;
            }
        }

        /// <summary>
        /// CHG-v1.4.1-08（负责人 2026-10-10「电话条目维护：页面工具栏新增导出 PDF 和 Excel，模板要有备注」）：
        /// 口径 = **当前筛选条件下的全部记录**（不分页，与页面「共 N 条记录」对得上），
        /// 列 = 分类 / 名称 / 电话 / 来源 / 类型 / 置顶 / 状态 / 备注 / 更新时间；文件写 t_report_log 留痕。
        /// </summary>
        public ReportLogDto ExportPhoneEntries(PhoneEntryExportRequest request, string operatorName = null)
        {
            request = request ?? new PhoneEntryExportRequest();
            if (request.Format != ExportFormat.Excel && request.Format != ExportFormat.Pdf)
            {
                throw ApiException.BadRequest("导出格式仅支持 Excel / PDF");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                var query = new PhoneEntryQueryRequest
                {
                    PageIndex = 1,
                    PageSize = PhoneExportRowLimit,
                    Keyword = request.Keyword,
                    CategoryId = request.CategoryId,
                    Source = request.Source,
                    Status = request.Status
                };
                int total;
                List<PhoneEntryDto> items =
                    (_phoneBook.QueryEntries(connection, query, out total).Items ?? new List<PhoneEntryDto>())
                    .ToList();

                string period = DateTime.Now.ToString("yyyyMMddHHmmss");
                string fileName = "phone_entries_" + period + (request.Format == ExportFormat.Excel ? ".xlsx" : ".pdf");
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);

                if (request.Format == ExportFormat.Excel) { ExportPhoneEntriesExcel(filePath, items); }
                else { WritePhoneEntriesPdf(filePath, items, operatorName); }

                var log = new ReportLogDto
                {
                    ReportType = "phone_entry",
                    Period = period,
                    Format = request.Format,
                    FilePath = filePath
                };
                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    log.Id = _finance.InsertReportLog(connection, transaction, log);
                    transaction.Commit();
                }

                _audit.Write("PHONE_ENTRY_EXPORT", "report_log", log.Id.ToString(),
                    "电话条目导出：" + items.Count + " 条，" + request.Format + "，文件 " + fileName,
                    result: "Success");

                ReportLogDto saved = _finance.GetReportLog(connection, log.Id);
                return saved ?? log;
            }
        }

        /// <summary>电话条目导出取数上限（与其它导出同量级；超出截断并在文末注明）。</summary>
        private const int PhoneExportRowLimit = 5000;

        private static string[] PhoneEntryHeaders()
        {
            return new[] { "分类", "名称", "电话", "来源", "类型", "置顶", "状态", "备注", "更新时间" };
        }

        private static string[] PhoneEntryRowValues(PhoneEntryDto item)
        {
            if (item == null) { return new string[PhoneEntryHeaders().Length]; }
            return new[]
            {
                Text(item.CategoryName),
                Text(item.Name),
                Text(item.Phone),
                Text(item.SourceText),
                Text(string.IsNullOrWhiteSpace(item.TypeName) ? item.EntryTypeText : item.TypeName),
                item.IsTop ? "是" : "否",
                Text(item.StatusText),
                // CHG-v1.4.1-08：备注是负责人点名要的列 —— 空值统一显示「—」，不省略该列
                Text(item.Note),
                Text(item.UpdatedAt)
            };
        }

        private static void ExportPhoneEntriesExcel(string filePath, List<PhoneEntryDto> items)
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("电话条目");
                sheet.Cell(1, 1).Value = "安怡物业 · 电话条目";
                sheet.Cell(2, 1).Value = "导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                                         "，共 " + items.Count + " 条";
                sheet.Cell(2, 1).Style.Font.FontSize = 10;

                string[] headers = PhoneEntryHeaders();
                const int start = 4;
                for (int c = 1; c <= headers.Length; c++)
                {
                    sheet.Cell(start, c).Value = headers[c - 1];
                    sheet.Cell(start, c).Style.Font.Bold = true;
                }

                int row = start + 1;
                foreach (PhoneEntryDto item in items)
                {
                    string[] cells = PhoneEntryRowValues(item);
                    for (int c = 1; c <= cells.Length; c++) { sheet.Cell(row, c).Value = cells[c - 1]; }
                    row++;
                }
                // 合计口径行（与 PDF 一致）：记录数 + 启用/停用拆分
                sheet.Cell(row, 1).Value = "合计";
                sheet.Cell(row, 1).Style.Font.Bold = true;
                sheet.Cell(row, 2).Value = "共 " + items.Count + " 条记录（启用 " +
                    items.Count(x => x.Status == PhoneEntryStatus.Enabled) + " / 停用 " +
                    items.Count(x => x.Status == PhoneEntryStatus.Disabled) + "）";
                for (int c = 1; c <= headers.Length; c++) sheet.Column(c).Width = c == 8 ? 32 : 16;
                workbook.SaveAs(filePath);
            }
        }

        private static void WritePhoneEntriesPdf(string filePath, List<PhoneEntryDto> items, string operatorName)
        {
            PdfFontSupport.Ensure();

            var titleFont = new XFont("SimHei", 15, XFontStyleEx.Bold);
            var headerFont = new XFont("SimHei", 8, XFontStyleEx.Bold);
            var bodyFont = new XFont("SimHei", 7, XFontStyleEx.Regular);

            string[] headers = PhoneEntryHeaders();
            // CHG-v1.4.1-08：9 列（含备注）走横向 A4（842×595），备注列给足宽度、其余列不互相压字
            double[] xs = { 30, 96, 186, 276, 356, 426, 476, 536, 700 };
            double[] widths = { 60, 84, 84, 74, 64, 44, 54, 158, 100 };

            using (var document = new PdfDocument())
            using (var canvas = new PdfTableCanvas(document, titleFont, headerFont, bodyFont, landscape: true))
            {
                canvas.Title("安怡物业 · 电话条目");
                canvas.Line("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                            "　记录：" + items.Count + " 条" +
                            (string.IsNullOrWhiteSpace(operatorName) ? string.Empty : "　操作人：" + operatorName));
                canvas.Gap(2);
                canvas.Head(headers, xs);

                foreach (PhoneEntryDto item in items)
                {
                    canvas.EnsureRow(13, headers, xs);
                    string[] cells = PhoneEntryRowValues(item);
                    for (int i = 0; i < cells.Length; i++)
                    {
                        canvas.Cell(FitToWidth(canvas.Gfx, cells[i], bodyFont, widths[i]), xs[i]);
                    }
                    canvas.NextRow(12);
                }

                canvas.Gap(6);
                canvas.Line("合计：共 " + items.Count + " 条记录（启用 " +
                            items.Count(x => x.Status == PhoneEntryStatus.Enabled) + " / 停用 " +
                            items.Count(x => x.Status == PhoneEntryStatus.Disabled) + "）");
                document.Save(filePath);
            }
        }

        /// <summary>收费项目清单 —— 导出展示字段（与列表页一致）。</summary>
        private static string[] ChargeItemRowValues(ChargeItemDto item)
        {
            string specs = item.SpecCount > 0
                ? (string.IsNullOrWhiteSpace(item.SpecPriceText) ? item.SpecCount + " 条规格" : item.SpecPriceText)
                : "统一价";
            return new[]
            {
                "XM-" + item.Id.ToString("D3"),
                string.IsNullOrWhiteSpace(item.StandardName) ? "—" : item.StandardName,
                string.IsNullOrWhiteSpace(item.Category) ? "未分类" : item.Category,
                ChargeObjectText(item),
                specs,
                string.IsNullOrWhiteSpace(item.PriceUnit)
                    ? "¥" + item.UnitPrice.ToString("0.00")
                    : "¥" + item.UnitPrice.ToString("0.00") + item.PriceUnit,
                item.Status == 0 ? "启用" : "停用"
            };
        }

        private static string ChargeObjectText(ChargeItemDto item)
        {
            if (!string.IsNullOrWhiteSpace(item.ObjectName)) { return item.ObjectName; }
            switch (item.ObjectType)
            {
                case ChargeObjectType.Parking: return "车位";
                case ChargeObjectType.Owner: return "业主";
                case ChargeObjectType.Custom: return "自定义";
                default: return "房产";
            }
        }

        private static void ExportChargeItemsExcel(string filePath, List<ChargeItemDto> items)
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("收费项目");
                sheet.Cell(1, 1).Value = "安怡物业 · 收费项目清单";
                sheet.Cell(2, 1).Value = "导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "，共 " + items.Count + " 条";

                string[] headers = { "项目编号", "收费标准", "类别", "缴费对象", "规格", "默认单价", "状态" };
                const int start = 4;
                for (int c = 1; c <= headers.Length; c++)
                {
                    sheet.Cell(start, c).Value = headers[c - 1];
                    sheet.Cell(start, c).Style.Font.Bold = true;
                }

                int row = start + 1;
                foreach (ChargeItemDto item in items)
                {
                    string[] values = ChargeItemRowValues(item);
                    for (int c = 1; c <= values.Length; c++) { sheet.Cell(row, c).Value = values[c - 1]; }
                    row++;
                }
                sheet.Columns(1, headers.Length).AdjustToContents();
                workbook.SaveAs(filePath);
            }
        }

        /// <summary>
        /// 取收费项目涉及的收费标准（按 StandardId 去重；未迁移的历史项目 = 无标准，PDF 按「未维护规格」展示）。
        /// </summary>
        private Dictionary<int, ChargeStandardDto> LoadStandardsForItems(IDbConnection connection, List<ChargeItemDto> items)
        {
            var map = new Dictionary<int, ChargeStandardDto>();
            foreach (int standardId in (items ?? new List<ChargeItemDto>())
                .Where(x => x.StandardId.HasValue && x.StandardId.Value > 0)
                .Select(x => x.StandardId.Value)
                .Distinct())
            {
                ChargeStandardDto dto = _standards.GetStandard(connection, standardId);
                if (dto != null) { map[standardId] = dto; }
            }
            return map;
        }

        /// <summary>
        /// 收费项目清单 PDF（CHG-v1.4.0-14，负责人 2026-10-08 第 6 轮反馈）：
        /// 两级表格 —— 项目行 + 其下缩进的**规格明细**（规格 / 适用条件 / 单价 / 计算规则 / 计费周期 / 状态）；
        /// 计算规则按变量名快照替换 `{v:ID}` 记号，未绑定变量显示「未绑定变量」，与价目表列表口径一致；
        /// 内容超出一页时自动翻页并重画两级表头（旧实现在页尾静默截断）。
        /// </summary>
        private static void ExportChargeItemsPdf(string filePath, List<ChargeItemDto> items,
            Dictionary<int, ChargeStandardDto> standards)
        {
            PdfFontSupport.Ensure();

            var titleFont = new XFont("SimHei", 15, XFontStyleEx.Bold);
            var headerFont = new XFont("SimHei", 9, XFontStyleEx.Bold);
            var bodyFont = new XFont("SimHei", 8, XFontStyleEx.Regular);
            var noteFont = new XFont("SimHei", 7.5, XFontStyleEx.Regular);

            // 列位：半角「¥」在 SimHei 子集缺字（渲染成方框），PDF 金额统一用全角「￥」（见本文件既有约定）
            string[] itemHeads = { "项目编号", "收费标准", "类别", "缴费对象", "规格", "状态" };
            double[] itemXs = { 36, 100, 252, 322, 410, 516 };
            double[] itemW = { 62, 150, 68, 86, 102, 34 };
            string[] specHeads = { "规格", "适用条件", "单价", "计算规则", "计费周期", "状态" };
            double[] specXs = { 48, 100, 212, 304, 446, 490 };
            double[] specW = { 50, 110, 90, 140, 42, 30 };

            using (var document = new PdfDocument())
            {
                XGraphics gfx = null;
                double pageHeight = 0;
                double y = 0;
                bool firstPage = true;
                bool pageHasItem = false;

                Action openPage = () =>
                {
                    if (gfx != null) { gfx.Dispose(); }
                    PdfPage page = document.AddPage();
                    page.Size = PdfSharp.PageSize.A4;
                    pageHeight = page.Height.Point;
                    gfx = XGraphics.FromPdfPage(page);
                    y = 30;
                    if (firstPage)
                    {
                        gfx.DrawString("安怡物业 · 收费项目清单", titleFont, XBrushes.Black, 36, y);
                        y += 20;
                        gfx.DrawString("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                                       "，共 " + items.Count + " 条项目（项目下列出该收费标准的全部规格）",
                            bodyFont, XBrushes.Black, 36, y);
                        y += 20;
                        firstPage = false;
                    }
                    else
                    {
                        gfx.DrawString("安怡物业 · 收费项目清单（续）", headerFont, XBrushes.Black, 36, y);
                        y += 18;
                    }
                    DrawChargeItemHeaderRow(gfx, headerFont, y, itemHeads, itemXs, specHeads, specXs);
                    // 两级表头下画一条浅分隔线，避免「项目行」与「规格表头」视觉粘连
                    gfx.DrawLine(XPens.LightGray, 36, y + 21, 553, y + 21);
                    pageHasItem = false;
                    y += 26;
                };

                openPage();
                foreach (ChargeItemDto item in items)
                {
                    ChargeStandardDto standard = null;
                    if (item.StandardId.HasValue) { standards.TryGetValue(item.StandardId.Value, out standard); }
                    List<ChargeStandardSpecDto> specs = standard == null
                        ? new List<ChargeStandardSpecDto>()
                        : (standard.Specs ?? new List<ChargeStandardSpecDto>())
                            .OrderBy(x => x.IsFallback).ThenBy(x => x.Id).ToList();

                    // 项目行 + 规格行整体不拆页；项目较高时（规格多）允许中途翻页并重画项目行
                    double need = 20 + Math.Max(specs.Count, 1) * 12 + 6;
                    if (y + need > pageHeight - 40 && y > 60) { openPage(); }
                    if (pageHasItem)
                    {
                        // 项目之间加浅分隔线，便于区分「项目行 + 其规格」分组
                        gfx.DrawLine(XPens.LightGray, 36, y - 4, 553, y - 4);
                    }
                    pageHasItem = true;

                    string[] itemValues = ChargeItemRowValues(item);
                    string[] itemCells =
                    {
                        itemValues[0], itemValues[1], itemValues[2], itemValues[3],
                        specs.Count == 0
                            ? "未维护规格"
                            : specs.Count + " 条规格（" + specs.Count(x => x.Status == 0) + " 条启用）",
                        itemValues[6]
                    };
                    DrawChargeItemRow(gfx, headerFont, y, itemCells, itemW, itemXs);
                    y += 13;

                    if (specs.Count == 0)
                    {
                        gfx.DrawString("（该收费标准未维护规格）", noteFont, XBrushes.Black, specXs[0], y);
                        y += 12;
                    }
                    else
                    {
                        Dictionary<int, string> varNames = VariableNameMap(standard);
                        foreach (ChargeStandardSpecDto spec in specs)
                        {
                            if (y + 12 > pageHeight - 36)
                            {
                                openPage();
                                // 翻页后重画项目行，避免续页规格脱离所属项目
                                DrawChargeItemRow(gfx, headerFont, y, itemCells, itemW, itemXs);
                                pageHasItem = true;
                                y += 13;
                            }
                            string[] cells =
                            {
                                SpecNameText(spec), SpecConditionText(spec), SpecPriceText(spec),
                                SpecFormulaText(spec, varNames), SpecCycleText(spec),
                                spec.Status == 0 ? "启用" : "停用"
                            };
                            DrawChargeItemRow(gfx, bodyFont, y, cells, specW, specXs);
                            y += 12;
                        }
                    }
                    y += 5;
                }
                if (gfx != null) { gfx.Dispose(); }
                document.Save(filePath);
            }
        }

        private static void DrawChargeItemHeaderRow(XGraphics gfx, XFont font, double y,
            string[] itemHeads, double[] itemXs, string[] specHeads, double[] specXs)
        {
            for (int i = 0; i < itemHeads.Length && i < itemXs.Length; i++)
            {
                gfx.DrawString(itemHeads[i], font, XBrushes.Black, itemXs[i], y);
            }
            for (int i = 0; i < specHeads.Length && i < specXs.Length; i++)
            {
                gfx.DrawString(specHeads[i], font, XBrushes.Black, specXs[i], y + 13);
            }
        }

        private static void DrawChargeItemRow(XGraphics gfx, XFont font, double y,
            string[] cells, double[] widths, double[] xs)
        {
            for (int i = 0; i < cells.Length && i < xs.Length; i++)
            {
                gfx.DrawString(FitToWidth(gfx, cells[i] ?? string.Empty, font, widths[i]), font, XBrushes.Black, xs[i], y);
            }
        }

        /// <summary>规格适用条件文本（与价目表列表口径一致；已下线的维度如实标注）。</summary>
        private static string SpecConditionText(ChargeStandardSpecDto spec)
        {
            var parts = new List<string>();
            if (spec.MatchUsage.HasValue)
            {
                string usage = spec.MatchUsage.Value == 1 ? "商铺" : (spec.MatchUsage.Value == 2 ? "空置" : "住宅");
                parts.Add("房产用途 = " + usage);
            }
            if (spec.MatchStatus.HasValue)
            {
                string text = spec.MatchStatus.Value == 0 ? "空置" : (spec.MatchStatus.Value == 1 ? "入住" : "装修中");
                parts.Add("房产状态 = " + text + "（已下线，请改选）");
            }
            if (spec.MatchSpaceType.HasValue)
            {
                string text = spec.MatchSpaceType.Value == 0 ? "产权" : (spec.MatchSpaceType.Value == 1 ? "普通" : "临时");
                parts.Add("车位类型 = " + text);
            }
            if (!string.IsNullOrWhiteSpace(spec.MatchBuilding)) { parts.Add("楼栋 = " + spec.MatchBuilding); }
            if (spec.IsFallback) { parts.Add("不限（兜底）"); }
            return parts.Count == 0 ? "不限" : string.Join(" · ", parts);
        }

        private static Dictionary<int, string> VariableNameMap(ChargeStandardDto standard)
        {
            var map = new Dictionary<int, string>();
            if (standard == null) { return map; }
            foreach (ChargeVariableDto variable in standard.Variables ?? new List<ChargeVariableDto>())
            {
                if (!map.ContainsKey(variable.Id)) { map[variable.Id] = variable.VarName; }
            }
            return map;
        }

        /// <summary>计算规则展示文本：把 `{v:ID}` 记号替换为变量名，未绑定变量不暴露落库记号。</summary>
        private static string SpecFormulaText(ChargeStandardSpecDto spec, Dictionary<int, string> varNames)
        {
            if (string.IsNullOrWhiteSpace(spec.Formula)) { return "单价"; }
            string text = spec.Formula;
            foreach (KeyValuePair<int, string> pair in varNames)
            {
                text = text.Replace("{v:" + pair.Key + "}", pair.Value);
            }
            while (text.Contains("{v:"))
            {
                int start = text.IndexOf("{v:", StringComparison.Ordinal);
                int end = text.IndexOf('}', start);
                if (end < 0) { break; }
                text = text.Substring(0, start) + "未绑定变量" + text.Substring(end + 1);
            }
            return text;
        }

        private static string SpecNameText(ChargeStandardSpecDto spec)
        {
            return string.IsNullOrWhiteSpace(spec.SpecName) ? "—" : spec.SpecName;
        }

        /// <summary>
        /// 规格单价展示：PDF 用全角「￥」（半角 ¥ 在 SimHei 子集缺字，会渲染成方框；
        /// 见本文件「收据模板 / 财务报表」同类约定）；单价单位去掉重复的「元/」前缀（前面已带 ￥）。
        /// </summary>
        private static string SpecPriceText(ChargeStandardSpecDto spec)
        {
            string unit = spec.PriceUnit ?? string.Empty;
            if (unit.StartsWith("元/", StringComparison.Ordinal)) { unit = unit.Substring(2); }
            return "￥" + spec.UnitPrice.ToString("0.00") +
                   (string.IsNullOrWhiteSpace(unit) ? string.Empty : " / " + unit);
        }

        private static string SpecCycleText(ChargeStandardSpecDto spec)
        {
            return string.IsNullOrWhiteSpace(spec.CycleName) ? "一次性" : spec.CycleName;
        }

        public ReportLogDto GetReportLog(int logId)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                ReportLogDto log = _finance.GetReportLog(connection, logId);
                if (log == null)
                {
                    throw ApiException.NotFound("导出记录不存在");
                }
                return log;
            }
        }

        // ========== CHG-v1.3.1-05：财务报表「报表与导出留痕」清单（缓存清理） ==========

        /// <summary>
        /// 留痕清单：报表留痕（t_report_log）+ 导出留痕（t_export_log）+ **未被任何留痕引用的孤立生成文件**。
        /// 用途（负责人 2026-09-23）：年度/季度/月度清算时清理"只增不减"的导出缓存；
        /// 删除 = 留痕软删 + 物理删除生成文件，**不涉及任何账目**（与账单/收款/报表口径无关）。
        /// </summary>
        public List<ExportTraceDto> ListExportTraces()
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                var traces = new List<ExportTraceDto>();
                var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (ReportLogDto log in _finance.ListReportLogs(connection))
                {
                    traces.Add(BuildTrace("report", log.Id, log.ReportType, log.Period,
                        log.Format, log.FilePath, log.CreatedAt, referenced));
                }
                foreach (ExportLogDto log in _finance.ListExportLogs(connection))
                {
                    traces.Add(BuildTrace("export", log.Id, log.Module, null,
                        log.Format, log.FilePath, log.CreatedAt, referenced));
                }

                // 孤立生成文件：目录里未被任何留痕（含已软删留痕）引用的文件
                string dir = DbConfig.ExportDirectory;
                if (Directory.Exists(dir))
                {
                    foreach (string file in Directory.GetFiles(dir))
                    {
                        if (referenced.Contains(Path.GetFullPath(file))) { continue; }
                        if (!IsExportFile(file)) { continue; }
                        traces.Add(BuildTrace("orphan", 0, "未引用生成文件", null,
                            null, file, File.GetLastWriteTime(file), referenced));
                    }
                }

                return traces.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Source).ToList();
            }
        }

        private static ExportTraceDto BuildTrace(string source, int id, string kind, string period,
            ExportFormat? format, string filePath, DateTime createdAt, HashSet<string> referenced)
        {
            string full = string.IsNullOrWhiteSpace(filePath) ? null : SafeFullPath(filePath);
            if (full != null) { referenced.Add(full); }
            bool hasFile = full != null && File.Exists(full);
            long size = hasFile ? new FileInfo(full).Length : 0L;
            string ext = full == null ? string.Empty : Path.GetExtension(full);
            if (ext.StartsWith(".")) { ext = ext.Substring(1); }
            string formatText = format.HasValue
                ? (format.Value == ExportFormat.Excel ? "Excel" : "PDF")
                : (string.Equals(ext, "pdf", StringComparison.OrdinalIgnoreCase) ? "PDF"
                    : (string.Equals(ext, "xlsx", StringComparison.OrdinalIgnoreCase) ? "Excel" : ext.ToUpperInvariant()));
            return new ExportTraceDto
            {
                Source = source,
                Id = id,
                Kind = kind,
                Period = period,
                Format = formatText,
                FileName = full == null ? string.Empty : Path.GetFileName(full),
                FilePath = full,
                FileSize = size,
                CreatedAt = createdAt,
                HasFile = hasFile,
                IsOrphan = string.Equals(source, "orphan", StringComparison.OrdinalIgnoreCase),
                SourceText = string.Equals(source, "report", StringComparison.OrdinalIgnoreCase) ? "报表留痕"
                    : (string.Equals(source, "export", StringComparison.OrdinalIgnoreCase) ? "导出留痕" : "孤立文件"),
                FileSizeText = FormatSize(size)
            };
        }

        private static string SafeFullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch (Exception) { return null; }
        }

        /// <summary>只认导出目录里的生成物（避免把无关文件列进清单）。</summary>
        private static bool IsExportFile(string file)
        {
            string ext = Path.GetExtension(file);
            return string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024) { return (bytes / 1024.0 / 1024.0).ToString("0.00") + " MB"; }
            if (bytes >= 1024) { return (bytes / 1024.0).ToString("0.0") + " KB"; }
            return bytes + " B";
        }

        /// <summary>
        /// 留痕清理：逐条「留痕软删 + 物理删除生成文件」。
        /// 只允许删除导出目录内的文件（路径越界一律拒绝并回报），逐条失败不影响其余条目。
        /// </summary>
        public ExportTraceDeleteResultDto DeleteExportTraces(ExportTraceDeleteRequest request,
            string operatorName = null, string ip = null)
        {
            var items = (request == null ? null : request.Items) ?? new List<ExportTraceKey>();
            if (items.Count == 0)
            {
                throw ApiException.BadRequest("请先选择要清理的报表/导出留痕");
            }

            var reportIds = new List<int>();
            var exportIds = new List<int>();
            var files = new List<string>();
            var skipped = new List<string>();
            string exportDir = Path.GetFullPath(DbConfig.ExportDirectory);

            foreach (ExportTraceKey item in items)
            {
                string source = item == null ? null : item.Source;
                if (string.Equals(source, "report", StringComparison.OrdinalIgnoreCase) && item.Id > 0)
                {
                    reportIds.Add(item.Id);
                }
                else if (string.Equals(source, "export", StringComparison.OrdinalIgnoreCase) && item.Id > 0)
                {
                    exportIds.Add(item.Id);
                }
                else if (!string.Equals(source, "orphan", StringComparison.OrdinalIgnoreCase))
                {
                    skipped.Add("来源无法识别，已跳过：" + (item == null ? "（空）" : source));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.FileName)) { continue; }
                string full = SafeFullPath(Path.Combine(exportDir, Path.GetFileName(item.FileName)));
                if (full == null || !full.StartsWith(exportDir, StringComparison.OrdinalIgnoreCase))
                {
                    skipped.Add(item.FileName + "：路径越界，未删除");
                    continue;
                }
                files.Add(full);
            }

            int rows = 0;
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                rows += _finance.SoftDeleteReportLogs(connection, transaction, reportIds);
                rows += _finance.SoftDeleteExportLogs(connection, transaction, exportIds);
                transaction.Commit();
            }

            int deletedFiles = 0;
            long freed = 0;
            foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!File.Exists(file))
                    {
                        skipped.Add(Path.GetFileName(file) + "：文件已不存在（仅清理留痕）");
                        continue;
                    }
                    long size = new FileInfo(file).Length;
                    File.Delete(file);
                    deletedFiles++;
                    freed += size;
                }
                catch (Exception ex)
                {
                    skipped.Add(Path.GetFileName(file) + "：删除失败（" + ex.Message + "）");
                }
            }

            string message = "已清理留痕 " + rows + " 条、生成文件 " + deletedFiles + " 个，释放 " + FormatSize(freed)
                + "（留痕软删，可再随「一键清理残余数据」物理回收；账目数据不受影响）";
            _audit.Write("EXPORT_TRACE_DELETE", "report_log", null, message,
                userName: operatorName, ip: ip, module: "财务报表", result: "Success");

            return new ExportTraceDeleteResultDto
            {
                DeletedRows = rows,
                DeletedFiles = deletedFiles,
                FreedBytes = freed,
                SkippedItems = skipped,
                Message = message
            };
        }

        // ---------- 周期解析 ----------
        private static void ResolvePeriod(FinancialReportQueryRequest query, out DateTime from, out DateTime to)
        {
            string periodType = query == null ? "month" : (query.PeriodType ?? "month").ToLowerInvariant();
            string period = query == null ? null : query.Period;

            if (string.Equals(periodType, "year", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(period) || period.Length != 4)
                {
                    throw ApiException.ValidationFailed("年报期间格式应为 yyyy，如 2026");
                }
                int year;
                if (!int.TryParse(period, out year) || year < 1900 || year > 2100)
                {
                    throw ApiException.ValidationFailed("年报期间格式应为 yyyy，如 2026");
                }
                from = new DateTime(year, 1, 1);
                to = new DateTime(year, 12, 31);
                return;
            }

            if (string.Equals(periodType, "quarter", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(period) || period.Length != 7 || period[4] != '-' || (period[5] != 'Q' && period[5] != 'q'))
                {
                    throw ApiException.ValidationFailed("季度期间格式应为 yyyy-Qn，如 2026-Q3");
                }
                int year;
                if (!int.TryParse(period.Substring(0, 4), out year) || year < 1900 || year > 2100)
                {
                    throw ApiException.ValidationFailed("季度期间格式应为 yyyy-Qn，如 2026-Q3");
                }
                int quarter;
                if (!int.TryParse(period.Substring(6, 1), out quarter) || quarter < 1 || quarter > 4)
                {
                    throw ApiException.ValidationFailed("季度期间格式应为 yyyy-Qn（n=1~4），如 2026-Q3");
                }
                from = new DateTime(year, (quarter - 1) * 3 + 1, 1);
                to = from.AddMonths(3).AddDays(-1);
                return;
            }

            if (string.IsNullOrWhiteSpace(period) || period.Length != 7 || period[4] != '-')
            {
                throw ApiException.ValidationFailed("月度期间格式应为 yyyy-MM，如 2026-08");
            }
            int y;
            int m;
            if (!int.TryParse(period.Substring(0, 4), out y) ||
                !int.TryParse(period.Substring(5, 2), out m) ||
                y < 1900 || y > 2100 ||
                m < 1 || m > 12)
            {
                throw ApiException.ValidationFailed("月度期间格式应为 yyyy-MM，如 2026-08");
            }
            from = new DateTime(y, m, 1);
            to = from.AddMonths(1).AddDays(-1);
        }

        private static FinancialReportWindow ResolveWindow(FinancialReportQueryRequest query)
        {
            FinancialReportWindow window = new FinancialReportWindow();
            DateTime from, to;
            ResolvePeriod(query, out from, out to);
            window.From = from;
            window.To = to;
            bool isYear = query != null && string.Equals(query.PeriodType, "year", StringComparison.OrdinalIgnoreCase);
            bool isQuarter = query != null && string.Equals(query.PeriodType, "quarter", StringComparison.OrdinalIgnoreCase);
            if (isYear)
            {
                window.PrevFrom = from.AddYears(-1);
            }
            else if (isQuarter)
            {
                window.PrevFrom = from.AddMonths(-3);
            }
            else
            {
                window.PrevFrom = from.AddMonths(-1);
            }
            window.PrevTo = from.AddDays(-1);
            window.QuarterFrom = new DateTime(from.Year, ((from.Month - 1) / 3) * 3 + 1, 1);
            window.QuarterTo = to;
            window.ChargeItemId = query == null ? (int?)null : query.ChargeItemId;
            window.IncludeRefund = query == null || !query.IncludeRefund.HasValue || query.IncludeRefund.Value;

            if (query != null && !string.IsNullOrWhiteSpace(query.ComparePeriod))
            {
                string cp = query.ComparePeriod.Trim();
                if (isYear)
                {
                    window.PrevFrom = new DateTime(int.Parse(cp), 1, 1);
                    window.PrevTo = new DateTime(int.Parse(cp), 12, 31);
                }
                else if (isQuarter)
                {
                    int year = int.Parse(cp.Substring(0, 4));
                    int quarter = int.Parse(cp.Substring(6, 1));
                    window.PrevFrom = new DateTime(year, (quarter - 1) * 3 + 1, 1);
                    window.PrevTo = window.PrevFrom.AddMonths(3).AddDays(-1);
                }
                else
                {
                    window.PrevFrom = new DateTime(int.Parse(cp.Substring(0, 4)), int.Parse(cp.Substring(5, 2)), 1);
                    window.PrevTo = window.PrevFrom.AddMonths(1).AddDays(-1);
                }
            }
            return window;
        }

        // ---------- Excel 导出（ClosedXML） ----------
        private static void ExportExcel(string filePath, string period, FinancialReportDto report, bool annualWindow)
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("财务报表");

                sheet.Cell(1, 1).Value = "安怡物业 · 财务报表";
                sheet.Cell(2, 1).Value = "期间：" + period;
                sheet.Cell(2, 1).Style.Font.FontSize = 11;

                sheet.Cell(4, 1).Value = "本期收入";
                sheet.Cell(4, 2).Value = report.IncomeTotal;
                sheet.Cell(4, 3).Value = "环比 " + report.IncomeMomPercent.ToString("0.0") + "%";
                sheet.Cell(5, 1).Value = "本期支出";
                sheet.Cell(5, 2).Value = report.ExpenseTotal;
                sheet.Cell(5, 3).Value = "环比 " + report.ExpenseMomPercent.ToString("0.0") + "%";
                sheet.Cell(6, 1).Value = "本期结余";
                sheet.Cell(6, 2).Value = report.Balance;

                sheet.Cell(8, 1).Value = "项目";
                sheet.Cell(8, 2).Value = "本期金额";
                sheet.Cell(8, 3).Value = "上期金额";
                sheet.Cell(8, 4).Value = "环比";
                sheet.Cell(8, 5).Value = annualWindow ? "本年累计" : "本季累计";
                sheet.Cell(8, 6).Value = "备注";
                int row = 9;
                foreach (FinancialSummaryItemDto item in report.SummaryItems ?? new List<FinancialSummaryItemDto>())
                {
                    sheet.Cell(row, 1).Value = item.Category;
                    sheet.Cell(row, 2).Value = item.CurrentAmount;
                    sheet.Cell(row, 3).Value = item.PreviousAmount;
                    sheet.Cell(row, 4).Value = item.MoM.HasValue ? item.MoM.Value.ToString("0.0") + "%" : "—";
                    sheet.Cell(row, 5).Value = item.QuarterTotal;
                    sheet.Cell(row, 6).Value = item.Remark;
                    row++;
                }
                workbook.SaveAs(filePath);
            }
        }

        // ---------- PDF 导出（PDFsharp，中文用 SimHei 字体解析） ----------
        /// <summary>
        /// CHG-v1.1.2-41：导出「退款/减免/调整单据」PDF（A4 一页），供提交后留档与审计追溯。
        /// 口径：只收单据主键 —— 金额、账单口径（应收/实缴/未收/状态）、缴费对象、经办人一律由服务端回查库内数据，
        /// 保证 PDF 与本系统记录一致；导出动作本身写 t_report_log 与审计日志（REFUND_EXPORT）。
        /// </summary>
        public ReportLogDto ExportRefundRecord(RefundRecordExportRequest request,
            string operatorName = null, string ip = null)
        {
            if (request == null || request.RefundId <= 0)
            {
                throw ApiException.ValidationFailed("请选择要导出的单据（退款/减免/调整记录）");
            }

            RefundRecordDetailDto record;
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                record = _finance.GetRefundRecord(connection, request.RefundId);
            }
            if (record == null)
            {
                throw ApiException.NotFound("单据不存在或已删除");
            }

            string fileName = "refund_" + SanitizeFileName(record.RefNo) + "_" + DateTime.Now.ToString("HHmmss") + ".pdf";
            string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);
            ExportRefundRecordPdf(filePath, record, request.Remark, operatorName);

            var log = new ReportLogDto
            {
                ReportType = "refund",
                Period = record.CreatedAt.ToString("yyyy-MM-dd"),
                Format = ExportFormat.Pdf,
                FilePath = filePath
            };

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                log.Id = _finance.InsertReportLog(connection, transaction, log);
                transaction.Commit();
                log = _finance.GetReportLog(connection, log.Id) ?? log;
            }

            _audit.Write("REFUND_EXPORT", "bill", record.BillId > 0 ? record.BillId.ToString() : "0",
                string.Format("导出单据 {0}（{1} {2:0.00} 元）PDF：{3}",
                    record.RefNo, RefundTypeText(record.RefundType), record.Amount, Path.GetFileName(filePath)),
                userName: operatorName, ip: ip, result: "成功");
            return log;
        }

        /// <summary>文件名安全化：去掉路径分隔符与非法字符（单据编号形如 RF-20260920142109252-35）。</summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { return "record"; }
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }

        private static string RefundTypeText(RefundType type)
        {
            switch (type)
            {
                case RefundType.Refund: return "退款申请";
                case RefundType.Discount: return "费用减免";
                default: return "财务调整";
            }
        }

        /// <summary>归档类型代码（ASCII，便于纸面/电子归档与自动核对）。</summary>
        private static string RefundTypeCode(RefundType type)
        {
            switch (type)
            {
                case RefundType.Refund: return "REFUND";
                case RefundType.Discount: return "DISCOUNT";
                default: return "ADJUSTMENT";
            }
        }

        private static string BillStatusText(BillStatus? status)
        {
            if (!status.HasValue) { return "—"; }
            switch (status.Value)
            {
                case BillStatus.Pending: return "待缴";
                case BillStatus.Partial: return "部分缴";
                case BillStatus.Overdue: return "逾期";
                case BillStatus.Paid: return "已缴";
                case BillStatus.Reversed: return "已冲正";
                default: return "草稿";
            }
        }

        /// <summary>缴费对象展示文本（与收款登记「姓名 · 楼栋 · 房号」同口径）。</summary>
        private static string RefundObjectText(RefundRecordDetailDto record)
        {
            if (record == null) { return "—"; }
            if (record.BillId <= 0) { return "无关联账单"; }
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(record.PayerName))
            {
                parts.Add("自定义缴费对象 " + record.PayerName.Trim());
            }
            if (!string.IsNullOrWhiteSpace(record.OwnerName)) { parts.Add(record.OwnerName.Trim()); }
            if (!string.IsNullOrWhiteSpace(record.SpaceNo)) { parts.Add("车位 " + record.SpaceNo.Trim()); }
            else if (!string.IsNullOrWhiteSpace(record.RoomNo))
            {
                parts.Add((record.BuildingNo ?? string.Empty).Trim() + " " + record.RoomNo.Trim());
            }
            return parts.Count == 0 ? "—" : string.Join(" · ", parts.ToArray());
        }

        /// <summary>
        /// 单据 PDF 版面（A4 一页）：标题 + 单据要素 + 关联账单口径 + 本次登记 + 落账说明 + 导出留痕。
        /// 字体与「收据打印模板」同源（PdfFontSupport + SimHei）；金额统一用「元」，避免 ¥ 字形缺失渲染成 cid。
        /// </summary>
        private static void ExportRefundRecordPdf(string filePath, RefundRecordDetailDto record,
            string remark, string operatorName)
        {
            PdfFontSupport.Ensure();
            using (var document = new PdfDocument())
            {
                PdfPage page = document.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                using (XGraphics gfx = XGraphics.FromPdfPage(page))
                {
                    var titleFont = new XFont("SimHei", 16, XFontStyleEx.Bold);
                    var labelFont = new XFont("SimHei", 9, XFontStyleEx.Bold);
                    var bodyFont = new XFont("SimHei", 9, XFontStyleEx.Regular);
                    var noteFont = new XFont("SimHei", 8, XFontStyleEx.Regular);

                    string typeText = RefundTypeText(record.RefundType);
                    double left = 40, col2 = 320, y = 40;
                    gfx.DrawString("安怡物业 · " + typeText + "单据（留档/审计用）", titleFont, XBrushes.Black, left, y);
                    y += 20;
                    gfx.DrawLine(XPens.Gray, left, y, 555, y);
                    y += 20;

                    gfx.DrawString("单据编号：" + (string.IsNullOrEmpty(record.RefNo) ? "—" : record.RefNo), bodyFont, XBrushes.Black, left, y);
                    gfx.DrawString("登记时间：" + record.CreatedAt.ToString("yyyy-MM-dd HH:mm"), bodyFont, XBrushes.Black, col2, y);
                    y += 16;
                    gfx.DrawString("经办人：" + (string.IsNullOrEmpty(record.OperatorName) ? "—" : record.OperatorName), bodyFont, XBrushes.Black, left, y);
                    gfx.DrawString("处理方式：" + (string.IsNullOrEmpty(record.Method) ? "—" : record.Method), bodyFont, XBrushes.Black, col2, y);
                    y += 16;
                    gfx.DrawString("本次金额：" + record.Amount.ToString("N2") + " 元" + AmountDirectionText(record), bodyFont, XBrushes.Black, left, y);
                    gfx.DrawString("附件：" + (string.IsNullOrEmpty(record.AttachmentName) ? "无" : record.AttachmentName), bodyFont, XBrushes.Black, col2, y);
                    y += 16;
                    // 归档类型代码 + 单据主键：便于归档检索与自动核对（纸面单据同样可读）
                    gfx.DrawString("类型代码：" + RefundTypeCode(record.RefundType), bodyFont, XBrushes.Black, left, y);
                    gfx.DrawString("单据主键：" + record.Id, bodyFont, XBrushes.Black, col2, y);
                    y += 22;

                    gfx.DrawString("一、关联账单", labelFont, XBrushes.Black, left, y);
                    y += 15;
                    if (record.BillId > 0)
                    {
                        gfx.DrawString("账单号：BILL-" + record.BillId.ToString("D4"), bodyFont, XBrushes.Black, left, y);
                        gfx.DrawString("收费项目：" + (string.IsNullOrEmpty(record.ChargeItemName) ? "—" : record.ChargeItemName), bodyFont, XBrushes.Black, col2, y);
                        y += 16;
                        gfx.DrawString("缴费对象：" + RefundObjectText(record), bodyFont, XBrushes.Black, left, y);
                        gfx.DrawString("账单期间：" + (string.IsNullOrEmpty(record.CyclePeriod) ? "—" : record.CyclePeriod), bodyFont, XBrushes.Black, col2, y);
                        y += 16;
                        decimal amount = record.BillAmount ?? 0m;
                        decimal paid = record.BillPaidAmount ?? 0m;
                        decimal unreceived = amount - paid;
                        if (unreceived < 0m) { unreceived = 0m; }
                        gfx.DrawString("应收金额：" + amount.ToString("N2") + " 元", bodyFont, XBrushes.Black, left, y);
                        gfx.DrawString("已缴金额：" + paid.ToString("N2") + " 元", bodyFont, XBrushes.Black, col2, y);
                        y += 16;
                        gfx.DrawString("未收金额：" + unreceived.ToString("N2") + " 元", bodyFont, XBrushes.Black, left, y);
                        gfx.DrawString("账单状态：" + BillStatusText(record.BillStatus), bodyFont, XBrushes.Black, col2, y);
                        y += 16;
                        gfx.DrawString("（以上为导出时点的账单口径快照，与账单工作台/收款登记同源）", noteFont,
                            XBrushes.Gray, left, y);
                    }
                    else
                    {
                        gfx.DrawString("本单据为「无关联账单」的冲正/补收登记，不改变任何账单金额与状态。", bodyFont,
                            XBrushes.Black, left, y);
                    }
                    y += 24;

                    gfx.DrawString("二、登记详情", labelFont, XBrushes.Black, left, y);
                    y += 15;
                    foreach (string line in WrapText("原因：" + (string.IsNullOrEmpty(record.Reason) ? "—" : record.Reason), 52))
                    {
                        gfx.DrawString(line, bodyFont, XBrushes.Black, left, y);
                        y += 15;
                    }
                    if (!string.IsNullOrWhiteSpace(remark))
                    {
                        foreach (string line in WrapText("导出备注：" + remark.Trim(), 52))
                        {
                            gfx.DrawString(line, bodyFont, XBrushes.Black, left, y);
                            y += 15;
                        }
                    }
                    y += 9;

                    gfx.DrawString("三、落账口径", labelFont, XBrushes.Black, left, y);
                    y += 15;
                    foreach (string line in WrapText(LedgerBasisText(record), 52))
                    {
                        gfx.DrawString(line, bodyFont, XBrushes.Black, left, y);
                        y += 15;
                    }
                    y += 12;

                    gfx.DrawLine(XPens.LightGray, left, y, 555, y);
                    y += 15;
                    gfx.DrawString("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                        "　导出人：" + (string.IsNullOrEmpty(operatorName) ? "—" : operatorName), noteFont, XBrushes.Gray, left, y);
                    y += 13;
                    gfx.DrawString("本单据由系统按库内记录生成，编号可在「审计日志查询」中反查；如与系统记录不一致，以系统记录为准。",
                        noteFont, XBrushes.Gray, left, y);
                }
                document.Save(filePath);
            }
        }

        /// <summary>金额方向后缀（退款恒为冲减；减免为调减应收；调整按方式方向）。</summary>
        private static string AmountDirectionText(RefundRecordDetailDto record)
        {
            if (record.RefundType == RefundType.Adjustment)
            {
                // 注意：PDF 用 SimHei 子集字体，U+2212/U+FF0B 等符号无字形（渲染成方框），统一用 ASCII +/-
                return record.AdjustDir == 1 ? "（调增补收 +）" : "（调减冲正 -）";
            }
            return record.RefundType == RefundType.Discount ? "（调减应收）" : "（冲减实缴）";
        }

        /// <summary>落账口径说明 —— 让单据本身可自解释（退款动实缴、减免动应收）。</summary>
        private static string LedgerBasisText(RefundRecordDetailDto record)
        {
            switch (record.RefundType)
            {
                case RefundType.Discount:
                    return "费用减免＝直接调减账单应收金额（本单据金额已从应收中扣除），已收金额不变，不产生资金流出；" +
                           "减免上限为账单未收余额（应收 - 已缴）。";
                case RefundType.Refund:
                    return "退款＝冲减账单已收金额（实缴），账单应收金额不变；剩余未收金额继续保留在收款登记应缴明细，可继续收取。";
                default:
                    return record.AdjustDir == 1
                        ? "账务调整（调增补收）＝增加账单已收金额，计入收入方向。"
                        : "账务调整（调减冲正）＝冲减账单已收金额；未指定方式时按冲减登记。";
            }
        }

        /// <summary>按字符数换行（中文按等宽估算），避免长原因/备注跑出页面右边距。</summary>
        private static List<string> WrapText(string text, int perLine)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) { return lines; }
            string remaining = text;   // 同一段落内已含换行符时逐行处理
            foreach (string paragraph in remaining.Split('\n'))
            {
                string current = paragraph.TrimEnd('\r');
                while (current.Length > perLine)
                {
                    lines.Add(current.Substring(0, perLine));
                    current = current.Substring(perLine);
                }
                lines.Add(current);
            }
            return lines;
        }

        /// <summary>
        /// 导出「收据打印模板」（CHG-v1.1.0-14）：
        public ReportLogDto ExportReceiptTemplate(ReceiptTemplateRequest request)
        {
            if (request == null || request.Items == null || request.Items.Count == 0)
            {
                throw ApiException.ValidationFailed("没有可导出的收款明细，请先完成收款");
            }

            string period = request.PaidAt == default(DateTime) ? DateTime.Now.ToString("yyyyMMddHHmmss")
                : request.PaidAt.ToString("yyyyMMddHHmmss");
            string fileName = "receipt_" + period + "_" + DateTime.Now.ToString("fff") + ".pdf";
            string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);
            ExportReceiptPdf(filePath, request);

            var log = new ReportLogDto
            {
                ReportType = "receipt",
                Period = request.PaidAt == default(DateTime) ? DateTime.Now.ToString("yyyy-MM-dd") : request.PaidAt.ToString("yyyy-MM-dd"),
                Format = ExportFormat.Pdf,
                FilePath = filePath
            };

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                log.Id = _finance.InsertReportLog(connection, transaction, log);
                transaction.Commit();
                return _finance.GetReportLog(connection, log.Id) ?? log;
            }
        }

        /// <summary>账单期间压缩显示：同年省略结束年份（2026-09-01 ~ 2026-09-30 → 2026-09-01~09-30）。</summary>
        /// <summary>
        /// CHG-v1.1.0-19：是否需要隐藏「缴费对象」列。
        /// 口径：请求显式声明（客户端判定为自定义缴费对象）**且**全部明细的缴费对象都等于缴款人名称 ——
        /// 两者同时满足才隐藏，避免误隐藏真实对象信息。
        /// </summary>
        private static bool ShouldHideObjectColumn(ReceiptTemplateRequest request)
        {
            if (request == null || !request.HideObjectColumn) { return false; }
            string payee = (request.PayeeName ?? string.Empty).Trim();
            if (payee.Length == 0 || request.Items == null || request.Items.Count == 0) { return false; }
            foreach (ReceiptTemplateItemRequest item in request.Items)
            {
                string objectText = (item == null ? string.Empty : (item.ObjectText ?? string.Empty)).Trim();
                // 空文本＝客户端未提供对象信息（自定义缴费对象场景），以客户端声明为准；
                // 一旦提供了非空对象文本，则必须与缴款人一致，否则视为真实对象信息，保留该列。
                if (objectText.Length > 0 && !string.Equals(objectText, payee, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static string ShortPeriod(string period)
        {
            if (string.IsNullOrWhiteSpace(period)) { return "—"; }
            string compact = period.Replace(" ", string.Empty);
            string[] parts = compact.Split('~');
            if (parts.Length == 2 && parts[0].Length >= 10 && parts[1].Length >= 10 &&
                parts[0].Substring(0, 4) == parts[1].Substring(0, 4))
            {
                return parts[0] + "~" + parts[1].Substring(5);
            }
            return compact;
        }

        private static void ExportReceiptPdf(string filePath, ReceiptTemplateRequest request)
        {
            PdfFontSupport.Ensure();
            using (var document = new PdfDocument())
            {
                PdfPage page = document.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                using (XGraphics gfx = XGraphics.FromPdfPage(page))
                {
                    var titleFont = new XFont("SimHei", 16, XFontStyleEx.Bold);
                    var headerFont = new XFont("SimHei", 10, XFontStyleEx.Bold);
                    var bodyFont = new XFont("SimHei", 9, XFontStyleEx.Regular);

                    double y = 40;
                    gfx.DrawString("安怡物业 · 收款收据（打印模板）", titleFont, XBrushes.Black, 40, y);
                    y += 26;
                    gfx.DrawString("缴款人：" + (request.PayeeName ?? "—"), bodyFont, XBrushes.Black, 40, y);
                    gfx.DrawString("收款日期：" + (request.PaidAt == default(DateTime) ? "—" : request.PaidAt.ToString("yyyy-MM-dd")), bodyFont, XBrushes.Black, 240, y);
                    y += 16;
                    gfx.DrawString("收款方式：" + (string.IsNullOrEmpty(request.PayMethod) ? "—" : request.PayMethod), bodyFont, XBrushes.Black, 40, y);
                    gfx.DrawString("经手人：" + (string.IsNullOrEmpty(request.HandlerName) ? "—" : request.HandlerName), bodyFont, XBrushes.Black, 240, y);
                    y += 16;
                    gfx.DrawString("收款流水号：" + (string.IsNullOrEmpty(request.BatchNo) ? "单张收款（无批量流水号）" : request.BatchNo),
                        bodyFont, XBrushes.Black, 40, y);
                    y += 24;

                    // CHG-v1.1.0-19：自定义缴费对象（缴款人＝缴费对象，同一名称）时过滤「缴费对象」列，
                    // 避免同一名称重复两列造成歧义；其余场景保持五列（CHG-v1.1.0-15 列宽口径）。
                    bool hideObjectColumn = ShouldHideObjectColumn(request);
                    double colObject = 40, colItem = 130, colPeriod = 234, colBill = 394, colAmount = 478;
                    if (hideObjectColumn)
                    {
                        colItem = 40; colPeriod = 200; colBill = 360; colAmount = 478;
                    }
                    if (!hideObjectColumn)
                    {
                        gfx.DrawString("缴费对象", headerFont, XBrushes.Black, colObject, y);
                    }
                    gfx.DrawString("收费项目", headerFont, XBrushes.Black, colItem, y);
                    gfx.DrawString("账单期间", headerFont, XBrushes.Black, colPeriod, y);
                    gfx.DrawString("账单号", headerFont, XBrushes.Black, colBill, y);
                    gfx.DrawString("金额", headerFont, XBrushes.Black, colAmount, y);
                    y += 6;
                    gfx.DrawLine(XPens.Gray, 40, y, 555, y);
                    double tableTop = y;
                    y += 14;
                    double tableBottom = y;

                    decimal total = 0m;
                    foreach (ReceiptTemplateItemRequest item in request.Items)
                    {
                        if (y > 760)
                        {
                            gfx.DrawString("…（本页已满，其余明细请见后续收款记录）", bodyFont, XBrushes.Gray, 40, y);
                            break;
                        }
                        if (!hideObjectColumn)
                        {
                            gfx.DrawString(Crop(item.ObjectText, 12), bodyFont, XBrushes.Black, colObject, y);
                        }
                        gfx.DrawString(Crop(item.ChargeItemName, 14), bodyFont, XBrushes.Black, colItem, y);
                        gfx.DrawString(Crop(ShortPeriod(item.CyclePeriod), 23), bodyFont, XBrushes.Black, colPeriod, y);
                        gfx.DrawString(Crop(item.BillNo, 14), bodyFont, XBrushes.Black, colBill, y);
                        gfx.DrawString(item.Amount.ToString("0.00"), bodyFont, XBrushes.Black, colAmount, y);
                        total += item.Amount;
                        y += 14;
                        tableBottom = y;
                    }

                    // 列分隔线：视觉上把各列固定住，避免长文本跨列看起来像「遮挡」
                    if (!hideObjectColumn)
                    {
                        gfx.DrawLine(XPens.LightGray, 128, tableTop, 128, tableBottom);
                    }
                    gfx.DrawLine(XPens.LightGray, colPeriod - 2, tableTop, colPeriod - 2, tableBottom);
                    gfx.DrawLine(XPens.LightGray, colBill - 2, tableTop, colBill - 2, tableBottom);
                    gfx.DrawLine(XPens.LightGray, 476, tableTop, 476, tableBottom);

                    y += 6;
                    gfx.DrawLine(XPens.Gray, 40, y, 555, y);
                    y += 16;
                    // 说明：PDFsharp + SimHei 子集不含「¥」字形（会渲染成 cid），统一用「元」表述金额
                    gfx.DrawString("合计：" + request.Items.Count + " 笔，" + total.ToString("0.00") + " 元",
                        headerFont, XBrushes.Black, 380, y);
                    y += 24;
                    if (!string.IsNullOrWhiteSpace(request.Remark))
                    {
                        gfx.DrawString("备注：" + Crop(request.Remark.Trim(), 40), bodyFont, XBrushes.Black, 40, y);
                        y += 16;
                    }
                    gfx.DrawString("收款单位：安怡物业服务中心", bodyFont, XBrushes.Black, 40, y);

                    document.Save(filePath);
                }
            }
        }

        private static void ExportPdf(string filePath, string period, FinancialReportDto report, bool annualWindow)
        {
            PdfFontSupport.Ensure();

            using (var document = new PdfDocument())
            {
                PdfPage page = document.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                using (XGraphics gfx = XGraphics.FromPdfPage(page))
                {
                    var titleFont = new XFont("SimHei", 16, XFontStyleEx.Bold);
                    var headerFont = new XFont("SimHei", 10, XFontStyleEx.Bold);
                    var bodyFont = new XFont("SimHei", 9, XFontStyleEx.Regular);

                    double y = 30;
                    gfx.DrawString("安怡物业 · 财务报表", titleFont, XBrushes.Black, 40, y);
                    y += 22;
                    gfx.DrawString("期间：" + period, bodyFont, XBrushes.Black, 40, y);
                    y += 24;

                    gfx.DrawString("本期收入：" + report.IncomeTotal.ToString("0.00") + " 元（环比 " + report.IncomeMomPercent.ToString("0.0") + "%）", bodyFont, XBrushes.Black, 40, y);
                    y += 16;
                    gfx.DrawString("本期支出：" + report.ExpenseTotal.ToString("0.00") + " 元（环比 " + report.ExpenseMomPercent.ToString("0.0") + "%）", bodyFont, XBrushes.Black, 40, y);
                    y += 16;
                    gfx.DrawString("本期结余：" + report.Balance.ToString("0.00") + " 元", bodyFont, XBrushes.Black, 40, y);
                    y += 24;

                    gfx.DrawString("项目", headerFont, XBrushes.Black, 40, y);
                    gfx.DrawString("本期金额", headerFont, XBrushes.Black, 150, y);
                    gfx.DrawString("上期金额", headerFont, XBrushes.Black, 260, y);
                    gfx.DrawString("环比", headerFont, XBrushes.Black, 360, y);
                    gfx.DrawString(annualWindow ? "本年累计" : "本季累计", headerFont, XBrushes.Black, 430, y);
                    gfx.DrawString("备注", headerFont, XBrushes.Black, 520, y);
                    y += 16;

                    foreach (FinancialSummaryItemDto item in report.SummaryItems ?? new List<FinancialSummaryItemDto>())
                    {
                        gfx.DrawString(Crop(item.Category, 12), bodyFont, XBrushes.Black, 40, y);
                        gfx.DrawString(item.CurrentAmount.ToString("0.00"), bodyFont, XBrushes.Black, 150, y);
                        gfx.DrawString(item.PreviousAmount.ToString("0.00"), bodyFont, XBrushes.Black, 260, y);
                        gfx.DrawString(item.MoM.HasValue ? item.MoM.Value.ToString("0.0") + "%" : "—", bodyFont, XBrushes.Black, 360, y);
                        gfx.DrawString(item.QuarterTotal.ToString("0.00"), bodyFont, XBrushes.Black, 430, y);
                        gfx.DrawString(Crop(item.Remark, 8), bodyFont, XBrushes.Black, 520, y);
                        y += 14;
                        if (y > page.Height.Point - 40)
                        {
                            break;
                        }
                    }
                }
                document.Save(filePath);
            }
        }

        private static string Crop(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "…";
        }

        /// <summary>
        /// CHG-v1.1.2-23：按**实际渲染宽度**裁剪文本（XGraphics.MeasureString）——
        /// 只有真正放不下时才截断并加省略号，避免固定字数上限把「关联单据」这类关键编号截成 RF-20260919…。
        /// </summary>
        private static string FitToWidth(XGraphics gfx, string text, XFont font, double maxWidth)
        {
            if (string.IsNullOrEmpty(text)) { return string.Empty; }
            if (gfx.MeasureString(text, font).Width <= maxWidth) { return text; }

            for (int len = text.Length - 1; len > 0; len--)
            {
                string candidate = text.Substring(0, len) + "…";
                if (gfx.MeasureString(candidate, font).Width <= maxWidth) { return candidate; }
            }
            return "…";
        }

        // ---------- 收支明细流水导出（CHG-v1.1.2-05） ----------
        private static void ExportLedgerExcel(string filePath, List<LedgerEntryDto> items)
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("收支明细流水");
                sheet.Cell(1, 1).Value = "安怡物业 · 收支明细流水";
                sheet.Cell(2, 1).Value = "导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "，共 " + items.Count + " 条";

                string[] headers = { "流水号", "日期", "收/支", "项目", "楼栋/房号", "付款人", "金额", "支付方式", "关联单据", "经手人" };
                int start = 4;
                for (int c = 1; c <= headers.Length; c++)
                {
                    sheet.Cell(start, c).Value = headers[c - 1];
                    sheet.Cell(start, c).Style.Font.Bold = true;
                }

                int row = start + 1;
                foreach (LedgerEntryDto item in items)
                {
                    sheet.Cell(row, 1).Value = "LS-" + item.Id.ToString("D4");
                    sheet.Cell(row, 2).Value = item.BizTime.ToString("yyyy-MM-dd HH:mm");
                    sheet.Cell(row, 3).Value = item.InAmount > 0 ? "收" : "支";
                    sheet.Cell(row, 4).Value = item.Subject ?? string.Empty;
                    sheet.Cell(row, 5).Value = item.ObjectText ?? string.Empty;
                    sheet.Cell(row, 6).Value = item.OwnerName ?? string.Empty;
                    sheet.Cell(row, 7).Value = item.InAmount != 0 ? item.InAmount : -item.OutAmount;
                    sheet.Cell(row, 8).Value = item.PayMethod ?? string.Empty;
                    sheet.Cell(row, 9).Value = item.BizNo ?? string.Empty;
                    sheet.Cell(row, 10).Value = item.OperatorName ?? string.Empty;
                    row++;
                }
                if (items.Count > 0)
                {
                    decimal income = 0m;
                    decimal outcome = 0m;
                    foreach (LedgerEntryDto item in items)
                    {
                        if (item.InAmount > 0) { income += item.InAmount; }
                        if (item.OutAmount > 0) { outcome += item.OutAmount; }
                    }
                    // CHG-v1.1.2-20：合计区块由「表格中部（D/E 列）」左移到 **A 列起**，与明细表左对齐
                    int sumRow = row + 1;
                    sheet.Cell(sumRow, 1).Value = "合计";
                    sheet.Cell(sumRow, 2).Value = "收入 " + income.ToString("0.00") + " 元";
                    sheet.Cell(sumRow, 3).Value = "支出 " + outcome.ToString("0.00") + " 元";
                    sheet.Cell(sumRow, 4).Value = "结余 " + (income - outcome).ToString("0.00") + " 元";
                    for (int c = 1; c <= 4; c++)
                    {
                        sheet.Cell(sumRow, c).Style.Font.Bold = true;
                        sheet.Cell(sumRow, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    }
                }
                for (int c = 1; c <= headers.Length; c++) { sheet.Column(c).Width = 16; }
                workbook.SaveAs(filePath);
            }
        }

        private static void ExportLedgerPdf(string filePath, List<LedgerEntryDto> items)
        {
            PdfFontSupport.Ensure();

            // CHG-v1.4.0-05（负责人 2026-10-08）：修两个缺陷 ——
            //   ① 原实现单页画满即 `break`，超过一页的流水被**静默丢弃**（导出上限 5000 行，必然截断）；
            //   ② 原模板没有数据合计（Excel 有、PDF 没有）。
            // 现改为分页输出 + 末页合计「收入/支出/结余/条数」，与 Excel 合计口径一致。
            var titleFont = new XFont("SimHei", 15, XFontStyleEx.Bold);
            var headerFont = new XFont("SimHei", 9, XFontStyleEx.Bold);
            var bodyFont = new XFont("SimHei", 8, XFontStyleEx.Regular);

            // CHG-v1.1.2-21 / CHG-v1.1.2-23：PDF 列布局按 A4 版面重排 ——
            // 每列先按「实际量宽（MeasureString）」裁剪，只有真正放不下才截断，
            // 并把「关联单据」列加宽到 120pt，使 RF-yyyyMMddHHmmssfff-NN / PAY-yyyyMMdd-NNNN 完整显示。
            string[] headers = { "流水号", "日期", "收/支", "项目", "楼栋/房号/单元", "付款人", "金额", "关联单据" };
            double[] xs = { 36, 88, 150, 174, 244, 330, 382, 440 };
            double[] widths = { 50, 60, 22, 68, 84, 50, 56, 120 };

            decimal income = 0m;
            decimal outcome = 0m;
            foreach (LedgerEntryDto item in items)
            {
                if (item.InAmount > 0) { income += item.InAmount; }
                if (item.OutAmount > 0) { outcome += item.OutAmount; }
            }

            using (var document = new PdfDocument())
            using (var canvas = new PdfTableCanvas(document, titleFont, headerFont, bodyFont))
            {
                canvas.Title("安怡物业 · 收支明细流水");
                canvas.Line("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "，共 " + items.Count + " 条");
                canvas.Gap(2);
                canvas.Head(headers, xs);

                foreach (LedgerEntryDto item in items)
                {
                    canvas.EnsureRow(14, headers, xs);
                    decimal signed = item.InAmount != 0 ? item.InAmount : -item.OutAmount;
                    string[] cells =
                    {
                        "LS-" + item.Id.ToString("D4"),
                        item.BizTime.ToString("MM-dd HH:mm"),
                        item.InAmount > 0 ? "收" : "支",
                        item.Subject,
                        item.ObjectText,
                        item.OwnerName,
                        signed.ToString("0.00"),
                        item.BizNo
                    };
                    for (int i = 0; i < cells.Length; i++)
                    {
                        canvas.Cell(FitToWidth(canvas.Gfx, cells[i], bodyFont, widths[i]), xs[i]);
                    }
                    canvas.NextRow(13);
                }

                canvas.Gap(6);
                canvas.Line("合计：收入 " + income.ToString("0.00") + " 元　　支出 " + outcome.ToString("0.00") +
                            " 元　　结余 " + (income - outcome).ToString("0.00") + " 元　　共 " + items.Count + " 条");
                document.Save(filePath);
            }
        }

        // ---------- 账单工作台：批次明细导出（CHG-v1.4.0-03） ----------

        /// <summary>账单批次明细导出上限（一次导出最多 5000 行明细）。</summary>
        private const int BillBatchExportMaxRows = 5000;

        /// <summary>
        /// 账单工作台「导出PDF」（CHG-v1.4.0-03，负责人 2026-10-08 裁定 A）：
        /// 口径 = **当前筛选下的批次清单 + 每批次缴费对象明细**。
        ///
        /// 设计：批次/明细主键由客户端按页面当前筛选结果给出，服务端按主键**回查**批次与账单
        /// （金额、对象、状态一律取库内值，不采信客户端传值），保证导出与页面同口径且可核对；
        /// 每批次一段：抬头（批次号 / 项目 / 期间 / 户数 / 总金额 / 状态 / 发布时间）
        /// + 明细表（缴费人 / 楼栋·单元·房号 / 收费项目 / 应缴 / 已收 / 状态 / 到期日）+ 小计。
        ///
        /// 原口径下账单工作台**没有任何导出**，已发布批次只显示对象数与金额，看不出是哪个对象、缴费人是谁。
        /// 文件写 t_report_log（report_type = bill_batch），下载走 /reports/files/{id}；本导出只读。
        /// </summary>
        public ReportLogDto ExportBillBatches(BillBatchExportRequest request, string operatorName = null)
        {
            if (request == null || request.BatchIds == null || request.BatchIds.Count == 0)
            {
                throw ApiException.BadRequest("当前筛选下没有可导出的账单批次");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                Dictionary<int, BillBatchDto> batches = (_finance.QueryGenerateLogs(connection) ?? new List<BillBatchDto>())
                    .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());

                var sections = new List<BillBatchExportSection>();
                int rowCount = 0;
                foreach (int batchId in request.BatchIds.Distinct())
                {
                    if (rowCount >= BillBatchExportMaxRows) { break; }
                    BillBatchDto batch;
                    if (!batches.TryGetValue(batchId, out batch)) { continue; }

                    var items = new List<BillListItemDto>();
                    items.AddRange(_finance.ListPublishedBillsByBatch(connection, batchId) ?? new List<BillListItemDto>());
                    items.AddRange(_finance.ListDraftBillsByBatch(connection, batchId) ?? new List<BillListItemDto>());
                    items = items.GroupBy(x => x.Id).Select(g => g.First())
                        .OrderBy(x => x.DueAt).ThenBy(x => x.Id).ToList();
                    if (rowCount + items.Count > BillBatchExportMaxRows)
                    {
                        items = items.Take(BillBatchExportMaxRows - rowCount).ToList();
                    }
                    rowCount += items.Count;

                    sections.Add(new BillBatchExportSection
                    {
                        Batch = batch,
                        Items = items,
                        Truncated = false
                    });
                }

                if (sections.Count == 0)
                {
                    throw ApiException.NotFound("没有找到可导出的账单批次（批次可能已被删除）");
                }

                string period = DateTime.Now.ToString("yyyyMMddHHmmss");
                string fileName = "bill_batch_" + period + ".pdf";
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);
                WriteBillBatchPdf(filePath, sections, rowCount, operatorName);

                var log = new ReportLogDto
                {
                    ReportType = "bill_batch",
                    Period = period,
                    Format = ExportFormat.Pdf,
                    FilePath = filePath
                };
                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    log.Id = _finance.InsertReportLog(connection, transaction, log);
                    transaction.Commit();
                }

                _audit.Write("BILL_BATCH_EXPORT", "report_log", log.Id.ToString(),
                    "账单工作台批次明细导出：" + sections.Count + " 个批次 / " + rowCount + " 条明细，文件 " + fileName,
                    userName: operatorName, result: "成功");

                ReportLogDto saved = _finance.GetReportLog(connection, log.Id);
                return saved ?? log;
            }
        }

        private sealed class BillBatchExportSection
        {
            public BillBatchDto Batch { get; set; }
            public List<BillListItemDto> Items { get; set; }
            public bool Truncated { get; set; }
        }

        private static void WriteBillBatchPdf(string filePath, List<BillBatchExportSection> sections,
            int rowCount, string operatorName)
        {
            PdfFontSupport.Ensure();

            var titleFont = new XFont("SimHei", 15, XFontStyleEx.Bold);
            var headFont = new XFont("SimHei", 10, XFontStyleEx.Bold);
            var bodyFont = new XFont("SimHei", 8, XFontStyleEx.Regular);

            string[] headers = { "缴费人", "楼栋/单元/房号", "收费项目", "应缴", "已收", "状态", "到期日" };
            double[] xs = { 36, 122, 250, 342, 390, 438, 486 };
            double[] widths = { 82, 124, 88, 44, 44, 44, 58 };

            using (var document = new PdfDocument())
            using (var canvas = new PdfTableCanvas(document, titleFont, headFont, bodyFont))
            {
                canvas.Title("安怡物业 · 账单批次缴费对象明细");
                canvas.Line("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "　批次：" +
                            sections.Count + " 个　明细：" + rowCount + " 条" +
                            (string.IsNullOrWhiteSpace(operatorName) ? string.Empty : "　操作人：" + operatorName));
                canvas.Gap(4);

                int index = 0;
                // CHG-v1.4.0-16（负责人 2026-10-08 第 2 轮反馈「导出的 PDF 缺少合计」）：文末给总计
                decimal grandAmount = 0m;
                decimal grandPaid = 0m;
                int grandRows = 0;
                foreach (BillBatchExportSection section in sections)
                {
                    index++;
                    canvas.EnsureRow(60, headers, xs);
                    canvas.Section("批次 " + index + "：" + Text(section.Batch.BatchNo) + "　｜　" +
                                   "收费项目：" + Text(section.Batch.ChargeItemName) + "　｜　" +
                                   "账单期间：" + Text(section.Batch.CyclePeriod), headFont, bodyFont);
                    canvas.Line("户数：" + section.Batch.HouseCount + "　总金额：" +
                                section.Batch.TotalAmount.ToString("0.00") + " 元　状态：" +
                                BillBatchStatusText(section.Batch) + "　发布时间：" + Text(section.Batch.PublishedAt) +
                                "　明细：" + section.Items.Count + " 条");
                    canvas.Head(headers, xs);

                    decimal amountSum = 0m;
                    decimal paidSum = 0m;
                    foreach (BillListItemDto item in section.Items)
                    {
                        canvas.EnsureRow(14, headers, xs);
                        amountSum += item.Amount;
                        paidSum += item.PaidAmount;
                        grandAmount += item.Amount;
                        grandPaid += item.PaidAmount;
                        grandRows++;
                        string[] cells =
                        {
                            BillPayerText(item),
                            BillObjectPath(item),
                            Text(item.ChargeItemName),
                            item.Amount.ToString("0.00"),
                            item.PaidAmount.ToString("0.00"),
                            BillBatchItemStatusText(item),
                            item.DueAt.ToString("yyyy-MM-dd")
                        };
                        for (int i = 0; i < cells.Length; i++)
                        {
                            canvas.Cell(FitToWidth(canvas.Gfx, cells[i], bodyFont, widths[i]), xs[i]);
                        }
                        canvas.NextRow(13);
                    }
                    canvas.Gap(2);
                    canvas.Line("小计：应缴 " + amountSum.ToString("0.00") + " 元　已收 " + paidSum.ToString("0.00") +
                                " 元　欠费 " + (amountSum - paidSum).ToString("0.00") + " 元");
                    canvas.Gap(10);
                }

                canvas.Gap(6);
                canvas.Line("总计：批次 " + sections.Count + " 个　　明细 " + grandRows + " 条　　应缴合计 " +
                            grandAmount.ToString("0.00") + " 元　　已收合计 " + grandPaid.ToString("0.00") +
                            " 元　　欠费合计 " + (grandAmount - grandPaid).ToString("0.00") + " 元");

                document.Save(filePath);
            }
        }

        /// <summary>批次状态文案（与账单工作台「状态」列口径一致）。</summary>
        private static string BillBatchStatusText(BillBatchDto batch)
        {
            if (batch == null) { return "—"; }
            if (batch.FailCount > 0 && batch.SuccessCount == 0 && batch.DraftCount == 0 && batch.PendingCount == 0
                && batch.PartialCount == 0 && batch.PaidCount == 0)
            {
                return "发布失败";
            }
            if (batch.PendingCount > 0 || batch.PartialCount > 0) { return "部分缴纳"; }
            if (batch.PaidCount > 0 && batch.DraftCount == 0 && batch.PendingCount == 0 && batch.PartialCount == 0)
            {
                return "已发布";
            }
            if (batch.DraftCount > 0) { return "草稿 / 部分发布"; }
            return "已发布";
        }

        /// <summary>单据状态（账单工作台明细口径：草稿 / 待缴 / 部分缴 / 已缴 / 逾期 / 已冲正）。</summary>
        private static string BillBatchItemStatusText(BillListItemDto item)
        {
            if (item == null) { return "—"; }
            if (item.Status == BillStatus.Draft) { return "草稿"; }
            if (item.PaidAmount >= item.Amount && item.Amount > 0m) { return "已缴"; }
            switch (item.Status)
            {
                case BillStatus.Partial: return "部分缴";
                case BillStatus.Overdue: return "逾期";
                case BillStatus.Reversed: return "已冲正";
                case BillStatus.Paid: return "已缴";
                default: return "待缴";
            }
        }

        /// <summary>缴费人文本：业主名优先，其次自定义缴费对象名称；房产无业主时标注「空置」。</summary>
        private static string BillPayerText(BillListItemDto item)
        {
            if (item == null) { return "—"; }
            if (!string.IsNullOrWhiteSpace(item.OwnerName)) { return item.OwnerName.Trim(); }
            if (!string.IsNullOrWhiteSpace(item.PayerName)) { return item.PayerName.Trim(); }
            return item.PropertyId.HasValue ? "—（空置）" : "—";
        }

        /// <summary>
        /// 缴费对象「楼栋/单元/房号」文本（口径与 SqlAddress.BuildingUnitRoom 一致）：
        /// 单元为空整段省略、已以「单元」结尾不重复补后缀；车位显示「车位 X」；无档案对象回落房产编号。
        /// </summary>
        private static string BillObjectPath(BillListItemDto item)
        {
            if (item == null) { return "—"; }
            if (!string.IsNullOrWhiteSpace(item.SpaceNo)) { return "车位 " + item.SpaceNo.Trim(); }

            string building = (item.BuildingNo ?? string.Empty).Trim();
            string unit = (item.UnitNo ?? string.Empty).Trim();
            string room = (item.RoomNo ?? string.Empty).Trim();
            string text = building;
            if (unit.Length > 0)
            {
                text += "/" + (unit.EndsWith("单元", StringComparison.Ordinal) ? unit : unit + "单元");
            }
            if (room.Length > 0) { text += "/" + room; }
            if (text.Length == 0) { text = (item.PropertyNo ?? string.Empty).Trim(); }
            return text.Length == 0 ? "—" : text;
        }

        // ---------- 欠费台账导出（CHG-v1.4.0-04） ----------

        /// <summary>欠费台账导出上限（一次导出最多 5000 行）。</summary>
        private const int ArrearLedgerExportMaxRows = 5000;

        /// <summary>
        /// 欠费台账「导出台账」（CHG-v1.4.0-04，负责人 2026-10-08 反馈「只有占位提示」）：
        /// 支持 **Excel / PDF**，口径 = 页面当前筛选下的台账行，文末给合计（欠费合计 / 记录数 / 涉及户数）。
        ///
        /// 设计：账单主键由客户端按页面当前可见行给出，服务端按主键回查台账行（金额、账龄、状态取库内值）；
        /// 列：缴费对象 / 业主 / 楼栋·单元·房号 / 欠费项目 / 欠费期间 / 应收 / 已收 / 欠费 / 最早欠期 /
        /// 账龄 / 状态 / 催缴状态。文件写 t_report_log（report_type = arrear_ledger）；本导出只读。
        /// </summary>
        public ReportLogDto ExportArrearsLedger(ArrearExportRequest request, string operatorName = null)
        {
            if (request == null || request.BillIds == null || request.BillIds.Count == 0)
            {
                throw ApiException.BadRequest("当前筛选下没有可导出的欠费台账记录");
            }
            if (request.BillIds.Count > ArrearLedgerExportMaxRows)
            {
                throw ApiException.ValidationFailed(
                    "一次最多导出 " + ArrearLedgerExportMaxRows + " 条欠费记录，请先按楼栋 / 账龄筛选后再导出");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                var query = new BillQueryRequest
                {
                    ArrearsOnly = true,
                    BillIds = request.BillIds.Distinct().ToList(),
                    PageIndex = 1,
                    PageSize = ArrearLedgerExportMaxRows
                };
                List<ArrearDto> rows = (_finance.QueryArrears(connection, query).Items ?? new List<ArrearDto>())
                    .Where(x => x.BillId > 0)
                    .OrderBy(x => x.AgingDays)
                    .ToList();
                if (rows.Count == 0)
                {
                    throw ApiException.NotFound("没有找到可导出的欠费记录（账单可能已结清或已从台账移出）");
                }

                string period = DateTime.Now.ToString("yyyyMMddHHmmss");
                bool excel = request.Format == ExportFormat.Excel;
                string fileName = "arrear_ledger_" + period + (excel ? ".xlsx" : ".pdf");
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);

                if (excel)
                {
                    ExportArrearLedgerExcel(filePath, rows);
                }
                else
                {
                    WriteArrearLedgerPdf(filePath, rows, operatorName);
                }

                var log = new ReportLogDto
                {
                    ReportType = "arrear_ledger",
                    Period = period,
                    Format = request.Format,
                    FilePath = filePath
                };
                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    log.Id = _finance.InsertReportLog(connection, transaction, log);
                    transaction.Commit();
                }

                _audit.Write("ARREAR_LEDGER_EXPORT", "report_log", log.Id.ToString(),
                    "欠费台账导出：" + rows.Count + " 条，" + request.Format + "，文件 " + fileName,
                    userName: operatorName, result: "成功");

                ReportLogDto saved = _finance.GetReportLog(connection, log.Id);
                return saved ?? log;
            }
        }

        private static string[] ArrearLedgerHeaders()
        {
            // CHG-v1.4.0-14：新增「催缴备注」列（渠道 + 备注解耦后可单独导出）
            return new[] { "缴费对象", "业主", "楼栋/单元/房号", "欠费项目", "欠费期间", "应收", "已收", "欠费",
                "最早欠期", "账龄", "状态", "催缴状态", "催缴备注" };
        }

        private static void ExportArrearLedgerExcel(string filePath, List<ArrearDto> rows)
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("欠费台账");
                sheet.Cell(1, 1).Value = "安怡物业 · 欠费台账";
                sheet.Cell(2, 1).Value = "导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                                         "，共 " + rows.Count + " 条，涉及 " + ArrearHouseholdCount(rows) + " 户";

                string[] headers = ArrearLedgerHeaders();
                int start = 4;
                for (int c = 1; c <= headers.Length; c++)
                {
                    sheet.Cell(start, c).Value = headers[c - 1];
                    sheet.Cell(start, c).Style.Font.Bold = true;
                }

                int row = start + 1;
                foreach (ArrearDto item in rows)
                {
                    sheet.Cell(row, 1).Value = ArrearLedgerObjectText(item);
                    sheet.Cell(row, 2).Value = Text(item.OwnerName);
                    sheet.Cell(row, 3).Value = Text(item.BuildingPath);
                    sheet.Cell(row, 4).Value = Text(item.ChargeItemName);
                    sheet.Cell(row, 5).Value = ArrearPeriodText(item);
                    sheet.Cell(row, 6).Value = item.Amount;
                    sheet.Cell(row, 7).Value = item.PaidAmount;
                    sheet.Cell(row, 8).Value = item.ArrearAmount;
                    sheet.Cell(row, 9).Value = item.DueAt.ToString("yyyy-MM-dd");
                    sheet.Cell(row, 10).Value = item.AgingDays + " 天";
                    sheet.Cell(row, 11).Value = ArrearStatusText(item);
                    sheet.Cell(row, 12).Value = Text(item.RemindChannel);
                    // CHG-v1.4.0-14：催缴备注列（渠道与备注解耦，备注单独成列可筛查）
                    sheet.Cell(row, 13).Value = string.IsNullOrWhiteSpace(item.RemindNote)
                        ? "—"
                        : item.RemindNote.Trim();
                    row++;
                }

                // 合计行（CHG-v1.4.0-04）：与页面顶部卡片同口径
                int sumRow = row + 1;
                sheet.Cell(sumRow, 1).Value = "合计";
                sheet.Cell(sumRow, 2).Value = "记录 " + rows.Count + " 条";
                sheet.Cell(sumRow, 3).Value = "涉及 " + ArrearHouseholdCount(rows) + " 户";
                sheet.Cell(sumRow, 4).Value = "应收 " + rows.Sum(x => x.Amount).ToString("0.00") + " 元";
                sheet.Cell(sumRow, 5).Value = "已收 " + rows.Sum(x => x.PaidAmount).ToString("0.00") + " 元";
                sheet.Cell(sumRow, 6).Value = "欠费 " + rows.Sum(x => x.ArrearAmount).ToString("0.00") + " 元";
                for (int c = 1; c <= 6; c++)
                {
                    sheet.Cell(sumRow, c).Style.Font.Bold = true;
                    sheet.Cell(sumRow, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                }

                for (int c = 1; c <= headers.Length; c++)
                {
                    sheet.Column(c).Width = c == 4 || c == 5 ? 22 : (c == 13 ? 30 : 16);
                }
                workbook.SaveAs(filePath);
            }
        }

        private static void WriteArrearLedgerPdf(string filePath, List<ArrearDto> rows, string operatorName)
        {
            PdfFontSupport.Ensure();

            var titleFont = new XFont("SimHei", 15, XFontStyleEx.Bold);
            var headerFont = new XFont("SimHei", 8, XFontStyleEx.Bold);
            var bodyFont = new XFont("SimHei", 7, XFontStyleEx.Regular);

            string[] headers = ArrearLedgerHeaders();
            // CHG-v1.4.0-15：13 列改横向 A4（842×595），列位与宽度按实测重排，右侧列不再出页
            double[] xs = { 30, 88, 132, 220, 294, 372, 408, 444, 484, 532, 568, 606, 654 };
            double[] widths = { 56, 42, 86, 72, 76, 34, 34, 38, 46, 34, 36, 46, 158 };

            using (var document = new PdfDocument())
            using (var canvas = new PdfTableCanvas(document, titleFont, headerFont, bodyFont, landscape: true))
            {
                canvas.Title("安怡物业 · 欠费台账");
                canvas.Line("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "　记录：" + rows.Count +
                            " 条　涉及：" + ArrearHouseholdCount(rows) + " 户" +
                            (string.IsNullOrWhiteSpace(operatorName) ? string.Empty : "　操作人：" + operatorName));
                canvas.Gap(2);
                canvas.Head(headers, xs);

                foreach (ArrearDto item in rows)
                {
                    canvas.EnsureRow(13, headers, xs);
                    string[] cells =
                    {
                        ArrearLedgerObjectText(item),
                        Text(item.OwnerName),
                        Text(item.BuildingPath),
                        Text(item.ChargeItemName),
                        ArrearPeriodText(item),
                        item.Amount.ToString("0.00"),
                        item.PaidAmount.ToString("0.00"),
                        item.ArrearAmount.ToString("0.00"),
                        item.DueAt.ToString("yyyy-MM-dd"),
                        item.AgingDays + " 天",
                        ArrearStatusText(item),
                        Text(item.RemindChannel),
                        // CHG-v1.4.0-14：催缴备注（最近一次催缴记录的备注），与渠道解耦、可单独归档
                        string.IsNullOrWhiteSpace(item.RemindNote) ? "—" : item.RemindNote.Trim()
                    };
                    for (int i = 0; i < cells.Length; i++)
                    {
                        canvas.Cell(FitToWidth(canvas.Gfx, cells[i], bodyFont, widths[i]), xs[i]);
                    }
                    canvas.NextRow(12);
                }

                canvas.Gap(6);
                canvas.Line("合计：应收 " + rows.Sum(x => x.Amount).ToString("0.00") +
                            " 元　　已收 " + rows.Sum(x => x.PaidAmount).ToString("0.00") +
                            " 元　　欠费 " + rows.Sum(x => x.ArrearAmount).ToString("0.00") +
                            " 元　　记录 " + rows.Count + " 条　　涉及 " + ArrearHouseholdCount(rows) + " 户");
                document.Save(filePath);
            }
        }

        /// <summary>欠费台账「缴费对象」文本（CHG-v1.2.0-27 口径：房产 / 车位 / 业主直缴 / 自定义对象）。</summary>
        private static string ArrearLedgerObjectText(ArrearDto item)
        {
            if (item == null) { return "—"; }
            if (string.Equals(item.ObjectKind, "owner", StringComparison.OrdinalIgnoreCase)) { return "业主直缴"; }
            string text = (item.PropertyNo ?? string.Empty).Trim();
            return text.Length == 0 ? "—" : text;
        }

        /// <summary>空值统一显示「—」（导出模板通用）。</summary>
        private static string Text(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
        }

        /// <summary>日期文本（导出模板通用）。</summary>
        private static string Text(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "—";
        }

        /// <summary>欠费期间（同年账期压缩显示；与页面「欠费期间」列同口径）。</summary>
        private static string ArrearPeriodText(ArrearDto item)
        {
            string start = (item == null ? null : item.CycleStart ?? string.Empty).Trim();
            string end = (item == null ? null : item.CycleEnd ?? string.Empty).Trim();
            if (start.Length == 0 && end.Length == 0) { return "—"; }
            if (start.Length == 0) { return end; }
            if (end.Length == 0) { return start; }
            if (start.Length >= 10 && end.Length >= 10 && start.Substring(0, 4) == end.Substring(0, 4))
            {
                return start + "~" + end.Substring(5);
            }
            return start + "~" + end;
        }

        /// <summary>欠费台账状态（逾期 / 部分缴 / 待缴；与页面「状态」列一致）。</summary>
        private static string ArrearStatusText(ArrearDto item)
        {
            if (item == null) { return "—"; }
            switch (item.Status)
            {
                case (int)BillStatus.Overdue: return "逾期";
                case (int)BillStatus.Partial: return "部分缴";
                case (int)BillStatus.Paid: return "已缴";
                default: return "待缴";
            }
        }

        /// <summary>
        /// 涉及户数（按缴费对象身份键 <see cref="ArrearDto.HouseholdKey"/> 去重，与台账页卡片同口径）。
        /// CHG-v1.4.1-07：原实现把「楼栋-房号」文本拼接又抄了一遍（漏单元、业主直缴会误并），
        /// 与前端两处分叉 —— 现统一用服务端下发的身份键（身份键口径见 <c>ArrearHousehold</c>）。
        /// </summary>
        private static int ArrearHouseholdCount(IEnumerable<ArrearDto> rows)
        {
            return (rows ?? Enumerable.Empty<ArrearDto>())
                .Select(x => !string.IsNullOrWhiteSpace(x.HouseholdKey)
                    ? x.HouseholdKey.Trim()
                    : (string.IsNullOrEmpty(x.BuildingNo)
                        ? (x.PropertyNo ?? string.Empty)
                        : x.BuildingNo + "-" + (x.PropertyNo ?? string.Empty)))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        // ---------- 通用 PDF 表格画布（CHG-v1.4.0-05：分页 + 末页合计） ----------

        /// <summary>
        /// 逐行输出的 PDF 表格画布：页满自动换页并重画表头（原导出多为「单页画满即 break」的静默截断）。
        /// XGraphics 与页绑定，翻页必须整体替换，故用对象持有。
        /// </summary>
        private sealed class PdfTableCanvas : IDisposable
        {
            private readonly PdfDocument _document;
            private readonly XFont _titleFont;
            private readonly XFont _headFont;
            private readonly XFont _bodyFont;
            private PdfPage _page;

            public double Y { get; private set; }
            public XGraphics Gfx { get; private set; }

            private readonly bool _landscape;

            public PdfTableCanvas(PdfDocument document, XFont titleFont, XFont headFont, XFont bodyFont,
                bool landscape = false)
            {
                _document = document;
                _titleFont = titleFont;
                _headFont = headFont;
                _bodyFont = bodyFont;
                _landscape = landscape;
                AddPage();
            }

            private void AddPage()
            {
                _page = _document.AddPage();
                _page.Size = PdfSharp.PageSize.A4;
                // CHG-v1.4.0-15：宽表（欠费台账 13 列）用横向 A4，避免右侧列被裁到页面之外
                if (_landscape)
                {
                    // 横向 A4（842×595 pt）—— PDFsharp 版本间枚举命名不一致，直接设尺寸最稳
                    _page.Width = XUnit.FromPoint(842);
                    _page.Height = XUnit.FromPoint(595);
                }
                Gfx = XGraphics.FromPdfPage(_page);
                Y = 30;
            }

            public void Title(string text)
            {
                Gfx.DrawString(text ?? string.Empty, _titleFont, XBrushes.Black, 30, Y);
                Y += 20;
            }

            public void Line(string text)
            {
                Gfx.DrawString(text ?? string.Empty, _bodyFont, XBrushes.Black, 30, Y);
                Y += 16;
            }

            public void Section(string title, XFont headFont, XFont bodyFont)
            {
                Gfx.DrawString(title ?? string.Empty, headFont, XBrushes.Black, 30, Y);
                Y += 16;
            }

            public void Head(string[] heads, double[] xs)
            {
                DrawRow(_headFont, heads, xs);
                Y += 13;
            }

            public void Cell(string text, double x)
            {
                Gfx.DrawString(text ?? string.Empty, _bodyFont, XBrushes.Black, x, Y);
            }

            public void NextRow(double dy)
            {
                Y += dy;
            }

            public void Gap(double dy)
            {
                Y += dy;
            }

            /// <summary>剩余空间不足时翻页，并在新页重画表头。</summary>
            public void EnsureRow(double need, string[] heads, double[] xs)
            {
                if (Y <= _page.Height.Point - need) { return; }
                Gfx.Dispose();
                AddPage();
                DrawRow(_headFont, heads, xs);
                Y += 13;
            }

            private void DrawRow(XFont font, string[] cells, double[] xs)
            {
                if (cells == null || xs == null) { return; }
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

        // ---------- 支出登记明细导出（CHG-v1.2.0-25） ----------

        /// <summary>导出状态文案（与页面「状态」列一致）。</summary>
        private static string ExpenseStatusText(ExpenseDto item)
        {
            return item.Status == 0 ? "已支付" : "已删除";
        }

        /// <summary>筛选条件摘要（写入 PDF 抬头，便于事后对账「这份文件是哪个口径导出的」）。</summary>
        private static string ExpenseFilterText(ExpenseExportRequest request)
        {
            var parts = new List<string>();
            parts.Add(string.IsNullOrWhiteSpace(request.Keyword) ? "关键字：不限" : "关键字：" + request.Keyword.Trim());
            parts.Add(request.CategoryId.HasValue && request.CategoryId.Value > 0
                ? "类别：指定分类"
                : "类别：全部");
            parts.Add(request.StatusFilter == 1 ? "状态：仅未删除"
                : (request.StatusFilter == 2 ? "状态：仅已删除" : "状态：全部"));
            return string.Join(" ｜ ", parts);
        }

        private static void ExportExpensesExcel(string filePath, List<ExpenseDto> items)
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("支出登记明细");
                sheet.Cell(1, 1).Value = "安怡物业 · 支出登记明细";
                sheet.Cell(2, 1).Value = "导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "，共 " + items.Count + " 笔";

                string[] headers = { "支出编号", "日期", "类别", "摘要", "金额", "收款方", "状态" };
                int start = 4;
                for (int c = 1; c <= headers.Length; c++)
                {
                    sheet.Cell(start, c).Value = headers[c - 1];
                    sheet.Cell(start, c).Style.Font.Bold = true;
                }

                int row = start + 1;
                foreach (ExpenseDto item in items)
                {
                    sheet.Cell(row, 1).Value = "ZC-" + item.Id.ToString("D4");
                    sheet.Cell(row, 2).Value = item.ExpenseDate.ToString("yyyy-MM-dd");
                    sheet.Cell(row, 3).Value = item.CategoryName ?? string.Empty;
                    sheet.Cell(row, 4).Value = item.Note ?? string.Empty;
                    sheet.Cell(row, 5).Value = -item.Amount;
                    sheet.Cell(row, 6).Value = item.Payee ?? string.Empty;
                    sheet.Cell(row, 7).Value = ExpenseStatusText(item);
                    row++;
                }

                if (items.Count > 0)
                {
                    decimal active = items.Where(x => x.Status == 0).Sum(x => x.Amount);
                    sheet.Cell(row, 1).Value = "合计";
                    sheet.Cell(row, 2).Value = "共 " + items.Count + " 笔";
                    sheet.Cell(row, 4).Value = "未删除合计 " + active.ToString("0.00") + " 元（不含已删除）";
                    for (int c = 1; c <= headers.Length; c++)
                    {
                        sheet.Cell(row, c).Style.Font.Bold = true;
                        sheet.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    }
                }
                for (int c = 1; c <= headers.Length; c++) { sheet.Column(c).Width = 16; }
                workbook.SaveAs(filePath);
            }
        }

        /// <summary>
        /// 支出登记明细 PDF（A4 纵向，长名单自动翻页并重画表头）。
        /// 列宽按 A4 可打印宽度（左右各 40pt 边距）排布，单元格先按实际量宽裁剪 ——
        /// 避免出现负责人多次反馈的「单元被遮挡 / 文字被裁掉」。
        /// </summary>
        private static void ExportExpensesPdf(string filePath, List<ExpenseDto> items, ExpenseExportRequest request)
        {
            PdfFontSupport.Ensure();

            var titleFont = new XFont("SimHei", 15, XFontStyleEx.Bold);
            var headerFont = new XFont("SimHei", 9, XFontStyleEx.Bold);
            var bodyFont = new XFont("SimHei", 8, XFontStyleEx.Regular);
            var smallFont = new XFont("SimHei", 7.5, XFontStyleEx.Regular);

            // 列宽按 A4 可打印宽度（595.28 - 左右各 40pt = 515pt）分配：
            // 摘要 146pt ≈ 18 个中文字、收款方 100pt ≈ 12 个中文字（覆盖现场常见内容不裁切）；
            // 最右边界 550pt 仍在右边距（555pt）之内 —— 单元之间不重叠、不越界。
            string[] headers = { "支出编号", "日期", "类别", "摘要", "金额", "收款方", "状态" };
            double[] xs = { 40, 92, 146, 206, 352, 414, 514 };
            double[] widths = { 52, 54, 60, 146, 62, 100, 36 };
            const double TableRight = 550;

            decimal activeTotal = items.Where(x => x.Status == 0).Sum(x => x.Amount);

            using (var document = new PdfDocument())
            {
                PdfPage page = null;
                XGraphics gfx = null;
                double y = 0;

                try
                {
                    foreach (ExpenseDto item in items)
                    {
                        if (gfx == null || y > page.Height.Point - 56)
                        {
                            if (gfx != null)
                            {
                                gfx.Dispose();
                                gfx = null;
                            }
                            page = document.AddPage();
                            page.Size = PdfSharp.PageSize.A4;
                            gfx = XGraphics.FromPdfPage(page);
                            y = 40;

                            if (document.PageCount == 1)
                            {
                                gfx.DrawString("安怡物业 · 支出登记明细", titleFont, XBrushes.Black, 40, y);
                                y += 18;
                                gfx.DrawString("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                                               "，共 " + items.Count + " 笔，未删除合计 " + activeTotal.ToString("0.00") + " 元",
                                    smallFont, XBrushes.Black, 40, y);
                                y += 13;
                                gfx.DrawString("筛选：" + ExpenseFilterText(request), smallFont, XBrushes.Black, 40, y);
                                y += 18;
                            }
                            else
                            {
                                gfx.DrawString("安怡物业 · 支出登记明细（续）", headerFont, XBrushes.Black, 40, y);
                                y += 16;
                            }

                            // 表头 + 表头分隔线（每页重画）
                            for (int i = 0; i < headers.Length; i++)
                            {
                                gfx.DrawString(headers[i], headerFont, XBrushes.Black, xs[i], y);
                            }
                            y += 4;
                            gfx.DrawLine(XPens.Gray, xs[0], y, TableRight, y);
                            y += 11;
                        }

                        string[] cells =
                        {
                            "ZC-" + item.Id.ToString("D4"),
                            item.ExpenseDate.ToString("yyyy-MM-dd"),
                            item.CategoryName ?? string.Empty,
                            item.Note ?? string.Empty,
                            // 全角「￥」：SimHei 含该字形；半角 ¥(U+00A5) 在 PDF 里会缺字（渲染成方框）
                            "-￥" + item.Amount.ToString("N2"),
                            item.Payee ?? string.Empty,
                            ExpenseStatusText(item)
                        };
                        for (int i = 0; i < cells.Length; i++)
                        {
                            gfx.DrawString(FitToWidth(gfx, cells[i], bodyFont, widths[i]),
                                bodyFont, XBrushes.Black, xs[i], y);
                        }
                        y += 14;
                    }

                    if (items.Count == 0)
                    {
                        page = document.AddPage();
                        page.Size = PdfSharp.PageSize.A4;
                        gfx = XGraphics.FromPdfPage(page);
                        y = 40;
                        gfx.DrawString("安怡物业 · 支出登记明细", titleFont, XBrushes.Black, 40, y);
                        y += 18;
                        gfx.DrawString("筛选：" + ExpenseFilterText(request), smallFont, XBrushes.Black, 40, y);
                        y += 20;
                        gfx.DrawString("当前筛选条件下没有支出记录。", bodyFont, XBrushes.Black, 40, y);
                    }
                    else
                    {
                        // 合计行（末尾）
                        if (y > page.Height.Point - 50)
                        {
                            gfx.Dispose();
                            gfx = null;
                            page = document.AddPage();
                            page.Size = PdfSharp.PageSize.A4;
                            gfx = XGraphics.FromPdfPage(page);
                            y = 40;
                        }
                        y += 4;
                        gfx.DrawLine(XPens.Gray, xs[0], y, TableRight, y);
                        y += 14;
                        gfx.DrawString("合计：" + items.Count + " 笔 ｜ 未删除 " +
                                       items.Count(x => x.Status == 0) + " 笔，合计 " + activeTotal.ToString("0.00") +
                                       " 元（不含已删除）", bodyFont, XBrushes.Black, xs[0], y);
                    }

                    if (gfx != null)
                    {
                        gfx.Dispose();
                        gfx = null;
                    }

                    // 页脚页码（全部页统一）
                    int totalPages = document.PageCount;
                    for (int index = 0; index < totalPages; index++)
                    {
                        PdfPage p = document.Pages[index];
                        using (XGraphics foot = XGraphics.FromPdfPage(p, XGraphicsPdfPageOptions.Append))
                        {
                            foot.DrawString("第 " + (index + 1) + " / " + totalPages + " 页",
                                smallFont, XBrushes.Gray, 40, p.Height.Point - 26);
                        }
                    }
                }
                finally
                {
                    if (gfx != null) { gfx.Dispose(); }
                }

                document.Save(filePath);
            }
        }

    }
}
