// ── 已下线（R4 变更）：应急处置子系统仅保留「场景与步骤维护」，事件工作台模块停止提供 ──
// 本文件为源码存档，已从 PropertyManagement.Client.csproj 移除，不参与编译；
// 恢复上线需同步恢复 ShellViewModel 导航、MainWindow DataTemplate 与工程项。
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Emergency;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>事件行（PG-EMG-03）。</summary>
    public class EmergencyEventRow : ObservableObject
    {
        public EmergencyEventDto Dto { get; set; }
        public string EventNo { get { return Dto.EventNo ?? ("EM-" + Dto.Id.ToString("D4")); } }
        public string SceneName { get { return Dto.SceneName ?? string.Empty; } }
        public string Location { get { return Dto.Location ?? string.Empty; } }
        public string LevelText { get { return string.IsNullOrEmpty(Dto.LevelText) ? (Dto.Level == 0 ? "Ⅰ 级" : (Dto.Level == 1 ? "Ⅱ 级" : "Ⅲ 级")) : Dto.LevelText; } }
        public string EventTimeText { get { return Dto.EventTimeText ?? Dto.EventTime.ToString("MM-dd HH:mm"); } }
        public string MainPerson { get { return string.IsNullOrEmpty(Dto.MainPerson) ? "—" : Dto.MainPerson; } }
        public string ElapsedText { get { return string.IsNullOrEmpty(Dto.ElapsedText) ? "—" : Dto.ElapsedText; } }
        public string StatusText { get { return Dto.StatusText ?? string.Empty; } }
        public bool IsOverdue { get { return Dto.IsOverdue; } }
        /// <summary>操作列：处置（已发起/处置中）。</summary>
        public bool CanHandle { get { return Dto.Status == EmergencyEventStatus.Initiated || Dto.Status == EmergencyEventStatus.Handling; } }
        /// <summary>操作列：结案（处置中，BR-EMG-02 处置完成方可结案）。</summary>
        public bool CanClose { get { return Dto.Status == EmergencyEventStatus.Handling; } }
        /// <summary>操作列：查看（已结案/已复盘/已撤销）。</summary>
        public bool CanView { get { return Dto.Status == EmergencyEventStatus.Closed || Dto.Status == EmergencyEventStatus.Reviewed || Dto.Status == EmergencyEventStatus.Cancelled; } }
        /// <summary>补录弹窗下拉显示文本。</summary>
        public string DisplayText { get { return EventNo + " " + SceneName + "（" + StatusText + "）"; } }
    }

    /// <summary>类型筛选下拉项（全部 + 场景）。</summary>
    public class EmergencySceneOption
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Display { get { return Id <= 0 ? "类型：全部" : "类型：" + Name; } }
    }

    /// <summary>处置记录行（详情弹窗，补录标记展示）。</summary>
    public class EmergencyRecordRow : ObservableObject
    {
        public EmergencyRecordDto Dto { get; set; }
        public string TimeText { get { return Dto.RecordTime.ToString("MM-dd HH:mm"); } }
        public string Content { get { return Dto.Content ?? string.Empty; } }
        public string Result { get { return Dto.Result ?? string.Empty; } }
        public string Recorder { get { return string.IsNullOrWhiteSpace(Dto.Recorder) ? "—" : Dto.Recorder; } }
        public string SupplementText { get { return Dto.IsSupplement ? "补录" : string.Empty; } }
    }

    /// <summary>复盘改进措施行（详情弹窗只读展示 + 逾期标红，原型行 251）。</summary>
    public class EmergencyReviewItemRow : ObservableObject
    {
        public EmergencyReviewItemDto Dto { get; set; }
        public string Content { get { return Dto.Content ?? string.Empty; } }
        public string Owner { get { return string.IsNullOrWhiteSpace(Dto.Owner) ? "—" : Dto.Owner; } }
        public string DueText { get { return Dto.DueDate.HasValue ? Dto.DueDate.Value.ToString("MM-dd") : "—"; } }
        public string StatusText { get { return Dto.StatusText ?? (Dto.Status == 1 ? "进行中" : (Dto.Status == 2 ? "已完成" : "待开展")); } }
        /// <summary>改进项逾期：期限早于今天且未完成 → 标红。</summary>
        public bool IsOverdue { get { return Dto.DueDate.HasValue && Dto.DueDate.Value.Date < DateTime.Today && Dto.Status != 2; } }
    }

    /// <summary>事件工作台（PG-EMG-03，UC-EMG-008，BR-EMG-02/04/06，P-03）。</summary>
    public class EmergencyWorkbenchViewModel : BaseInfoPageViewModel
    {
        private const int PageSize = 10;

        private string _keyword = string.Empty;
        private EmergencySceneOption _sceneFilter;
        private int _levelFilterIndex; // 0=全部，1/2/3 → Level
        private int _statusFilterIndex; // 0=全部 1=进行中 2=已结案
        private int _pageIndex = 1;
        private int _total;
        private int _totalPages;

        // 统计卡（GET events/stats，T6-1-7）
        private int _ongoingCount;
        private int _overtimeCount;
        private int _todayNewCount;
        private int _monthClosedCount;
        private int _reviewPendingCount;

        // 弹窗状态
        private bool _isDetailVisible;
        private bool _isCloseVisible;
        private bool _isSupplementVisible;
        private EmergencyEventDetailDto _detail;
        private string _detailTitle = string.Empty;
        private EmergencyEventRow _closeTarget;
        private string _closeSummary = string.Empty;
        private DateTime _recordTime = DateTime.Now;
        private string _recordContent = string.Empty;
        private string _recordResult = string.Empty;
        private EmergencyEventRow _supplementTarget;
        private DateTime _supplementTime = DateTime.Now;
        private string _supplementContent = string.Empty;
        private string _supplementResult = string.Empty;

        /// <summary>跨页导航回调（工作台「发起事件」→ 应急发起页，由 ShellViewModel 注入）。</summary>
        private readonly Action<string> _navigateToPage;

        public EmergencyWorkbenchViewModel(IApiClient api) : this(api, null) { }

        public EmergencyWorkbenchViewModel(IApiClient api, Action<string> navigateToPage) : base(api)
        {
            _navigateToPage = navigateToPage;
            QueryCommand = new AsyncRelayCommand(() => { _pageIndex = 1; return LoadAsync(); });
            PrevPageCommand = new AsyncRelayCommand(() => { if (_pageIndex > 1) { _pageIndex--; return LoadAsync(); } return Task.CompletedTask; });
            NextPageCommand = new AsyncRelayCommand(() => { if (_pageIndex < _totalPages) { _pageIndex++; return LoadAsync(); } return Task.CompletedTask; });
            LaunchCommand = new RelayCommand(LaunchEvent);
            SupplementCommand = new AsyncRelayCommand(OpenSupplementAsync);
            HandleCommand = new AsyncRelayCommand<EmergencyEventRow>(OpenHandleAsync);
            CloseCaseCommand = new AsyncRelayCommand<EmergencyEventRow>(OpenCloseAsync);
            ViewCommand = new AsyncRelayCommand<EmergencyEventRow>(OpenViewAsync);
            CloseDialogCommand = new RelayCommand(CloseDialogs);
            AddRecordCommand = new AsyncRelayCommand(AddRecordAsync);
            ConfirmCloseCommand = new AsyncRelayCommand(ConfirmCloseAsync);
            ConfirmSupplementCommand = new AsyncRelayCommand(ConfirmSupplementAsync);
            _ = LoadAsync();
        }

        public ObservableCollection<EmergencyEventRow> Items { get; } = new ObservableCollection<EmergencyEventRow>();
        public ObservableCollection<EmergencySceneOption> SceneOptions { get; } = new ObservableCollection<EmergencySceneOption>();
        public ObservableCollection<EmergencyEventRow> SupplementCandidates { get; } = new ObservableCollection<EmergencyEventRow>();
        public ObservableCollection<EmergencyRecordRow> DetailRecords { get; } = new ObservableCollection<EmergencyRecordRow>();
        public ObservableCollection<EmergencyAssignDto> DetailAssignments { get; } = new ObservableCollection<EmergencyAssignDto>();
        public ObservableCollection<EmergencyReviewItemRow> DetailReviewItems { get; } = new ObservableCollection<EmergencyReviewItemRow>();

        public string Keyword { get { return _keyword; } set { SetProperty(ref _keyword, value); } }

        public EmergencySceneOption SceneFilter
        {
            get { return _sceneFilter; }
            set { if (SetProperty(ref _sceneFilter, value)) { _pageIndex = 1; _ = LoadAsync(); } }
        }

        /// <summary>级别筛选：0=全部，1/2/3 → Ⅰ/Ⅱ/Ⅲ。</summary>
        public int LevelFilterIndex
        {
            get { return _levelFilterIndex; }
            set { if (SetProperty(ref _levelFilterIndex, value)) { _pageIndex = 1; _ = LoadAsync(); } }
        }

        /// <summary>状态筛选：0=全部 1=进行中（已发起+处置中） 2=已结案（已结案+已复盘）。</summary>
        public int StatusFilterIndex
        {
            get { return _statusFilterIndex; }
            set { if (SetProperty(ref _statusFilterIndex, value)) { _pageIndex = 1; _ = LoadAsync(); } }
        }

        public int PageIndex { get { return _pageIndex; } private set { SetProperty(ref _pageIndex, value); } }
        public int Total { get { return _total; } private set { SetProperty(ref _total, value); } }
        public int TotalPages { get { return _totalPages; } private set { SetProperty(ref _totalPages, value); } }
        public string PageText { get { return Total <= 0 ? "共 0 条" : "共 " + Total + " 条 · 第 " + PageIndex + " / " + Math.Max(1, TotalPages) + " 页"; } }
        public bool CanPrev { get { return PageIndex > 1; } }
        public bool CanNext { get { return PageIndex < TotalPages; } }

        public int OngoingCount { get { return _ongoingCount; } private set { SetProperty(ref _ongoingCount, value); } }
        public int OvertimeCount { get { return _overtimeCount; } private set { SetProperty(ref _overtimeCount, value); OnPropertyChanged(nameof(HasOvertime)); OnPropertyChanged(nameof(OvertimeLabel)); } }
        /// <summary>统计卡子标注「含超时 N 起」（原型行 106-107）。</summary>
        public bool HasOvertime { get { return _overtimeCount > 0; } }
        public string OvertimeLabel { get { return "含超时 " + _overtimeCount + " 起"; } }
        public int TodayNewCount { get { return _todayNewCount; } private set { SetProperty(ref _todayNewCount, value); } }
        public int MonthClosedCount { get { return _monthClosedCount; } private set { SetProperty(ref _monthClosedCount, value); } }
        public int ReviewPendingCount { get { return _reviewPendingCount; } private set { SetProperty(ref _reviewPendingCount, value); } }
        /// <summary>工作台顶部提醒条（P-03 双提醒之一）。</summary>
        public bool HasReviewPending { get { return _reviewPendingCount > 0; } }
        public string ReviewPendingText { get { return "有 " + _reviewPendingCount + " 起事件结案后未复盘（超过 P-03 时限）请尽快补建复盘"; } }

        public bool IsDetailVisible { get { return _isDetailVisible; } set { SetProperty(ref _isDetailVisible, value); } }
        public bool IsCloseVisible { get { return _isCloseVisible; } set { SetProperty(ref _isCloseVisible, value); } }
        public bool IsSupplementVisible { get { return _isSupplementVisible; } set { SetProperty(ref _isSupplementVisible, value); } }
        public EmergencyEventDetailDto Detail
        {
            get { return _detail; }
            private set
            {
                SetProperty(ref _detail, value);
                OnPropertyChanged(nameof(DetailCanRecord));
                OnPropertyChanged(nameof(DetailIsArchived));
                OnPropertyChanged(nameof(HasDetailReview));
            }
        }
        public string DetailTitle { get { return _detailTitle; } private set { SetProperty(ref _detailTitle, value); } }
        /// <summary>详情弹窗中事件处于处置中（可新增处置记录）。</summary>
        public bool DetailCanRecord
        {
            get
            {
                return Detail != null && Detail.Event != null
                    && (Detail.Event.Status == EmergencyEventStatus.Initiated || Detail.Event.Status == EmergencyEventStatus.Handling);
            }
        }
        /// <summary>详情弹窗中事件已归档（已结案/已复盘，BR-EMG-06 只可补录）。</summary>
        public bool DetailIsArchived
        {
            get
            {
                return Detail != null && Detail.Event != null
                    && (Detail.Event.Status == EmergencyEventStatus.Closed || Detail.Event.Status == EmergencyEventStatus.Reviewed);
            }
        }
        /// <summary>详情弹窗复盘区块显隐。</summary>
        public System.Windows.Visibility HasDetailReview
        {
            get { return Detail != null && Detail.Review != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; }
        }
        public string CloseSummary { get { return _closeSummary; } set { SetProperty(ref _closeSummary, value); } }
        public string RecordTimeText { get { return _recordTime.ToString("yyyy-MM-dd HH:mm"); } }
        public DateTime RecordTime { get { return _recordTime; } set { SetProperty(ref _recordTime, value); } }
        public string RecordContent { get { return _recordContent; } set { SetProperty(ref _recordContent, value); } }
        public string RecordResult { get { return _recordResult; } set { SetProperty(ref _recordResult, value); } }
        public EmergencyEventRow SupplementTarget
        {
            get { return _supplementTarget; }
            set { SetProperty(ref _supplementTarget, value); }
        }
        public DateTime SupplementTime { get { return _supplementTime; } set { SetProperty(ref _supplementTime, value); } }
        public string SupplementContent { get { return _supplementContent; } set { SetProperty(ref _supplementContent, value); } }
        public string SupplementResult { get { return _supplementResult; } set { SetProperty(ref _supplementResult, value); } }

        public IAsyncRelayCommand QueryCommand { get; }
        public IAsyncRelayCommand PrevPageCommand { get; }
        public IAsyncRelayCommand NextPageCommand { get; }
        public IRelayCommand LaunchCommand { get; }
        public IAsyncRelayCommand SupplementCommand { get; }
        public IAsyncRelayCommand<EmergencyEventRow> HandleCommand { get; }
        public IAsyncRelayCommand<EmergencyEventRow> CloseCaseCommand { get; }
        public IAsyncRelayCommand<EmergencyEventRow> ViewCommand { get; }
        public IRelayCommand CloseDialogCommand { get; }
        public IAsyncRelayCommand AddRecordCommand { get; }
        public IAsyncRelayCommand ConfirmCloseCommand { get; }
        public IAsyncRelayCommand ConfirmSupplementCommand { get; }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var stats = await Api.GetEmergencyEventStatsAsync();
                OngoingCount = stats.Ongoing;
                OvertimeCount = stats.OvertimeCount;
                TodayNewCount = stats.TodayNew;
                MonthClosedCount = stats.MonthClosed;
                int prevPending = _reviewPendingCount;
                ReviewPendingCount = stats.ReviewPending;
                if (prevPending != stats.ReviewPending)
                {
                    OnPropertyChanged(nameof(HasReviewPending));
                    OnPropertyChanged(nameof(ReviewPendingText));
                }

                if (SceneOptions.Count == 0)
                {
                    SceneOptions.Add(new EmergencySceneOption { Id = 0, Name = "全部" });
                    foreach (var s in await Api.GetEmergencyScenesAsync())
                        SceneOptions.Add(new EmergencySceneOption { Id = s.Id, Name = s.Name });
                    if (_sceneFilter == null) SetProperty(ref _sceneFilter, SceneOptions[0], nameof(SceneFilter));
                }

                var query = new EmergencyEventQueryRequest
                {
                    PageIndex = _pageIndex, PageSize = PageSize, Keyword = Keyword,
                    SceneId = _sceneFilter != null && _sceneFilter.Id > 0 ? _sceneFilter.Id : (int?)null,
                    Level = _levelFilterIndex > 0 ? _levelFilterIndex : (int?)null
                };
                PageResult<EmergencyEventDto> page;
                // 「进行中」= 已发起 + 处置中；「已结案」= 已结案 + 已复盘：客户端合并两状态查询后统一分页
                if (_statusFilterIndex == 1) page = await QueryMergedAsync(query, EmergencyEventStatus.Initiated, EmergencyEventStatus.Handling);
                else if (_statusFilterIndex == 2) page = await QueryMergedAsync(query, EmergencyEventStatus.Closed, EmergencyEventStatus.Reviewed);
                else page = await Api.QueryEmergencyEventsAsync(query);

                Items.Clear();
                foreach (var dto in page.Items) Items.Add(new EmergencyEventRow { Dto = dto });
                Total = page.Total;
                TotalPages = PageSize > 0 ? (int)Math.Ceiling(page.Total / (double)PageSize) : 1;
                OnPropertyChanged(nameof(PageText));
                OnPropertyChanged(nameof(CanPrev));
                OnPropertyChanged(nameof(CanNext));
            }, "事件已加载");
        }

        /// <summary>多状态合并分页：每个状态取前 pageIndex*pageSize 条，合并按 Id 倒序后取当前页。</summary>
        private async Task<PageResult<EmergencyEventDto>> QueryMergedAsync(EmergencyEventQueryRequest template, params EmergencyEventStatus[] statuses)
        {
            int cap = Math.Min(500, Math.Max(PageSize, _pageIndex * PageSize));
            var merged = new List<EmergencyEventDto>();
            int total = 0;
            foreach (var st in statuses)
            {
                var req = new EmergencyEventQueryRequest
                {
                    PageIndex = 1, PageSize = cap, Keyword = template.Keyword,
                    Status = st, SceneId = template.SceneId, Level = template.Level
                };
                var part = await Api.QueryEmergencyEventsAsync(req);
                total += part.Total;
                merged.AddRange(part.Items);
            }
            merged = merged.OrderByDescending(x => x.Id).ToList();
            var pageItems = merged.Skip((_pageIndex - 1) * PageSize).Take(PageSize).ToList();
            return new PageResult<EmergencyEventDto> { PageIndex = _pageIndex, PageSize = PageSize, Total = total, Items = pageItems };
        }

        private void LaunchEvent()
        {
            if (_navigateToPage != null) _navigateToPage("应急发起");
            else StatusText = DateTime.Now.ToString("HH:mm:ss ") + "请从左侧导航打开「应急发起」页面";
        }

        // ===================== 处置详情（步骤 + 新增处置记录） =====================

        private async Task OpenHandleAsync(EmergencyEventRow row)
        {
            if (row == null) return;
            await LoadDetailAsync(row, true);
        }

        private async Task OpenViewAsync(EmergencyEventRow row)
        {
            if (row == null) return;
            await LoadDetailAsync(row, false);
        }

        private async Task LoadDetailAsync(EmergencyEventRow row, bool canRecord)
        {
            await RunAsync(async () =>
            {
                var detail = await Api.GetEmergencyEventAsync(row.Dto.Id);
                Detail = detail;
                DetailTitle = row.EventNo + " · " + row.SceneName;
                DetailRecords.Clear();
                if (detail.Records != null) foreach (var r in detail.Records) DetailRecords.Add(new EmergencyRecordRow { Dto = r });
                DetailAssignments.Clear();
                if (detail.Assignments != null) foreach (var a in detail.Assignments) DetailAssignments.Add(a);
                DetailReviewItems.Clear();
                if (detail.Review != null && detail.Review.Items != null)
                    foreach (var i in detail.Review.Items) DetailReviewItems.Add(new EmergencyReviewItemRow { Dto = i });
                RecordTime = DateTime.Now;
                RecordContent = string.Empty;
                RecordResult = string.Empty;
                OnPropertyChanged(nameof(DetailCanRecord));
                IsDetailVisible = true;
            }, null);
        }

        /// <summary>新增处置记录（BR-EMG-04：时间/操作/结果必填）。</summary>
        private async Task AddRecordAsync()
        {
            if (Detail == null || Detail.Event == null) return;
            if (string.IsNullOrWhiteSpace(RecordContent)) { ErrorText = "请填写操作内容（BR-EMG-04）"; return; }
            if (string.IsNullOrWhiteSpace(RecordResult)) { ErrorText = "请填写处置结果（BR-EMG-04：记录必含时间/操作/结果）"; return; }
            int eventId = Detail.Event.Id;
            await RunAsync(async () =>
            {
                // 已结案/已复盘事件打开详情时补录须带 IsSupplement=true（BR-EMG-06）
                bool supplement = !DetailCanRecord;
                await Api.AddEmergencyRecordAsync(eventId, new EmergencyRecordRequest
                {
                    EventId = eventId, RecordTime = RecordTime, Content = RecordContent.Trim(),
                    Result = RecordResult.Trim(), IsSupplement = supplement
                });
                RecordContent = string.Empty; RecordResult = string.Empty;
                await LoadDetailAsync(new EmergencyEventRow { Dto = Detail.Event }, true);
                await LoadAsync();
            }, "处置记录已保存");
        }

        // ===================== 结案（处置结果 + 物资消耗必填 + 二次确认） =====================

        private async Task OpenCloseAsync(EmergencyEventRow row)
        {
            if (row == null) return;
            _closeTarget = row;
            CloseSummary = string.Empty;
            IsCloseVisible = true;
        }

        private async Task ConfirmCloseAsync()
        {
            if (_closeTarget == null) return;
            if (string.IsNullOrWhiteSpace(CloseSummary)) { ErrorText = "结案必填处置结果与物资消耗（BR-EMG-02）"; return; }
            var target = _closeTarget;
            var answer = System.Windows.MessageBox.Show(
                "确认结案事件 " + target.EventNo + "？\n结案后事件归档，仅可补录处置记录与复盘。",
                "结案二次确认", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (answer != System.Windows.MessageBoxResult.Yes) return;
            await RunAsync(async () =>
            {
                await Api.CloseEmergencyAsync(target.Dto.Id, new EmergencyCloseRequest { EventId = target.Dto.Id, Summary = CloseSummary.Trim() });
                IsCloseVisible = false;
                _closeTarget = null;
                await LoadAsync();
            }, "事件已结案");
        }

        // ===================== 补录事件（选已结案事件 + IsSupplement=true） =====================

        private async Task OpenSupplementAsync()
        {
            await RunAsync(async () =>
            {
                SupplementCandidates.Clear();
                var closed = await Api.QueryEmergencyEventsAsync(new EmergencyEventQueryRequest
                { PageIndex = 1, PageSize = 100, Status = EmergencyEventStatus.Closed });
                foreach (var dto in closed.Items) SupplementCandidates.Add(new EmergencyEventRow { Dto = dto });
                var reviewed = await Api.QueryEmergencyEventsAsync(new EmergencyEventQueryRequest
                { PageIndex = 1, PageSize = 100, Status = EmergencyEventStatus.Reviewed });
                foreach (var dto in reviewed.Items) SupplementCandidates.Add(new EmergencyEventRow { Dto = dto });
                SupplementTarget = SupplementCandidates.FirstOrDefault();
                SupplementTime = DateTime.Now;
                SupplementContent = string.Empty;
                SupplementResult = string.Empty;
                IsSupplementVisible = true;
            }, null);
        }

        private async Task ConfirmSupplementAsync()
        {
            if (SupplementTarget == null) { ErrorText = "请选择已结案事件"; return; }
            if (string.IsNullOrWhiteSpace(SupplementContent)) { ErrorText = "请填写操作内容"; return; }
            if (string.IsNullOrWhiteSpace(SupplementResult)) { ErrorText = "请填写处置结果（BR-EMG-04）"; return; }
            var target = SupplementTarget;
            await RunAsync(async () =>
            {
                await Api.AddEmergencyRecordAsync(target.Dto.Id, new EmergencyRecordRequest
                {
                    EventId = target.Dto.Id, RecordTime = SupplementTime, Content = SupplementContent.Trim(),
                    Result = SupplementResult.Trim(), IsSupplement = true // 补录标记（BR-EMG-06）
                });
                IsSupplementVisible = false;
                await LoadAsync();
            }, "补录记录已保存");
        }

        private void CloseDialogs()
        {
            IsDetailVisible = false;
            IsCloseVisible = false;
            IsSupplementVisible = false;
        }
    }
}
