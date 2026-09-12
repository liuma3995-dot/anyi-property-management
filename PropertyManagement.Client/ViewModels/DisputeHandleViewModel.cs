using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Dispute;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>处理进度时间线项（t_dispute_status_log，竖排：登记受理 / 处理中 / 已结案）。</summary>
    public class DisputeTimelineItem
    {
        public string Title { get; set; }
        public string TimeText { get; set; }
        /// <summary>非末项显示连接线。</summary>
        public bool ShowLine { get; set; }
    }

    /// <summary>调解方案与进度行（六列：次 / 日期 / 方式 / 方案摘要 / 当事人意见 / 结果；补录带标记与原因）。</summary>
    public class DisputeRecordRow
    {
        public int Index { get; set; }
        public DisputeRecordDto Dto { get; set; }
        public string DateText { get { return Dto.RecordTime.ToString("MM-dd"); } }
        public string Method { get { return OrDash(Dto.Method); } }
        public string PlanSummary { get { return OrDash(FirstNonEmpty(Dto.PlanSummary, Dto.Content)); } }
        public string PartyOpinion { get { return OrDash(Dto.PartyOpinion); } }
        public string ResultText { get { return OrDash(Dto.Result); } }
        /// <summary>备注（记录内容 t_dispute_record.content）。</summary>
        public string ContentText { get { return OrDash(Dto.Content); } }
        public bool IsSupplement { get { return Dto.IsSupplement; } }
        /// <summary>补录标记与原因（红字小字，无原因只显示“补录”）。</summary>
        public string SupplementText
        {
            get
            {
                if (!Dto.IsSupplement) return string.Empty;
                return string.IsNullOrEmpty(Dto.SupplementReason) ? "补录" : "补录：" + Dto.SupplementReason;
            }
        }
        public bool HasSupplementText { get { return !string.IsNullOrEmpty(SupplementText); } }

        private static string OrDash(string value) { return string.IsNullOrEmpty(value) ? "—" : value; }
        private static string FirstNonEmpty(string a, string b) { return string.IsNullOrEmpty(a) ? b : a; }
    }

    /// <summary>结案报告导出格式选项（F2：PDF 打印签字 / Excel 归档）。</summary>
    public class DisputeExportFormatOption
    {
        public string Label { get; set; }
        public ExportFormat Value { get; set; }
        public override string ToString() { return Label; }
    }

    /// <summary>处理与结案页（PG-DIS-03，UC-DIS-003/004/005/007，BR-DIS-01/02/04）：
    /// 构造不自动加载（P0 竞态修复）——仅经 LoadCaseAsync(caseId)（ShellViewModel 从列表跳转时传入）装载；
    /// 无案件时显示空态。含状态时间线、六列调解方案表、新增记录、结案确认弹窗、结案后补录（管理员 + 原因必填）与已结案查看锁定。</summary>
    public class DisputeHandleViewModel : BaseInfoPageViewModel
    {
        private DisputeCaseDto _case;
        private bool _hasCase;
        private DateTime? _recordDate = DateTime.Now.Date;
        private string _recordMethod = string.Empty;
        private string _recordPlan = string.Empty;
        private string _recordOpinion = string.Empty;
        private string _recordResult = string.Empty;
        private string _recordContent = string.Empty;
        private int _closeType;
        private string _summary = string.Empty;
        private string _supContent = string.Empty;
        private string _supMethod = string.Empty;
        private string _supPlan = string.Empty;
        private string _supOpinion = string.Empty;
        private string _supResult = string.Empty;
        private string _supReason = string.Empty;

        public DisputeHandleViewModel(IApiClient api) : base(api)
        {
            // P0：构造不再“取第一个处理中案件”兜底加载，避免与 ShellViewModel.LoadCaseAsync 竞态覆盖目标案件。
            // 无 caseId（侧栏直进）→ 保持空态，由 XAML 提示“请从纠纷列表选择案件进入处理”。
            AddRecordCommand = new AsyncRelayCommand(AddRecordAsync);
            CloseCommand = new AsyncRelayCommand(CloseAsync);
            SupplementCommand = new AsyncRelayCommand(SupplementAsync);
            ExportReportCommand = new AsyncRelayCommand(ExportReportAsync);
            UploadAttachmentCommand = new AsyncRelayCommand(UploadAttachmentAsync);
            DownloadAttachmentCommand = new AsyncRelayCommand<DisputeAttachmentDto>(DownloadAttachmentAsync);
            DeleteAttachmentCommand = new AsyncRelayCommand<DisputeAttachmentDto>(DeleteAttachmentAsync);
            SelectedExportFormat = ExportFormatOptions[0];
        }

        public DisputeCaseDto Case { get { return _case; } private set { SetProperty(ref _case, value); } }
        public bool HasCase { get { return _hasCase; } private set { SetProperty(ref _hasCase, value); } }
        /// <summary>空态反向绑定（侧栏直进无案件时显示引导）。</summary>
        public bool NoCase { get { return !_hasCase; } }

        /// <summary>事件标题：JF-编号 · 当事人 类型纠纷（原型 PG-DIS-03 事件头）。</summary>
        public string CaseTitle
        {
            get
            {
                if (_case == null) return string.Empty;
                var no = string.IsNullOrEmpty(_case.CaseNo) ? "JF-" + _case.Id.ToString("D4") : _case.CaseNo;
                var summary = string.IsNullOrEmpty(_case.PartySummary) ? string.Empty : _case.PartySummary + " ";
                return no + " · " + summary + (_case.TypeName ?? string.Empty) + "纠纷";
            }
        }

        public string CaseStatusText { get { return _case == null || string.IsNullOrEmpty(_case.StatusText) ? "已登记" : _case.StatusText; } }
        public DisputeCaseStatus CaseStatusEnum { get { return _case == null ? DisputeCaseStatus.Registered : _case.Status; } }
        public string TypeName { get { return _case == null || string.IsNullOrEmpty(_case.TypeName) ? "—" : _case.TypeName; } }
        public string MediatorName { get { return _case == null || string.IsNullOrEmpty(_case.MediatorName) ? "未分配" : _case.MediatorName; } }
        /// <summary>已受理 N 天 = 今天 - 登记日期（原型事件头右侧）。</summary>
        public string DaysOpenText
        {
            get
            {
                if (_case == null) return string.Empty;
                int days = (int)(DateTime.Now.Date - _case.OccurTime.Date).TotalDays;
                if (days < 0) days = 0;
                return "已受理 " + days + " 天";
            }
        }
        /// <summary>当事人行（甲方 / 乙方 + 电话）；无当事人明细时回退 PartySummary。</summary>
        public string PartyLineText
        {
            get
            {
                if (_case == null) return string.Empty;
                if (Parties.Count > 0)
                {
                    return string.Join("　｜　", Parties.Select(p =>
                        (p.PartyTypeText ?? (p.PartyType == "1" ? "乙方" : "甲方")) + "：" + p.Name +
                        (string.IsNullOrEmpty(p.Phone) ? string.Empty : " · " + p.Phone)));
                }
                return _case.PartySummary ?? string.Empty;
            }
        }
        public string LevelText { get { return _case == null || string.IsNullOrEmpty(_case.LevelText) ? string.Empty : _case.LevelText; } }
        public bool HasLevel { get { return !string.IsNullOrEmpty(LevelText); } }

        /// <summary>已结案 → 页面锁定为查看模式（保存 / 结案禁用，走补录）。</summary>
        public bool IsClosed { get { return _case != null && _case.Status == DisputeCaseStatus.Closed; } }
        public bool CanEdit { get { return !IsClosed; } }
        public bool HasCloseSummary { get { return IsClosed && !string.IsNullOrEmpty(_case.CloseSummary); } }
        public string CloseTypeDisplay { get { return _case == null || string.IsNullOrEmpty(_case.CloseTypeText) ? "—" : _case.CloseTypeText; } }

        public ObservableCollection<DisputeTimelineItem> TimelineItems { get; } = new ObservableCollection<DisputeTimelineItem>();
        public ObservableCollection<DisputeRecordRow> Records { get; } = new ObservableCollection<DisputeRecordRow>();
        public ObservableCollection<DisputePartyDto> Parties { get; } = new ObservableCollection<DisputePartyDto>();
        /// <summary>调解协议扫描件（F1，随案件详情一并装载）。</summary>
        public ObservableCollection<DisputeAttachmentDto> Attachments { get; } = new ObservableCollection<DisputeAttachmentDto>();
        public bool HasAttachments { get { return Attachments.Count > 0; } }
        /// <summary>空态反向绑定（无扫描件时显示提示）。</summary>
        public bool HasNoAttachments { get { return Attachments.Count == 0; } }
        public string AttachmentEmptyText { get { return "暂无扫描件（支持 PDF / JPG / PNG，单个 ≤20MB，最多 10 份）"; } }
        public string AttachmentSummaryText
        {
            get { return "调解协议扫描件（" + Attachments.Count + " / 10）"; }
        }

        public DateTime? RecordDate { get { return _recordDate; } set { SetProperty(ref _recordDate, value); } }
        public string RecordMethod { get { return _recordMethod; } set { SetProperty(ref _recordMethod, value); } }
        public string RecordPlan { get { return _recordPlan; } set { SetProperty(ref _recordPlan, value); } }
        public string RecordOpinion { get { return _recordOpinion; } set { SetProperty(ref _recordOpinion, value); } }
        public string RecordResult { get { return _recordResult; } set { SetProperty(ref _recordResult, value); } }
        public string RecordContent { get { return _recordContent; } set { SetProperty(ref _recordContent, value); } }

        public int CloseType
        {
            get { return _closeType; }
            set
            {
                if (SetProperty(ref _closeType, value))
                {
                    OnPropertyChanged(nameof(IsCloseMediated));
                    OnPropertyChanged(nameof(IsCloseSettled));
                    OnPropertyChanged(nameof(IsCloseTransferred));
                }
            }
        }

        /// <summary>结案类型单选（P-08）：调解成功（签订协议）/ 调解终止（无法达成）/ 移交司法 / 派出所。</summary>
        public bool IsCloseMediated { get { return _closeType == 0; } set { if (value) CloseType = 0; } }
        public bool IsCloseSettled { get { return _closeType == 1; } set { if (value) CloseType = 1; } }
        public bool IsCloseTransferred { get { return _closeType == 2; } set { if (value) CloseType = 2; } }
        public string SummaryText { get { return _summary; } set { SetProperty(ref _summary, value); } }

        public string SupContent { get { return _supContent; } set { SetProperty(ref _supContent, value); } }
        public string SupMethod { get { return _supMethod; } set { SetProperty(ref _supMethod, value); } }
        public string SupPlan { get { return _supPlan; } set { SetProperty(ref _supPlan, value); } }
        public string SupOpinion { get { return _supOpinion; } set { SetProperty(ref _supOpinion, value); } }
        public string SupResult { get { return _supResult; } set { SetProperty(ref _supResult, value); } }
        public string SupReason { get { return _supReason; } set { SetProperty(ref _supReason, value); } }

        public IAsyncRelayCommand AddRecordCommand { get; }
        public IAsyncRelayCommand CloseCommand { get; }
        public IAsyncRelayCommand SupplementCommand { get; }

        // ---------- F2：结案报告导出（仅已结案可用） ----------

        /// <summary>导出格式选项：PDF（打印签字）/ Excel（归档）。</summary>
        public List<DisputeExportFormatOption> ExportFormatOptions { get; } = new List<DisputeExportFormatOption>
        {
            new DisputeExportFormatOption { Label = "PDF（打印签字）", Value = ExportFormat.Pdf },
            new DisputeExportFormatOption { Label = "Excel（归档）", Value = ExportFormat.Excel }
        };

        private DisputeExportFormatOption _selectedExportFormat;
        public DisputeExportFormatOption SelectedExportFormat
        {
            get { return _selectedExportFormat; }
            set { SetProperty(ref _selectedExportFormat, value); }
        }

        /// <summary>仅已结案案件可导出结案报告。</summary>
        public bool CanExportReport { get { return IsClosed; } }

        public IAsyncRelayCommand ExportReportCommand { get; }

        // ---------- F1：调解协议扫描件（上传 / 下载 / 删除） ----------

        /// <summary>上传扫描件：未结案 / 已结案均可（结案不强制上传）；类型与大小由服务端二次校验。</summary>
        public IAsyncRelayCommand UploadAttachmentCommand { get; }
        public IAsyncRelayCommand<DisputeAttachmentDto> DownloadAttachmentCommand { get; }
        public IAsyncRelayCommand<DisputeAttachmentDto> DeleteAttachmentCommand { get; }

        /// <summary>按案件 id 装载（ShellViewModel 列表跳转路径唯一入口；不改 ShellViewModel）。</summary>
        public async Task LoadCaseAsync(int id)
        {
            await RunAsync(async () => { await LoadCaseCoreAsync(id); }, null);
        }

        private async Task LoadCaseCoreAsync(int id)
        {
            var detail = await Api.GetDisputeAsync(id);
            ApplyDetail(detail);
        }

        private void ApplyDetail(DisputeCaseDetailDto detail)
        {
            if (detail == null || detail.Case == null)
            {
                _case = null;
                HasCase = false;
                OnPropertyChanged(nameof(NoCase));
                return;
            }

            Case = detail.Case;
            HasCase = true;
            OnPropertyChanged(nameof(NoCase));
            OnPropertyChanged(nameof(CaseTitle));
            OnPropertyChanged(nameof(CaseStatusText));
            OnPropertyChanged(nameof(CaseStatusEnum));
            OnPropertyChanged(nameof(TypeName));
            OnPropertyChanged(nameof(MediatorName));
            OnPropertyChanged(nameof(DaysOpenText));
            OnPropertyChanged(nameof(LevelText));
            OnPropertyChanged(nameof(HasLevel));
            OnPropertyChanged(nameof(IsClosed));
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanExportReport));
            OnPropertyChanged(nameof(HasCloseSummary));
            OnPropertyChanged(nameof(CloseTypeDisplay));

            Parties.Clear();
            if (detail.Parties != null) foreach (var p in detail.Parties) Parties.Add(p);
            OnPropertyChanged(nameof(PartyLineText));

            Records.Clear();
            if (detail.Records != null)
            {
                int index = 1;
                foreach (var r in detail.Records.OrderBy(r => r.RecordTime)) Records.Add(new DisputeRecordRow { Index = index++, Dto = r });
            }

            Attachments.Clear();
            if (detail.Attachments != null) foreach (var a in detail.Attachments) Attachments.Add(a);
            OnPropertyChanged(nameof(HasAttachments));
            OnPropertyChanged(nameof(HasNoAttachments));
            OnPropertyChanged(nameof(AttachmentSummaryText));

            BuildTimeline(detail.StatusLogs, detail.Records);
        }

        /// <summary>处理进度时间线：状态日志（登记受理/处理中/已结案）+ 每条调解记录（用记录日期），按时间合并排序；无数据时以当前状态兜底。</summary>
        private void BuildTimeline(IList<DisputeStatusLogDto> logs, IList<DisputeRecordDto> records)
        {
            TimelineItems.Clear();
            var entries = new List<TimelineEntry>();
            if (logs != null)
            {
                foreach (var log in logs)
                    entries.Add(new TimelineEntry { Title = StatusTitle(log.NewStatus), Time = log.ChangedAt });
            }
            if (records != null)
            {
                int n = 1;
                foreach (var r in records.OrderBy(x => x.RecordTime))
                {
                    string title = "第" + n + "次调解" + (string.IsNullOrWhiteSpace(r.Method) ? string.Empty : " · " + r.Method.Trim());
                    if (r.IsSupplement) title += "（补录）";
                    entries.Add(new TimelineEntry { Title = title, Time = r.RecordTime });
                    n++;
                }
            }
            if (entries.Count == 0 && _case != null)
                entries.Add(new TimelineEntry { Title = StatusTitle(_case.Status), Time = _case.OccurTime });

            var items = entries.OrderBy(e => e.Time)
                .Select(e => new DisputeTimelineItem { Title = e.Title, TimeText = e.Time.ToString("MM-dd HH:mm") })
                .ToList();
            for (int i = 0; i < items.Count; i++) items[i].ShowLine = i < items.Count - 1;
            foreach (var item in items) TimelineItems.Add(item);
        }

        /// <summary>时间线合并项（状态节点 / 调解记录节点）。</summary>
        private class TimelineEntry
        {
            public string Title { get; set; }
            public DateTime Time { get; set; }
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

        private async Task AddRecordAsync()
        {
            if (_case == null || IsClosed) return;
            if (!RecordDate.HasValue) { ErrorText = "请选择调解日期"; return; }
            if (string.IsNullOrWhiteSpace(_recordPlan) && string.IsNullOrWhiteSpace(_recordContent))
            {
                ErrorText = "方案摘要与记录内容至少填写一项";
                return;
            }
            await RunAsync(async () =>
            {
                if (_case.Status == DisputeCaseStatus.Registered)
                {
                    // 首次记录自动推进 已登记 → 处理中（BR-DIS-01 正常路径）
                    await Api.UpdateDisputeStatusAsync(_case.Id, new DisputeCaseStatusRequest { Status = DisputeCaseStatus.Handling });
                }
                await Api.AddDisputeRecordAsync(_case.Id, new DisputeRecordRequest
                {
                    CaseId = _case.Id,
                    RecordTime = RecordDate.Value.Date + DateTime.Now.TimeOfDay,
                    Content = _recordContent,
                    Method = _recordMethod,
                    PlanSummary = _recordPlan,
                    PartyOpinion = _recordOpinion,
                    Result = _recordResult
                });
                RecordMethod = RecordPlan = RecordOpinion = RecordResult = RecordContent = string.Empty;
                RecordDate = DateTime.Now.Date;
                await LoadCaseCoreAsync(_case.Id);
            }, "调解记录已保存");
        }

        private async Task CloseAsync()
        {
            if (_case == null || IsClosed) return;
            if (string.IsNullOrWhiteSpace(_summary)) { ErrorText = "结案报告为必填项"; return; }
            var confirm = MessageBox.Show("结案后不可更改处理记录，确认结案？", "确认结案",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            await RunAsync(async () =>
            {
                var closeType = _closeType == 0 ? DisputeCloseType.Mediated
                    : (_closeType == 1 ? DisputeCloseType.Settled : DisputeCloseType.Transferred);
                await Api.CloseDisputeAsync(_case.Id, new DisputeCloseRequest { CaseId = _case.Id, CloseType = closeType, Summary = _summary });
                SummaryText = string.Empty;
                await LoadCaseCoreAsync(_case.Id);
            }, "案件已结案，复盘提醒已生成");
        }

        /// <summary>结案后补录（UC-DIS-007 / BR-DIS-04）：仅管理员 + 补录原因必填，服务端落补录标记。</summary>
        private async Task SupplementAsync()
        {
            if (_case == null || !IsClosed) return;
            if (string.IsNullOrWhiteSpace(_supReason)) { ErrorText = "补录原因为必填项（留痕）"; return; }
            if (string.IsNullOrWhiteSpace(_supContent) && string.IsNullOrWhiteSpace(_supPlan))
            {
                ErrorText = "记录内容与方案摘要至少填写一项";
                return;
            }
            await RunAsync(async () =>
            {
                try
                {
                    await Api.SupplementDisputeCaseAsync(_case.Id, new DisputeSupplementRequest
                    {
                        Content = _supContent,
                        Method = _supMethod,
                        PlanSummary = _supPlan,
                        PartyOpinion = _supOpinion,
                        Result = _supResult,
                        Reason = _supReason
                    });
                }
                catch (ApiClientException ex)
                {
                    // 403 → 明确提示仅管理员可补录
                    throw new ApiClientException(ex.Code,
                        ex.Code == ErrorCode.Forbidden ? "仅管理员可补录" : ex.Message);
                }
                SupContent = SupMethod = SupPlan = SupOpinion = SupResult = SupReason = string.Empty;
                await LoadCaseCoreAsync(_case.Id);
            }, "补录记录已保存");
        }

        /// <summary>导出结案报告（F2）：仅已结案案件；PDF / Excel 由服务端生成并留痕，导出后可直接打开文件。</summary>
        private async Task ExportReportAsync()
        {
            if (_case == null) return;
            if (!IsClosed)
            {
                ErrorText = "仅已结案案件可导出结案报告";
                return;
            }

            DisputeExportFormatOption option = _selectedExportFormat ?? ExportFormatOptions[0];
            string typeName = option.Value == ExportFormat.Excel ? "Excel" : "PDF";
            await RunAsync(async () =>
            {
                var log = await Api.ExportDisputeCloseReportAsync(_case.Id, option.Value);
                string path = log == null ? null : log.FilePath;
                string location = string.IsNullOrEmpty(path)
                    ? "（导出留痕编号 " + (log == null ? 0 : log.Id) + "）"
                    : path;
                var answer = MessageBox.Show(
                    "结案报告（" + typeName + "）已生成：\n" + location + "\n\n是否立即打开文件？",
                    "导出结案报告", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (answer == MessageBoxResult.Yes && !string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        ErrorText = "结案报告已生成，但自动打开失败：" + ex.Message;
                    }
                }
            }, "结案报告（" + typeName + "）已导出");
        }

        /// <summary>F1：上传调解协议扫描件（OpenFileDialog → multipart → 刷新清单）。</summary>
        private async Task UploadAttachmentAsync()
        {
            if (_case == null) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择调解协议扫描件",
                Filter = "扫描件（PDF / JPG / PNG）|*.pdf;*.jpg;*.jpeg;*.png",
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;

            await RunAsync(async () =>
            {
                var uploaded = await Api.UploadDisputeAttachmentAsync(_case.Id, dialog.FileName);
                await LoadCaseCoreAsync(_case.Id);
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已上传扫描件：" +
                    (uploaded == null ? System.IO.Path.GetFileName(dialog.FileName) : uploaded.FileName);
            }, null);
        }

        /// <summary>F1：下载扫描件（另存为）→ 询问是否打开。</summary>
        private async Task DownloadAttachmentAsync(DisputeAttachmentDto attachment)
        {
            if (_case == null || attachment == null) return;
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存扫描件",
                FileName = attachment.FileName,
                Filter = "全部文件|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            await RunAsync(async () =>
            {
                await Api.DownloadDisputeAttachmentAsync(_case.Id, attachment.Id, dialog.FileName);
                var answer = MessageBox.Show("扫描件已保存到：\n" + dialog.FileName + "\n\n是否立即打开文件？",
                    "下载扫描件", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (answer == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        ErrorText = "文件已保存，但自动打开失败：" + ex.Message;
                    }
                }
            }, "扫描件已下载");
        }

        /// <summary>F1：删除扫描件（仅管理员；服务端软删记录并删除物理文件）。</summary>
        private async Task DeleteAttachmentAsync(DisputeAttachmentDto attachment)
        {
            if (_case == null || attachment == null) return;
            var confirm = MessageBox.Show("确认删除扫描件「" + attachment.FileName + "」？\n删除后物理文件一并移除，且不可恢复。",
                "删除扫描件", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            await RunAsync(async () =>
            {
                try
                {
                    await Api.DeleteDisputeAttachmentAsync(_case.Id, attachment.Id);
                }
                catch (ApiClientException ex)
                {
                    throw new ApiClientException(ex.Code,
                        ex.Code == ErrorCode.Forbidden ? "仅管理员可删除扫描件" : ex.Message);
                }
                await LoadCaseCoreAsync(_case.Id);
            }, "扫描件已删除（物理文件已移除）");
        }
    }
}
