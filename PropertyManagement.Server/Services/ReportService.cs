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
