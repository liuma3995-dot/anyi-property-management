// ── 已下线（R4 变更）：应急处置子系统仅保留「场景与步骤维护」，复盘记录模块停止提供 ──
// 本文件为源码存档，已从 PropertyManagement.Client.csproj 移除，不参与编译；
// 恢复上线需同步恢复 ShellViewModel 导航、MainWindow DataTemplate 与工程项。
using System;
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
    /// <summary>复盘列表行（PG-EMG-04 左列 7 列）。</summary>
    public class EmergencyReviewListRow : ObservableObject
    {
        public EmergencyReviewDto Dto { get; set; }
        public string ReviewNo { get { return string.IsNullOrEmpty(Dto.ReviewNo) ? ("FP-" + Dto.Id.ToString("D4")) : Dto.ReviewNo; } }
        public string EventNo { get { return Dto.EventNo ?? ("EM-" + Dto.EventId); } }
        public string PlanDateText
        {
            get
            {
                if (!Dto.PlanDate.HasValue) return "—";
                // 待开展 → 「2026-08-30（计划）」口径（原型行 126）；其余为实际复盘日期
                string status = StatusText;
                return Dto.PlanDate.Value.ToString("yyyy-MM-dd") + (status == "待开展" ? "（计划）" : string.Empty);
            }
        }
        public string HostName { get { return string.IsNullOrWhiteSpace(Dto.HostName) ? "—" : Dto.HostName; } }
        public string ItemCountText { get { return (Dto.Items != null ? Dto.Items.Count : 0) + " 项"; } }
        public string CompletionText { get { return Dto.CompletionRate.HasValue ? Dto.CompletionRate.Value + "%" : "—"; } }
        public string StatusText { get { return Dto.StatusText ?? "待开展"; } }
    }

    /// <summary>复盘详情改进措施行（措施/责任人/期限/状态 + 逾期标红）。</summary>
    public class EmergencyReviewDetailItemRow : ObservableObject
    {
        public EmergencyReviewItemDto Dto { get; set; }
        public string Content { get { return Dto.Content ?? string.Empty; } }
        public string Owner { get { return string.IsNullOrWhiteSpace(Dto.Owner) ? "—" : Dto.Owner; } }
        public string DueText { get { return Dto.DueDate.HasValue ? Dto.DueDate.Value.ToString("MM-dd") : "—"; } }
        public string StatusText { get { return Dto.StatusText ?? (Dto.Status == 1 ? "进行中" : (Dto.Status == 2 ? "已完成" : "待开展")); } }
        /// <summary>改进项逾期（期限早于今天且未完成）→ 标红（原型行 251）。</summary>
        public bool IsOverdue { get { return Dto.DueDate.HasValue && Dto.DueDate.Value.Date < DateTime.Today && Dto.Status != 2; } }
    }

    /// <summary>新增复盘弹窗改进措施录入行（措施/责任人/期限）。</summary>
    public class EmergencyReviewFormItemRow : ObservableObject
    {
        private string _content = string.Empty;
        private string _owner = string.Empty;
        private DateTime? _dueDate;

        public string Content { get { return _content; } set { SetProperty(ref _content, value); } }
        public string Owner { get { return _owner; } set { SetProperty(ref _owner, value); } }
        public DateTime? DueDate { get { return _dueDate; } set { SetProperty(ref _dueDate, value); } }
    }

    /// <summary>复盘记录页（PG-EMG-04，UC-EMG-007，BR-EMG-02，P-03）。</summary>
    public class EmergencyReviewViewModel : BaseInfoPageViewModel
    {
        private EmergencyReviewListRow _selectedReview;
        private EmergencyReviewDto _detail;
        private string _detailTitle = string.Empty;
        private string _eventSummary = string.Empty;

        // 新增复盘弹窗
        private bool _isNewReviewVisible;
        private EmergencyEventRow _newReviewTarget;
        private string _newReviewHost = string.Empty;
        private DateTime _newReviewDate = DateTime.Today;
        private string _newReviewCause = string.Empty;

        public EmergencyReviewViewModel(IApiClient api) : base(api)
        {
            RefreshCommand = new AsyncRelayCommand(LoadAsync);
            OpenNewReviewCommand = new AsyncRelayCommand(OpenNewReviewAsync);
            CancelNewReviewCommand = new RelayCommand(() => IsNewReviewVisible = false);
            SubmitReviewCommand = new AsyncRelayCommand(SubmitReviewAsync);
            AddItemRowCommand = new RelayCommand(AddItemRow);
            RemoveItemRowCommand = new RelayCommand<EmergencyReviewFormItemRow>(RemoveItemRow);
            _ = LoadAsync();
        }

        public ObservableCollection<EmergencyReviewListRow> Reviews { get; } = new ObservableCollection<EmergencyReviewListRow>();
        public ObservableCollection<string> CauseBullets { get; } = new ObservableCollection<string>();
        public ObservableCollection<EmergencyReviewDetailItemRow> DetailItems { get; } = new ObservableCollection<EmergencyReviewDetailItemRow>();
        public ObservableCollection<EmergencyEventRow> ClosedEvents { get; } = new ObservableCollection<EmergencyEventRow>();
        public ObservableCollection<EmergencyReviewFormItemRow> FormItems { get; } = new ObservableCollection<EmergencyReviewFormItemRow>();

        public EmergencyReviewListRow SelectedReview
        {
            get { return _selectedReview; }
            set { if (SetProperty(ref _selectedReview, value)) { _ = LoadDetailAsync(); } }
        }

        public EmergencyReviewDto Detail { get { return _detail; } private set { SetProperty(ref _detail, value); } }
        /// <summary>详情标题「FP-2608-02 · 电梯困人复盘」（场景名无契约字段，用关联事件编号口径）。</summary>
        public string DetailTitle { get { return _detailTitle; } private set { SetProperty(ref _detailTitle, value); } }
        /// <summary>事件概要行（08-26 18:44 · 地点 · 描述）。</summary>
        public string EventSummary { get { return _eventSummary; } private set { SetProperty(ref _eventSummary, value); } }
        public bool HasDetail { get { return Detail != null; } }
        public System.Windows.Visibility HasDetailVisibility { get { return Detail != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; } }
        public System.Windows.Visibility NoDetailVisibility { get { return Detail != null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible; } }

        public bool IsNewReviewVisible { get { return _isNewReviewVisible; } set { SetProperty(ref _isNewReviewVisible, value); } }
        public EmergencyEventRow NewReviewTarget { get { return _newReviewTarget; } set { SetProperty(ref _newReviewTarget, value); } }
        public string NewReviewHost { get { return _newReviewHost; } set { SetProperty(ref _newReviewHost, value); } }
        public DateTime NewReviewDate { get { return _newReviewDate; } set { SetProperty(ref _newReviewDate, value); } }
        public string NewReviewCause { get { return _newReviewCause; } set { SetProperty(ref _newReviewCause, value); } }

        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand OpenNewReviewCommand { get; }
        public IRelayCommand CancelNewReviewCommand { get; }
        public IAsyncRelayCommand SubmitReviewCommand { get; }
        public IRelayCommand AddItemRowCommand { get; }
        public IRelayCommand<EmergencyReviewFormItemRow> RemoveItemRowCommand { get; }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var page = await Api.QueryEmergencyReviewsAsync(new PageRequest { PageIndex = 1, PageSize = 100 });
                Reviews.Clear();
                foreach (var dto in page.Items) Reviews.Add(new EmergencyReviewListRow { Dto = dto });
                var keep = SelectedReview;
                if (keep != null)
                {
                    var stillThere = Reviews.FirstOrDefault(x => x.Dto.Id == keep.Dto.Id);
                    if (stillThere != null)
                    {
                        SetProperty(ref _selectedReview, stillThere, nameof(SelectedReview));
                        OnPropertyChanged(nameof(SelectedReview));
                        await LoadDetailCoreAsync(stillThere);
                        return;
                    }
                }
                if (SelectedReview == null && Reviews.Count > 0) SelectedReview = Reviews[0];
            }, "复盘已加载");
        }

        private async Task LoadDetailAsync()
        {
            if (SelectedReview == null) return;
            await LoadDetailCoreAsync(SelectedReview);
        }

        /// <summary>详情 = 复盘记录（含改进措施）+ 关联事件概要。</summary>
        private async Task LoadDetailCoreAsync(EmergencyReviewListRow row)
        {
            await RunAsync(async () =>
            {
                var review = await Api.GetEmergencyReviewAsync(row.Dto.EventId);
                Detail = review;
                string eventNo = !string.IsNullOrEmpty(review.EventNo) ? review.EventNo : row.EventNo;
                DetailTitle = row.ReviewNo + " · " + eventNo + " 复盘";
                try
                {
                    var evt = await Api.GetEmergencyEventAsync(row.Dto.EventId);
                    if (evt != null && evt.Event != null)
                    {
                        EventSummary = evt.Event.EventTime.ToString("MM-dd HH:mm") + " · " + (evt.Event.Location ?? string.Empty)
                            + (string.IsNullOrWhiteSpace(evt.Event.DetailLocation) ? string.Empty : "（" + evt.Event.DetailLocation + "）")
                            + (string.IsNullOrWhiteSpace(evt.Event.Description) ? string.Empty : " · " + evt.Event.Description);
                    }
                }
                catch (Exception)
                {
                    // 事件概要拉取失败不阻塞复盘详情展示
                }
                CauseBullets.Clear();
                if (!string.IsNullOrWhiteSpace(review.Cause))
                {
                    foreach (var line in review.Cause.Replace("\r\n", "\n").Split('\n'))
                        if (!string.IsNullOrWhiteSpace(line)) CauseBullets.Add(line.Trim());
                    if (CauseBullets.Count == 0) CauseBullets.Add(review.Cause.Trim());
                }
                DetailItems.Clear();
                if (review.Items != null) foreach (var i in review.Items) DetailItems.Add(new EmergencyReviewDetailItemRow { Dto = i });
                OnPropertyChanged(nameof(HasDetail));
                OnPropertyChanged(nameof(HasDetailVisibility));
                OnPropertyChanged(nameof(NoDetailVisibility));
            }, null);
        }

        // ===================== 新增复盘（关联已结案事件，UC-EMG-007） =====================

        private async Task OpenNewReviewAsync()
        {
            await RunAsync(async () =>
            {
                ClosedEvents.Clear();
                var closed = await Api.QueryEmergencyEventsAsync(new EmergencyEventQueryRequest
                { PageIndex = 1, PageSize = 100, Status = EmergencyEventStatus.Closed });
                foreach (var dto in closed.Items) ClosedEvents.Add(new EmergencyEventRow { Dto = dto });
                NewReviewTarget = ClosedEvents.FirstOrDefault();
                NewReviewHost = string.Empty;
                NewReviewDate = DateTime.Today;
                NewReviewCause = string.Empty;
                FormItems.Clear();
                AddItemRow();
                IsNewReviewVisible = true;
            }, null);
        }

        private void AddItemRow()
        {
            FormItems.Add(new EmergencyReviewFormItemRow());
        }

        private void RemoveItemRow(EmergencyReviewFormItemRow row)
        {
            if (row != null) FormItems.Remove(row);
        }

        /// <summary>P0 修复：复盘必须选择已结案事件后提交（不再固定写第一个事件）。</summary>
        private async Task SubmitReviewAsync()
        {
            if (NewReviewTarget == null) { ErrorText = "请选择已结案事件"; return; }
            if (string.IsNullOrWhiteSpace(NewReviewCause)) { ErrorText = "请填写问题与根因"; return; }
            var items = FormItems.Where(i => i != null && !string.IsNullOrWhiteSpace(i.Content))
                .Select(i => new EmergencyReviewItemRequest { Content = i.Content.Trim(), Owner = i.Owner, DueDate = i.DueDate, Status = 0 })
                .ToList();
            var target = NewReviewTarget;
            await RunAsync(async () =>
            {
                await Api.ReviewEmergencyAsync(target.Dto.Id, new EmergencyReviewRequest
                {
                    EventId = target.Dto.Id,
                    Cause = NewReviewCause.Trim(),
                    HostName = string.IsNullOrWhiteSpace(NewReviewHost) ? null : NewReviewHost.Trim(),
                    PlanDate = NewReviewDate,
                    Items = items
                });
                IsNewReviewVisible = false;
                await LoadAsync();
            }, "复盘已提交");
        }
    }
}
