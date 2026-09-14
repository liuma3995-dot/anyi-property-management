using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using Dapper;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PropertyManagement.Contract.Dispute;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 结案报告导出（PG-DIS-03 新增，F2）：
    /// 仅已结案案件可导出；一次读取案件快照（案件 + 当事人 + 处理记录 + 状态时间线）
    /// 生成 PDF（PDFsharp，打印签字用）/ Excel（ClosedXML，归档用），文件落 DbConfig.ExportDirectory，
    /// 并在 t_report_log 留痕（report_type = dispute_close，period = 案件编号）。
    /// 报告章节（负责人已确认）：报告头 / 一、案件基本信息 / 二、当事人 / 三、处理进度 /
    /// 四、调解方案与进度（六列）/ 五、结案报告 / 六、签字栏（必含）/ 七、协议扫描件清单（必含）。
    /// </summary>
    public class DisputeCloseReportService
    {
        private const string ReportType = "dispute_close";

        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IDisputeRepository _dispute;
        private readonly IDisputeAttachmentRepository _attachments;
        private readonly IFinanceRepository _finance;
        private readonly AuditService _audit;

        public DisputeCloseReportService()
            : this(new SqliteConnectionFactory(), new SqlDisputeRepository(), new SqlDisputeAttachmentRepository(),
                   new SqlFinanceRepository(), new AuditService())
        {
        }

        public DisputeCloseReportService(
            IDbConnectionFactory connectionFactory,
            IDisputeRepository dispute,
            IDisputeAttachmentRepository attachments,
            IFinanceRepository finance,
            AuditService audit)
        {
            _connectionFactory = connectionFactory;
            _dispute = dispute;
            _attachments = attachments;
            _finance = finance;
            _audit = audit;
        }

        /// <summary>导出结案报告：未结案一律拒绝（BR-DIS-01 状态流），成功返回导出留痕记录。</summary>
        public ReportLogDto Export(int caseId, ExportFormat format, string operatorName)
        {
            if (format != ExportFormat.Pdf && format != ExportFormat.Excel)
            {
                throw ApiException.ValidationFailed("结案报告仅支持 PDF / Excel 两种格式");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                DisputeCaseDto kase = _dispute.GetCase(connection, caseId);
                if (kase == null)
                {
                    throw ApiException.NotFound("纠纷案件不存在");
                }
                if (kase.Status != DisputeCaseStatus.Closed)
                {
                    throw ApiException.Conflict("仅已结案案件可导出结案报告（当前状态：" +
                        (string.IsNullOrEmpty(kase.StatusText) ? kase.Status.ToString() : kase.StatusText) + "）");
                }

                var detail = new DisputeCaseDetailDto
                {
                    Case = kase,
                    Parties = _dispute.ListParties(connection, caseId),
                    Records = _dispute.ListRecords(connection, caseId),
                    StatusLogs = _dispute.ListStatusLogs(connection, caseId),
                    Attachments = _attachments.List(connection, caseId)
                };

                ReportContext context = BuildContext(connection, detail, operatorName);

                string outputDirectory = ResolveOutputDirectory();
                string extension = format == ExportFormat.Excel ? ".xlsx" : ".pdf";
                string fileName = "dispute_close_" + SafeToken(kase.CaseNo, kase.Id) + "_" +
                    DateTime.Now.ToString("yyyyMMddHHmmss") + extension;
                string filePath = Path.Combine(outputDirectory, fileName);

                if (format == ExportFormat.Excel)
                {
                    WriteExcel(filePath, context);
                }
                else
                {
                    WritePdf(filePath, context);
                }

                var log = new ReportLogDto
                {
                    ReportType = ReportType,
                    Period = string.IsNullOrWhiteSpace(kase.CaseNo) ? "JF-" + kase.Id : kase.CaseNo,
                    Format = format,
                    FilePath = filePath
                };

                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    log.Id = _finance.InsertReportLog(connection, transaction, log);
                    transaction.Commit();
                }

                _audit.Write("DISPUTE_REPORT_EXPORT", "dispute_case", caseId.ToString(),
                    "结案报告导出：" + log.Period + "，" + (format == ExportFormat.Excel ? "Excel" : "PDF") +
                    "，文件 " + fileName);

                return _finance.GetReportLog(connection, log.Id) ?? log;
            }
        }

        // ============================================================
        // 报告数据快照（导出时刻），PDF 与 Excel 共用同一份数据
        // ============================================================

        /// <summary>
        /// 结案报告默认落盘目录：当前登录用户的「文档」目录（如 C:\Users\Administrator\Documents），
        /// 便于用户直接归档/打印；文档目录不可用时回退到程序导出目录（%ProgramData%\PropertyManagement\exports）。
        /// </summary>
        private static string ResolveOutputDirectory()
        {
            try
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrWhiteSpace(documents))
                {
                    Directory.CreateDirectory(documents);
                    return documents;
                }
            }
            catch
            {
                // 文档目录不可写/不可用 → 回退程序导出目录
            }

            Directory.CreateDirectory(DbConfig.ExportDirectory);
            return DbConfig.ExportDirectory;
        }

        private ReportContext BuildContext(IDbConnection connection, DisputeCaseDetailDto detail, string operatorName)
        {
            DisputeCaseDto kase = detail.Case;
            var context = new ReportContext
            {
                Case = kase,
                ExportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ExportedBy = string.IsNullOrWhiteSpace(operatorName) ? "admin" : operatorName.Trim(),
                PropertyPath = ResolvePropertyPath(connection, kase.PropertyId)
            };

            foreach (DisputePartyDto party in detail.Parties ?? new List<DisputePartyDto>())
            {
                string role = string.Equals(party.PartyTypeText, "乙方") ? "乙方" : "甲方";
                var parts = new List<string>();
                string path = party.OwnerId.HasValue ? ResolveOwnerPropertyPath(connection, party.OwnerId.Value) : string.Empty;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    parts.Add(path);
                }
                parts.Add(string.IsNullOrWhiteSpace(party.Name) ? "—" : party.Name.Trim());
                parts.Add(string.IsNullOrWhiteSpace(party.Phone) ? "—" : party.Phone.Trim());
                context.Parties.Add(new PartyLine
                {
                    Role = role,
                    Text = string.Join("  ", parts),
                    External = !party.OwnerId.HasValue
                });
            }

            context.Timeline = BuildTimeline(detail.StatusLogs, detail.Records, kase);

            int index = 1;
            foreach (DisputeRecordDto record in (detail.Records ?? new List<DisputeRecordDto>())
                .OrderBy(r => r.RecordTime).ThenBy(r => r.Id))
            {
                context.Records.Add(new RecordLine
                {
                    Index = index++,
                    Date = record.RecordTime.ToString("yyyy-MM-dd"),
                    Method = Dash(record.Method),
                    SupplementNote = record.IsSupplement
                        ? "补录：" + (string.IsNullOrWhiteSpace(record.SupplementReason) ? "未填写原因" : record.SupplementReason.Trim())
                        : string.Empty,
                    Plan = Dash(FirstNonEmpty(record.PlanSummary, record.Content)),
                    Opinion = Dash(record.PartyOpinion),
                    Result = Dash(record.Result)
                });
            }

            context.Attachments = LoadAttachments(connection, kase.Id);
            return context;
        }

        /// <summary>
        /// 协议扫描件清单（章节七，F1）：来源 t_dispute_attachment（导出时刻快照），空清单显示「暂无扫描件」。
        /// </summary>
        private List<AttachmentLine> LoadAttachments(IDbConnection connection, int caseId)
        {
            var lines = new List<AttachmentLine>();
            int index = 1;
            foreach (DisputeAttachmentDto attachment in _attachments.List(connection, caseId))
            {
                lines.Add(new AttachmentLine
                {
                    Index = index++,
                    FileName = Dash(attachment.FileName),
                    Size = string.IsNullOrWhiteSpace(attachment.SizeText)
                        ? DisputeAttachmentService.FormatSize(attachment.SizeBytes)
                        : attachment.SizeText,
                    Uploader = Dash(attachment.UploadedBy),
                    UploadedAt = attachment.UploadedAt.ToString("yyyy-MM-dd HH:mm")
                });
            }
            return lines;
        }

        /// <summary>处理进度：状态节点（登记受理/处理中/已结案）+ 每条调解记录节点（用记录日期）按时间合并。</summary>
        private static List<TimelineLine> BuildTimeline(
            IList<DisputeStatusLogDto> logs,
            IList<DisputeRecordDto> records,
            DisputeCaseDto kase)
        {
            var lines = new List<TimelineLine>();
            foreach (DisputeStatusLogDto log in logs ?? new List<DisputeStatusLogDto>())
            {
                lines.Add(new TimelineLine { Time = log.ChangedAt, Title = StatusTitle(log.NewStatus) });
            }

            int n = 1;
            foreach (DisputeRecordDto record in (records ?? new List<DisputeRecordDto>())
                .OrderBy(r => r.RecordTime).ThenBy(r => r.Id))
            {
                string title = "第" + n + "次调解" +
                    (string.IsNullOrWhiteSpace(record.Method) ? string.Empty : " · " + record.Method.Trim()) +
                    (record.IsSupplement ? "（补录）" : string.Empty);
                lines.Add(new TimelineLine { Time = record.RecordTime, Title = title });
                n++;
            }

            if (lines.Count == 0 && kase != null)
            {
                lines.Add(new TimelineLine { Time = kase.OccurTime, Title = StatusTitle(kase.Status) });
            }

            return lines.OrderBy(l => l.Time).ToList();
        }

        private static string StatusTitle(DisputeCaseStatus status)
        {
            switch (status)
            {
                case DisputeCaseStatus.Handling: return "处理中";
                case DisputeCaseStatus.Closed: return "已结案";
                default: return "登记受理";
            }
        }

        /// <summary>关联房产完整房号：楼栋（原值）+ 单元（原值）+ 房号；无单元时不补占位（取不到返回空串）。</summary>
        private static string ResolvePropertyPath(IDbConnection connection, int? propertyId)
        {
            if (!propertyId.HasValue || propertyId.Value <= 0)
            {
                return string.Empty;
            }

            try
            {
                return connection.ExecuteScalar<string>(
                    "SELECT COALESCE(b.building_no,'') || COALESCE(u.unit_no,'') || COALESCE(p.room_no,'') " +
                    "FROM t_property p " +
                    "LEFT JOIN t_building b ON b.id = p.building_id " +
                    "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                    "WHERE p.id = @propertyId", new { propertyId = propertyId.Value }) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 当事人（引用业主）的完整房号：优先取「有效/即将到期」关联，其次最近一条未删除关联（避免一户多房导致行重复）。
        /// 注意表名为 t_owner_property_rel（t_owner_property_relation 不存在，曾导致导出抛 SQLite 异常被兜底成 503）。
        /// 房号仅作展示增强，取不到时返回空串，不得因此让整份报告导出失败。
        /// </summary>
        private static string ResolveOwnerPropertyPath(IDbConnection connection, int ownerId)
        {
            if (ownerId <= 0)
            {
                return string.Empty;
            }

            try
            {
                return connection.ExecuteScalar<string>(
                    "SELECT COALESCE((SELECT b2.building_no FROM t_building b2 WHERE b2.id = p.building_id), COALESCE(b.building_no,''), '') || " +
                    "COALESCE(u.unit_no,'') || COALESCE(p.room_no,'') " +
                    "FROM t_owner_property_rel r " +
                    "JOIN t_property p ON p.id = r.property_id " +
                    "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                    "LEFT JOIN t_building b ON b.id = u.building_id " +
                    "WHERE r.owner_id = @ownerId AND r.del_flag = 0 " +
                    "ORDER BY CASE WHEN r.rel_status IN (0,1) THEN 0 ELSE 1 END, r.id DESC LIMIT 1",
                    new { ownerId }) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        // ============================================================
        // Excel 导出（ClosedXML）
        // ============================================================

        private static void WriteExcel(string filePath, ReportContext context)
        {
            DisputeCaseDto kase = context.Case;
            using (var workbook = new XLWorkbook())
            {
                IXLWorksheet sheet = workbook.Worksheets.Add("结案报告");
                for (int c = 1; c <= 6; c++)
                {
                    sheet.Column(c).Width = 18;
                }
                sheet.Column(1).Width = 14;

                int row = 1;
                sheet.Range(row, 1, row, 6).Merge();
                IXLCell title = sheet.Cell(row, 1);
                title.Value = "民事纠纷调解结案报告";
                title.Style.Font.SetBold().Font.SetFontSize(16);
                title.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                title.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                sheet.Row(row).Height = 30;
                row++;

                sheet.Range(row, 1, row, 6).Merge();
                IXLCell subtitle = sheet.Cell(row, 1);
                subtitle.Value = "案件编号：" + Dash(kase.CaseNo) +
                    "　　导出时间：" + context.ExportedAt +
                    "　　导出人：" + context.ExportedBy;
                subtitle.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                subtitle.Style.Font.SetFontSize(10);
                subtitle.Style.Font.SetFontColor(XLColor.FromHtml("#5A6472"));
                row += 2;

                row = ExcelSection(sheet, row, "一、案件基本信息");
                row = ExcelKeyValue(sheet, row, "纠纷类型", Dash(kase.TypeName), "纠纷等级", Dash(kase.LevelText));
                row = ExcelKeyValue(sheet, row, "关联房产", Dash(context.PropertyPath), "登记日期", kase.OccurTime.ToString("yyyy-MM-dd"));
                row = ExcelKeyValue(sheet, row, "期望解决时间", Dash(kase.ExpectedAt.HasValue ? kase.ExpectedAt.Value.ToString("yyyy-MM-dd") : null),
                    "调解员", Dash(kase.MediatorName));
                row = ExcelKeyValue(sheet, row, "案件状态", Dash(kase.StatusText), "已受理", ContextDays(kase) + " 天");
                row = ExcelKeyValue(sheet, row, "结案类型", Dash(kase.CloseTypeText),
                    "结案时间", kase.ClosedAt.HasValue ? kase.ClosedAt.Value.ToString("yyyy-MM-dd HH:mm") : "—");
                row++;

                row = ExcelSection(sheet, row, "二、当事人");
                if (context.Parties.Count == 0)
                {
                    row = ExcelPlain(sheet, row, "暂无当事人信息", 6);
                }
                else
                {
                    foreach (PartyLine party in context.Parties)
                    {
                        string text = party.Role + "：" + party.Text + (party.External ? "（外部登记）" : string.Empty);
                        row = ExcelPlain(sheet, row, text, 6);
                    }
                }
                row++;

                row = ExcelSection(sheet, row, "三、处理进度");
                if (context.Timeline.Count == 0)
                {
                    row = ExcelPlain(sheet, row, "暂无进度记录", 6);
                }
                else
                {
                    foreach (TimelineLine line in context.Timeline)
                    {
                        row = ExcelPlain(sheet, row, line.Time.ToString("yyyy-MM-dd HH:mm") + "　　" + line.Title, 6);
                    }
                }
                row++;

                row = ExcelSection(sheet, row, "四、调解方案与进度");
                string[] headers = { "次", "日期", "方式", "方案摘要", "当事人意见", "结果" };
                for (int i = 0; i < headers.Length; i++)
                {
                    IXLCell cell = sheet.Cell(row, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.SetBold();
                    cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F4F7FB"));
                    cell.Style.Border.SetLeftBorder(XLBorderStyleValues.Thin);
                    cell.Style.Border.SetRightBorder(XLBorderStyleValues.Thin);
                    cell.Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
                    cell.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
                }
                row++;
                if (context.Records.Count == 0)
                {
                    sheet.Range(row, 1, row, 6).Merge();
                    sheet.Cell(row, 1).Value = "暂无调解记录";
                    sheet.Cell(row, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    row++;
                }
                else
                {
                    foreach (RecordLine record in context.Records)
                    {
                        string method = record.Method +
                            (string.IsNullOrEmpty(record.SupplementNote) ? string.Empty : "\n" + record.SupplementNote);
                        object[] values = { record.Index, record.Date, method, record.Plan, record.Opinion, record.Result };
                        for (int i = 0; i < values.Length; i++)
                        {
                            IXLCell cell = sheet.Cell(row, i + 1);
                            if (values[i] is int)
                            {
                                cell.Value = (int)values[i];
                            }
                            else
                            {
                                cell.Value = Convert.ToString(values[i]);
                            }
                            cell.Style.Alignment.SetWrapText(true);
                            cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                            cell.Style.Border.SetLeftBorder(XLBorderStyleValues.Thin);
                            cell.Style.Border.SetRightBorder(XLBorderStyleValues.Thin);
                            cell.Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
                            cell.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
                            if (i == 0 || i == 1 || i == 5)
                            {
                                cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                            }
                        }
                        row++;
                    }
                }
                row++;

                row = ExcelSection(sheet, row, "五、结案报告");
                sheet.Range(row, 1, row + 3, 6).Merge();
                IXLCell summary = sheet.Cell(row, 1);
                summary.Value = Dash(kase.CloseSummary);
                summary.Style.Alignment.SetWrapText(true);
                summary.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                sheet.Row(row).Height = 22;
                row += 6;

                row = ExcelSection(sheet, row, "六、签字栏");
                string[] signRoles = { "调解员", "甲方", "乙方", "物业经办" };
                for (int i = 0; i < signRoles.Length; i++)
                {
                    IXLCell cell = sheet.Cell(row, i + 1);
                    cell.Value = signRoles[i];
                    cell.Style.Font.SetBold();
                    cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    ExcelBorder(cell);
                }
                sheet.Cell(row, 5).Value = "签字日期";
                sheet.Cell(row, 5).Style.Font.SetBold();
                sheet.Cell(row, 6).Value = "　　　年　　月　　日";
                row++;
                sheet.Range(row, 1, row + 2, 4).Merge();
                sheet.Cell(row, 1).Value = "（签字区）";
                sheet.Cell(row, 1).Style.Font.SetFontColor(XLColor.FromHtml("#8A94A6"));
                sheet.Cell(row, 1).Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                sheet.Range(row, 5, row + 2, 6).Merge();
                for (int c = 1; c <= 6; c++)
                {
                    ExcelBorder(sheet.Cell(row, c));
                }
                sheet.Row(row).Height = 24;
                row += 4;

                row = ExcelSection(sheet, row, "七、协议扫描件清单");
                string[] attachHeaders = { "序号", "文件名", "大小", "上传人", "上传时间" };
                for (int i = 0; i < attachHeaders.Length; i++)
                {
                    IXLCell cell = sheet.Cell(row, i + 1);
                    cell.Value = attachHeaders[i];
                    cell.Style.Font.SetBold();
                    cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    ExcelBorder(cell);
                }
                sheet.Cell(row, 6).Value = string.Empty;
                ExcelBorder(sheet.Cell(row, 6));
                row++;
                if (context.Attachments.Count == 0)
                {
                    sheet.Range(row, 1, row, 6).Merge();
                    sheet.Cell(row, 1).Value = "暂无扫描件";
                    sheet.Cell(row, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    sheet.Cell(row, 1).Style.Font.SetFontColor(XLColor.FromHtml("#8A94A6"));
                    row++;
                }
                else
                {
                    foreach (AttachmentLine attachment in context.Attachments)
                    {
                        object[] values = { attachment.Index, attachment.FileName, attachment.Size, attachment.Uploader, attachment.UploadedAt };
                        for (int i = 0; i < values.Length; i++)
                        {
                            IXLCell cell = sheet.Cell(row, i + 1);
                            if (values[i] is int)
                            {
                                cell.Value = (int)values[i];
                            }
                            else
                            {
                                cell.Value = Convert.ToString(values[i]);
                            }
                            ExcelBorder(cell);
                        }
                        row++;
                    }
                }

                row++;
                sheet.Range(row, 1, row, 6).Merge();
                IXLCell footer = sheet.Cell(row, 1);
                footer.Value = "安怡物业管理系统 · 纠纷调解子系统 · 处理与结案";
                footer.Style.Font.SetFontSize(9);
                footer.Style.Font.SetFontColor(XLColor.FromHtml("#8A94A6"));
                footer.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                workbook.SaveAs(filePath);
            }
        }

        private static void ExcelBorder(IXLCell cell)
        {
            cell.Style.Border.SetLeftBorder(XLBorderStyleValues.Thin);
            cell.Style.Border.SetRightBorder(XLBorderStyleValues.Thin);
            cell.Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
            cell.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
        }

        private static int ExcelSection(IXLWorksheet sheet, int row, string text)
        {
            sheet.Range(row, 1, row, 6).Merge();
            IXLCell cell = sheet.Cell(row, 1);
            cell.Value = text;
            cell.Style.Font.SetBold().Font.SetFontSize(12);
            sheet.Row(row).Height = 20;
            return row + 1;
        }

        private static int ExcelKeyValue(IXLWorksheet sheet, int row, string label1, string value1, string label2, string value2)
        {
            sheet.Cell(row, 1).Value = label1;
            sheet.Cell(row, 1).Style.Font.SetFontColor(XLColor.FromHtml("#5A6472"));
            sheet.Range(row, 2, row, 3).Merge();
            sheet.Cell(row, 2).Value = value1;
            sheet.Cell(row, 4).Value = label2;
            sheet.Cell(row, 4).Style.Font.SetFontColor(XLColor.FromHtml("#5A6472"));
            sheet.Range(row, 5, row, 6).Merge();
            sheet.Cell(row, 5).Value = value2;
            return row + 1;
        }

        private static int ExcelPlain(IXLWorksheet sheet, int row, string text, int span)
        {
            sheet.Range(row, 1, row, span).Merge();
            IXLCell cell = sheet.Cell(row, 1);
            cell.Value = text;
            cell.Style.Alignment.SetWrapText(true);
            return row + 1;
        }

        // ============================================================
        // PDF 导出（PDFsharp + 中文字体）
        // ============================================================

        private static void WritePdf(string filePath, ReportContext context)
        {
            PdfFontSupport.Ensure();
            using (var writer = new PdfReportWriter())
            {
                writer.WriteReport(context);
                writer.Save(filePath);
            }
        }

        /// <summary>A4 结案报告排版器：自动分页 + 表格换行 + 页脚页码。</summary>
        private sealed class PdfReportWriter : IDisposable
        {
            private const double MarginLeft = 46;
            private const double MarginRight = 46;
            private const double MarginTop = 44;
            private const double MarginBottom = 56;
            private const double Pad = 3.5;

            private readonly PdfDocument _document = new PdfDocument();
            private readonly XFont _titleFont = new XFont("SimHei", 15, XFontStyleEx.Bold);
            private readonly XFont _subFont = new XFont("SimHei", 9, XFontStyleEx.Regular);
            private readonly XFont _sectionFont = new XFont("SimHei", 11, XFontStyleEx.Bold);
            private readonly XFont _bodyFont = new XFont("SimHei", 9.5, XFontStyleEx.Regular);
            private readonly XFont _boldFont = new XFont("SimHei", 9.5, XFontStyleEx.Bold);
            private readonly XFont _smallFont = new XFont("SimHei", 8.5, XFontStyleEx.Regular);
            private readonly XBrush _muted = new XSolidBrush(XColor.FromArgb(255, 90, 100, 114));
            private readonly XPen _hairPen = new XPen(XColor.FromArgb(255, 210, 216, 224), 0.6);
            private readonly XPen _gridPen = new XPen(XColor.FromArgb(255, 186, 194, 205), 0.6);
            private readonly XBrush _headFill = new XSolidBrush(XColor.FromArgb(255, 244, 247, 251));

            private XGraphics _gfx;
            private PdfPage _page;
            private double _y;

            public PdfReportWriter()
            {
                AddPage();
            }

            private double PageWidth { get { return _page.Width.Point; } }
            private double PageHeight { get { return _page.Height.Point; } }
            private double Right { get { return PageWidth - MarginRight; } }
            private double ContentWidth { get { return PageWidth - MarginLeft - MarginRight; } }

            private void AddPage()
            {
                if (_gfx != null)
                {
                    _gfx.Dispose();
                }
                _page = _document.AddPage();
                _page.Size = PdfSharp.PageSize.A4;
                _gfx = XGraphics.FromPdfPage(_page);
                _y = MarginTop;
            }

            private void Ensure(double height)
            {
                if (_y + height > PageHeight - MarginBottom)
                {
                    AddPage();
                }
            }

            private List<string> Wrap(string text, XFont font, double maxWidth)
            {
                var lines = new List<string>();
                if (text == null)
                {
                    lines.Add(string.Empty);
                    return lines;
                }

                var buffer = new StringBuilder();
                foreach (char ch in text)
                {
                    if (ch == '\n')
                    {
                        lines.Add(buffer.ToString());
                        buffer.Clear();
                        continue;
                    }
                    string candidate = buffer.ToString() + ch;
                    if (buffer.Length > 0 && _gfx.MeasureString(candidate, font).Width > maxWidth)
                    {
                        lines.Add(buffer.ToString());
                        buffer.Clear();
                    }
                    buffer.Append(ch);
                }
                lines.Add(buffer.ToString());
                return lines;
            }

            private void Text(string text, XFont font, XBrush brush, double x, double top)
            {
                _gfx.DrawString(text ?? string.Empty, font, brush, x, top + font.Size);
            }

            private double Paragraph(string text, XFont font, XBrush brush, double indent, double width, double gap)
            {
                double lineHeight = font.Size * gap;
                List<string> lines = Wrap(text, font, width);
                foreach (string line in lines)
                {
                    Ensure(lineHeight);
                    Text(line, font, brush, MarginLeft + indent, _y);
                    _y += lineHeight;
                }
                return lines.Count * lineHeight;
            }

            /// <summary>章节标题；keepWith 为紧随其后的区块最小高度，避免标题孤立在页尾。</summary>
            private void Section(string text, double keepWith = 26)
            {
                Ensure(24 + keepWith);
                _y += 8;
                Text(text, _sectionFont, XBrushes.Black, MarginLeft, _y);
                _y += 16;
                _gfx.DrawLine(_hairPen, MarginLeft, _y - 3, Right, _y - 3);
                _y += 4;
            }

            private double Row(string label, string value, double x, double labelWidth, double valueWidth)
            {
                double labelLines = Wrap(label, _bodyFont, labelWidth).Count;
                List<string> valueLines = Wrap(value, _bodyFont, valueWidth);
                double lineHeight = _bodyFont.Size * 1.5;
                double height = Math.Max(labelLines, valueLines.Count) * lineHeight;
                Ensure(height);
                Text(label, _bodyFont, _muted, x, _y);
                for (int i = 0; i < valueLines.Count; i++)
                {
                    Text(valueLines[i], _bodyFont, XBrushes.Black, x + labelWidth, _y + i * lineHeight);
                }
                return height;
            }

            private void KeyValuePairs(List<KeyValuePair<string, string>> pairs)
            {
                double columnGap = 14;
                double columnWidth = (ContentWidth - columnGap) / 2;
                double labelWidth = 62;
                for (int i = 0; i < pairs.Count; i += 2)
                {
                    double leftHeight = Row(pairs[i].Key, pairs[i].Value, MarginLeft, labelWidth, columnWidth - labelWidth);
                    double rightHeight = i + 1 < pairs.Count
                        ? Row(pairs[i + 1].Key, pairs[i + 1].Value,
                            MarginLeft + columnWidth + columnGap, labelWidth, columnWidth - labelWidth)
                        : 0;
                    _y += Math.Max(leftHeight, rightHeight) + 2;
                }
            }

            /// <summary>绘制一张带表头的网格表；单元格按列宽自动换行，跨页时重绘表头。</summary>
            private void Table(string[] headers, IList<string[]> rows, double[] weights, double minRowHeight)
            {
                double scale = ContentWidth / weights.Sum();
                double[] widths = weights.Select(w => w * scale).ToArray();
                const double gap = 1.42;
                double lineHeight = _smallFont.Size * gap;
                double headerHeight = 19;

                DrawHeader(headers, widths, headerHeight);

                if (rows.Count == 0)
                {
                    Ensure(minRowHeight);
                    DrawRow(new[] { "暂无数据" }, widths, minRowHeight, lineHeight);
                    return;
                }

                foreach (string[] row in rows)
                {
                    double needed = 0;
                    for (int c = 0; c < widths.Length; c++)
                    {
                        string cell = c < row.Length ? row[c] : string.Empty;
                        needed = Math.Max(needed, Wrap(cell ?? string.Empty, _smallFont, widths[c] - Pad * 2).Count);
                    }
                    double height = Math.Max(minRowHeight, needed * lineHeight + Pad * 2);
                    if (_y + height > PageHeight - MarginBottom)
                    {
                        AddPage();
                        DrawHeader(headers, widths, headerHeight);
                    }
                    DrawRow(row, widths, height, lineHeight);
                }
            }

            private void DrawHeader(string[] headers, double[] widths, double height)
            {
                Ensure(height);
                double x = MarginLeft;
                for (int c = 0; c < widths.Length; c++)
                {
                    _gfx.DrawRectangle(_headFill, x, _y, widths[c], height);
                    _gfx.DrawRectangle(_gridPen, x, _y, widths[c], height);
                    string text = c < headers.Length ? headers[c] : string.Empty;
                    double textWidth = _gfx.MeasureString(text, _boldFont).Width;
                    Text(text, _boldFont, XBrushes.Black, x + (widths[c] - textWidth) / 2, _y + (height - _boldFont.Size) / 2 - 2);
                    x += widths[c];
                }
                _y += height;
            }

            private void DrawRow(string[] row, double[] widths, double height, double lineHeight)
            {
                double x = MarginLeft;
                for (int c = 0; c < widths.Length; c++)
                {
                    string cell = c < row.Length ? (row[c] ?? string.Empty) : string.Empty;
                    _gfx.DrawRectangle(_gridPen, x, _y, widths[c], height);
                    List<string> lines = Wrap(cell, _smallFont, widths[c] - Pad * 2);
                    for (int i = 0; i < lines.Count; i++)
                    {
                        Text(lines[i], _smallFont, XBrushes.Black, x + Pad, _y + Pad + i * lineHeight - 1);
                    }
                    x += widths[c];
                }
                _y += height;
            }

            /// <summary>签字栏：4 个签署人栏位 + 签字日期行。</summary>
            private void SignatureBlock()
            {
                string[] roles = { "调解员", "甲方", "乙方", "物业经办" };
                double[] weights = { 1, 1, 1, 1 };
                double scale = ContentWidth / weights.Sum();
                double[] widths = weights.Select(w => w * scale).ToArray();
                const double headerHeight = 19;
                double blankHeight = 64;
                double dateHeight = 22;

                Ensure(headerHeight + blankHeight + dateHeight + 8);
                DrawHeader(roles, widths, headerHeight);
                DrawRow(new[] { "", "", "", "" }, widths, blankHeight, _smallFont.Size * 1.42);

                _gfx.DrawRectangle(_gridPen, MarginLeft, _y, ContentWidth, dateHeight);
                Text("签字日期：　　　　年　　　月　　　日", _smallFont, XBrushes.Black, MarginLeft + Pad, _y + 4);
                _y += dateHeight;
            }

            public void WriteReport(ReportContext context)
            {
                DisputeCaseDto kase = context.Case;

                Ensure(60);
                string title = "民事纠纷调解结案报告";
                double titleWidth = _gfx.MeasureString(title, _titleFont).Width;
                Text(title, _titleFont, XBrushes.Black, MarginLeft + (ContentWidth - titleWidth) / 2, _y);
                _y += 26;

                string subtitle = "案件编号：" + Dash(kase.CaseNo) +
                    "　　导出时间：" + context.ExportedAt +
                    "　　导出人：" + context.ExportedBy;
                double subtitleWidth = _gfx.MeasureString(subtitle, _subFont).Width;
                Text(subtitle, _subFont, _muted, MarginLeft + Math.Max(0, (ContentWidth - subtitleWidth) / 2), _y);
                _y += 16;
                _gfx.DrawLine(_hairPen, MarginLeft, _y, Right, _y);
                _y += 6;

                Section("一、案件基本信息");
                KeyValuePairs(new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("纠纷类型", Dash(kase.TypeName)),
                    new KeyValuePair<string, string>("纠纷等级", Dash(kase.LevelText)),
                    new KeyValuePair<string, string>("关联房产", Dash(context.PropertyPath)),
                    new KeyValuePair<string, string>("登记日期", kase.OccurTime.ToString("yyyy-MM-dd")),
                    new KeyValuePair<string, string>("期望解决时间", Dash(kase.ExpectedAt.HasValue ? kase.ExpectedAt.Value.ToString("yyyy-MM-dd") : null)),
                    new KeyValuePair<string, string>("调解员", Dash(kase.MediatorName)),
                    new KeyValuePair<string, string>("案件状态", Dash(kase.StatusText)),
                    new KeyValuePair<string, string>("已受理", ContextDays(kase) + " 天"),
                    new KeyValuePair<string, string>("结案类型", Dash(kase.CloseTypeText)),
                    new KeyValuePair<string, string>("结案时间", kase.ClosedAt.HasValue ? kase.ClosedAt.Value.ToString("yyyy-MM-dd HH:mm") : "—")
                });

                Section("二、当事人");
                if (context.Parties.Count == 0)
                {
                    Paragraph("暂无当事人信息", _bodyFont, _muted, 0, ContentWidth, 1.6);
                }
                else
                {
                    foreach (PartyLine party in context.Parties)
                    {
                        string text = party.Role + "：" + party.Text + (party.External ? "（外部登记）" : string.Empty);
                        Paragraph(text, _bodyFont, XBrushes.Black, 0, ContentWidth, 1.6);
                    }
                }

                Section("三、处理进度");
                if (context.Timeline.Count == 0)
                {
                    Paragraph("暂无进度记录", _bodyFont, _muted, 0, ContentWidth, 1.6);
                }
                else
                {
                    foreach (TimelineLine line in context.Timeline)
                    {
                        Paragraph(line.Time.ToString("yyyy-MM-dd HH:mm") + "　　" + line.Title,
                            _bodyFont, XBrushes.Black, 0, ContentWidth, 1.6);
                    }
                }

                Section("四、调解方案与进度", 62);
                Table(
                    new[] { "次", "日期", "方式", "方案摘要", "当事人意见", "结果" },
                    context.Records.Select(r => new[]
                    {
                        r.Index.ToString(),
                        r.Date,
                        string.IsNullOrEmpty(r.SupplementNote) ? r.Method : r.Method + "\n" + r.SupplementNote,
                        r.Plan,
                        r.Opinion,
                        r.Result
                    }).ToList(),
                    new double[] { 26, 52, 86, 122, 118, 90 },
                    20);

                Section("五、结案报告", 44);
                Paragraph(Dash(kase.CloseSummary), _bodyFont, XBrushes.Black, 0, ContentWidth, 1.6);

                Section("六、签字栏", 110);
                SignatureBlock();

                Section("七、协议扫描件清单", 46);
                if (context.Attachments.Count == 0)
                {
                    Paragraph("暂无扫描件", _bodyFont, _muted, 0, ContentWidth, 1.6);
                }
                else
                {
                    Table(
                        new[] { "序号", "文件名", "大小", "上传人", "上传时间" },
                        context.Attachments.Select(a => new[]
                        {
                            a.Index.ToString(), a.FileName, a.Size, a.Uploader, a.UploadedAt
                        }).ToList(),
                        new double[] { 30, 190, 62, 92, 121 },
                        20);
                }
            }

            public void Save(string filePath)
            {
                Finish();
                _document.Save(filePath);
            }

            private void Finish()
            {
                if (_gfx != null)
                {
                    _gfx.Dispose();
                    _gfx = null;
                }

                int total = _document.PageCount;
                for (int i = 0; i < total; i++)
                {
                    PdfPage page = _document.Pages[i];
                    using (XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
                    {
                        double y = page.Height.Point - 34;
                        gfx.DrawLine(new XPen(XColor.FromArgb(255, 210, 216, 224), 0.6),
                            MarginLeft, y - 12, page.Width.Point - MarginRight, y - 12);
                        gfx.DrawString("安怡物业管理系统 · 纠纷调解子系统 · 处理与结案", _smallFont, _muted, MarginLeft, y);
                        string pageText = "第 " + (i + 1) + " 页 / 共 " + total + " 页";
                        double width = gfx.MeasureString(pageText, _smallFont).Width;
                        gfx.DrawString(pageText, _smallFont, _muted, page.Width.Point - MarginRight - width, y);
                    }
                }
            }

            public void Dispose()
            {
                if (_gfx != null)
                {
                    _gfx.Dispose();
                    _gfx = null;
                }
                _document.Dispose();
            }
        }

        // ============================================================
        // 快照模型与公共小工具
        // ============================================================

        private class ReportContext
        {
            public DisputeCaseDto Case { get; set; }
            public string ExportedAt { get; set; }
            public string ExportedBy { get; set; }
            public string PropertyPath { get; set; }
            public List<PartyLine> Parties { get; } = new List<PartyLine>();
            public List<TimelineLine> Timeline { get; set; } = new List<TimelineLine>();
            public List<RecordLine> Records { get; } = new List<RecordLine>();
            public List<AttachmentLine> Attachments { get; set; } = new List<AttachmentLine>();
        }

        private class PartyLine
        {
            public string Role { get; set; }
            public string Text { get; set; }
            public bool External { get; set; }
        }

        private class TimelineLine
        {
            public DateTime Time { get; set; }
            public string Title { get; set; }
        }

        private class RecordLine
        {
            public int Index { get; set; }
            public string Date { get; set; }
            public string Method { get; set; }
            public string SupplementNote { get; set; }
            public string Plan { get; set; }
            public string Opinion { get; set; }
            public string Result { get; set; }
        }

        private class AttachmentLine
        {
            public int Index { get; set; }
            public string FileName { get; set; }
            public string Size { get; set; }
            public string Uploader { get; set; }
            public string UploadedAt { get; set; }
        }

        private static int ContextDays(DisputeCaseDto kase)
        {
            int days = (int)(DateTime.Now.Date - kase.OccurTime.Date).TotalDays;
            return days < 0 ? 0 : days;
        }

        private static string Dash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
        }

        private static string FirstNonEmpty(string a, string b)
        {
            return string.IsNullOrWhiteSpace(a) ? b : a;
        }

        private static string SafeToken(string caseNo, int caseId)
        {
            string token = string.IsNullOrWhiteSpace(caseNo) ? "JF" + caseId : caseNo.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                token = token.Replace(invalid, '_');
            }
            return token;
        }
    }
}
