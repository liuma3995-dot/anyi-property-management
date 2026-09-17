using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using ClosedXML.Excel;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
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
        private readonly AuditService _audit;

        public ReportService()
            : this(new SqliteConnectionFactory(), new SqlFinanceRepository(), new AuditService())
        {
        }

        public ReportService(IDbConnectionFactory connectionFactory, IFinanceRepository finance, AuditService audit)
        {
            _connectionFactory = connectionFactory;
            _finance = finance;
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
        /// 导出「收据打印模板」（CHG-v1.1.0-14）：
        /// 收据号已下线，模板以「收款流水号 + 逐项收款明细」为口径；
        /// 统一收款时把同批每张账单（缴费对象/收费项目/账单期间/账单号/金额）逐行写清，便于打印核对。
        /// </summary>
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

    }
}
