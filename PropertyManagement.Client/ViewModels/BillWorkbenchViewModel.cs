using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>账单批次行（PG-FIN-02，T4F-2-1 操作按状态派生）。</summary>
    public class BillBatchRow : ObservableObject
    {
        public BillBatchDto Dto { get; set; }

        public string StatusText
        {
            get
            {
                switch (Dto.Status)
                {
                    case "Draft": return "草稿";
                    case "Partial": return "部分缴纳";
                    case "Failed": return "发布失败";
                    default: return "已发布";
                }
            }
        }

        public Brush StatusBrush
        {
            get
            {
                switch (Dto.Status)
                {
                    case "Draft": return Br("#B76E00");
                    case "Partial": return Br("#7A5AF8");
                    case "Failed": return Br("#D64545");
                    default: return Br("#12805C");
                }
            }
        }

        public Brush StatusBg
        {
            get
            {
                switch (Dto.Status)
                {
                    case "Draft": return Br("#FFF5DC");
                    case "Partial": return Br("#F1EDFF");
                    case "Failed": return Br("#FDECEC");
                    default: return Br("#E8F7F1");
                }
            }
        }

        public string BatchNoText { get { return string.IsNullOrEmpty(Dto.BatchNo) ? "BILL-" + Dto.Id.ToString("D4") : Dto.BatchNo; } }

        public string ChargeItemName { get { return Dto.ChargeItemName ?? "—"; } }

        public string CyclePeriod { get { return Dto.CyclePeriod ?? "—"; } }

        public string HouseCountText { get { return Dto.HouseCount.ToString(); } }

        public string TotalAmountText { get { return "¥" + Dto.TotalAmount.ToString("N2"); } }

        public string PublishTimeText
        {
            get { return Dto.PublishedAt.HasValue ? Dto.PublishedAt.Value.ToString("yyyy-MM-dd HH:mm") : "—"; }
        }

        // ---- T4F-2-1：操作列按批次状态派生 ----
        public bool CanPublish { get { return Dto.Status == "Draft"; } }
        public bool CanEdit { get { return Dto.Status == "Draft"; } }
        public bool CanView { get { return Dto.Status == "Published" || Dto.Status == "Partial"; } }
        public bool CanRemind { get { return Dto.Status == "Partial"; } }
        public bool CanRetry { get { return Dto.Status == "Failed"; } }
        public bool CanShowFailures { get { return Dto.Status == "Failed"; } }
        /// <summary>删除可用：所有状态（草稿/已发布/部分缴/发布失败/已缴）均可删除（软删批次及其账单，保留操作轨迹）。</summary>
        public bool CanDelete { get { return true; } }

        private static Brush Br(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    /// <summary>账单工作台（PG-FIN-02，UC-FIN-002，FL-FIN-01，BR-FIN-01/03；T4F-2-1 统计卡/筛选/派生操作/发布确认）。</summary>
    public class BillWorkbenchViewModel : FinancePageViewModel
    {
        private const string AllPeriods = "全部期间";

        private readonly Action<string> _navigateToPage;

        private string _draftText = "0";
        private string _publishedText = "0";
        private string _publishedSubText = "本月 0 批次";
        private string _partialText = "0";
        private string _partialSubText = "欠缴 ¥0";
        private string _failedText = "0";
        private ChargeItemDto _selectedItem;
        private BillingCycleDto _selectedCycle;
        private bool _isFailurePanelVisible;
        private bool _isGenerateVisible;
        private string _failureTitle = string.Empty;
        private string _keyword = string.Empty;
        private string _periodFilter = AllPeriods;
        private int _statusFilter;
        private bool _inited;

        private bool _isPublishConfirmVisible;
        private string _publishConfirmText = string.Empty;
        private BillBatchRow _pendingPublish;

        private bool _isViewVisible;
        private BillBatchRow _viewBatchRow;
        private bool _isDeleteConfirmVisible;
        private string _deleteConfirmText = string.Empty;
        private BillBatchRow _pendingDelete;
        private DateTime? _newCycleStart;
        private DateTime? _newCycleEnd;

        private List<BillBatchDto> _allBatches = new List<BillBatchDto>();

        public BillWorkbenchViewModel(IApiClient api, Action<string> navigateToPage = null) : base(api)
        {
            _navigateToPage = navigateToPage;
            GenerateCommand = new AsyncRelayCommand(GenerateAsync);
            OpenGenerateCommand = new RelayCommand(() => IsGenerateVisible = true);
            CloseGenerateCommand = new RelayCommand(() => IsGenerateVisible = false);
            PublishCommand = new RelayCommand<BillBatchRow>(RequestPublish);
            ConfirmPublishCommand = new AsyncRelayCommand(ConfirmPublishAsync);
            CancelPublishCommand = new RelayCommand(() => IsPublishConfirmVisible = false);
            EditCommand = new RelayCommand<BillBatchRow>(EditBatch);
            ViewCommand = new RelayCommand<BillBatchRow>(ViewBatch);
            CloseViewCommand = new RelayCommand(() => IsViewVisible = false);
            RemindCommand = new RelayCommand<BillBatchRow>(RemindBatch);
            RetryCommand = new AsyncRelayCommand<BillBatchRow>(RetryAsync);
            ShowFailuresCommand = new RelayCommand<BillBatchRow>(ShowFailures);
            ShowAllFailuresCommand = new RelayCommand(ShowAllFailures);
            CloseFailuresCommand = new RelayCommand(() => IsFailurePanelVisible = false);
            AddCycleCommand = new AsyncRelayCommand(AddCycleAsync);
            DeleteCommand = new RelayCommand<BillBatchRow>(RequestDelete);
            ConfirmDeleteCommand = new AsyncRelayCommand(ConfirmDeleteAsync);
            CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirmVisible = false);
            _ = LoadAsync();
        }

        public ObservableCollection<ChargeItemDto> ChargeItems { get; } = new ObservableCollection<ChargeItemDto>();

        public ObservableCollection<BillingCycleDto> Cycles { get; } = new ObservableCollection<BillingCycleDto>();

        public ObservableCollection<BillBatchRow> Batches { get; } = new ObservableCollection<BillBatchRow>();

        public ObservableCollection<BillFailureDto> Failures { get; } = new ObservableCollection<BillFailureDto>();

        public ObservableCollection<string> PeriodFilterOptions { get; } = new ObservableCollection<string>();

        public string DraftText { get { return _draftText; } private set { SetProperty(ref _draftText, value); } }

        public string PublishedText { get { return _publishedText; } private set { SetProperty(ref _publishedText, value); } }

        public string PublishedSubText { get { return _publishedSubText; } private set { SetProperty(ref _publishedSubText, value); } }

        public string PartialText { get { return _partialText; } private set { SetProperty(ref _partialText, value); } }

        public string PartialSubText { get { return _partialSubText; } private set { SetProperty(ref _partialSubText, value); } }

        public string FailedText { get { return _failedText; } private set { SetProperty(ref _failedText, value); } }

        public bool IsFailurePanelVisible { get { return _isFailurePanelVisible; } private set { SetProperty(ref _isFailurePanelVisible, value); } }

        public bool IsGenerateVisible { get { return _isGenerateVisible; } private set { SetProperty(ref _isGenerateVisible, value); } }

        public string FailureTitle { get { return _failureTitle; } private set { SetProperty(ref _failureTitle, value); } }

        public string Keyword
        {
            get { return _keyword; }
            set
            {
                if (SetProperty(ref _keyword, value) && _inited)
                {
                    _ = RefreshBatchesAsync();
                }
            }
        }

        public string PeriodFilter
        {
            get { return _periodFilter; }
            set
            {
                if (SetProperty(ref _periodFilter, value) && _inited)
                {
                    _ = RefreshBatchesAsync();
                }
            }
        }

        public int StatusFilter
        {
            get { return _statusFilter; }
            set
            {
                if (SetProperty(ref _statusFilter, value) && _inited)
                {
                    _ = RefreshBatchesAsync();
                }
            }
        }

        public bool IsPublishConfirmVisible { get { return _isPublishConfirmVisible; } private set { SetProperty(ref _isPublishConfirmVisible, value); } }

        public string PublishConfirmText { get { return _publishConfirmText; } private set { SetProperty(ref _publishConfirmText, value); } }

        public bool IsViewVisible { get { return _isViewVisible; } private set { SetProperty(ref _isViewVisible, value); } }

        public BillBatchRow ViewBatchRow { get { return _viewBatchRow; } private set { SetProperty(ref _viewBatchRow, value); } }
        public bool IsDeleteConfirmVisible { get { return _isDeleteConfirmVisible; } private set { SetProperty(ref _isDeleteConfirmVisible, value); } }
        public string DeleteConfirmText { get { return _deleteConfirmText; } private set { SetProperty(ref _deleteConfirmText, value); } }

        public ChargeItemDto SelectedItem { get { return _selectedItem; } set { SetProperty(ref _selectedItem, value); } }

        public BillingCycleDto SelectedCycle { get { return _selectedCycle; } set { SetProperty(ref _selectedCycle, value); } }
        public DateTime? NewCycleStart { get { return _newCycleStart; } set { SetProperty(ref _newCycleStart, value); } }
        public DateTime? NewCycleEnd { get { return _newCycleEnd; } set { SetProperty(ref _newCycleEnd, value); } }

        public IAsyncRelayCommand GenerateCommand { get; }
        public IRelayCommand OpenGenerateCommand { get; }
        public IRelayCommand CloseGenerateCommand { get; }
        public IRelayCommand<BillBatchRow> PublishCommand { get; }
        public IAsyncRelayCommand ConfirmPublishCommand { get; }
        public IRelayCommand CancelPublishCommand { get; }
        public IRelayCommand<BillBatchRow> EditCommand { get; }
        public IRelayCommand<BillBatchRow> ViewCommand { get; }
        public IRelayCommand CloseViewCommand { get; }
        public IRelayCommand<BillBatchRow> RemindCommand { get; }
        public IAsyncRelayCommand<BillBatchRow> RetryCommand { get; }
        public IRelayCommand<BillBatchRow> ShowFailuresCommand { get; }
        public IRelayCommand ShowAllFailuresCommand { get; }
        public IRelayCommand CloseFailuresCommand { get; }
        public IAsyncRelayCommand AddCycleCommand { get; }
        public IRelayCommand<BillBatchRow> DeleteCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteCommand { get; }
        public IRelayCommand CancelDeleteCommand { get; }

        private async Task ReloadCyclesAsync(BillingCycleDto preferred = null)
        {
            var cycles = await Api.GetCyclesAsync();
            Cycles.Clear();
            foreach (var cycle in cycles.OrderByDescending(x => x.EndDate))
            {
                Cycles.Add(cycle);
            }
            SelectedCycle = preferred != null
                ? Cycles.FirstOrDefault(x => x.Id == preferred.Id)
                : Cycles.FirstOrDefault();
        }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var items = await Api.GetChargeItemsAsync();
                ChargeItems.Clear();
                foreach (var item in items.Where(x => x.Status == 0))
                {
                    ChargeItems.Add(item);
                }
                SelectedItem = ChargeItems.FirstOrDefault();

                await ReloadCyclesAsync();

                await RefreshBatchesAsync();
                await RefreshSummaryAsync();
                _inited = true;
            }, "账单工作台已加载");
        }

        private async Task RefreshBatchesAsync()
        {
            var batches = await Api.GetGenerateLogsAsync();
            _allBatches = batches.ToList();

            // 期间选项（去重，保留当前选择）
            RefreshPeriodOptions();

            Batches.Clear();
            var query = _allBatches.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(Keyword))
            {
                string kw = Keyword.Trim();
                query = query.Where(x =>
                    (x.BatchNo ?? string.Empty).Contains(kw) ||
                    (x.ChargeItemName ?? string.Empty).Contains(kw));
            }
            if (!string.Equals(PeriodFilter, AllPeriods, StringComparison.Ordinal))
            {
                query = query.Where(x => string.Equals(x.CyclePeriod, PeriodFilter, StringComparison.Ordinal));
            }
            switch (StatusFilter)
            {
                case 1: query = query.Where(x => x.Status == "Draft"); break;
                case 2: query = query.Where(x => x.Status == "Published"); break;
                case 3: query = query.Where(x => x.Status == "Partial"); break;
                case 4: query = query.Where(x => x.Status == "Failed"); break;
            }
            foreach (var dto in query.OrderByDescending(x => x.Id))
            {
                Batches.Add(new BillBatchRow { Dto = dto });
            }
        }

        private void RefreshPeriodOptions()
        {
            // T4R-2：就地增删选项，避免 Clear() 触发 ComboBox 选中被重置（期间筛选失效）
            string current = PeriodFilter;
            var desired = new List<string> { AllPeriods };
            desired.AddRange(_allBatches
                .Select(x => x.CyclePeriod)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .OrderByDescending(x => x));

            for (int i = PeriodFilterOptions.Count - 1; i >= 0; i--)
            {
                if (!desired.Contains(PeriodFilterOptions[i]))
                {
                    PeriodFilterOptions.RemoveAt(i);
                }
            }
            int insertPos = 0;
            foreach (string item in desired)
            {
                if (!PeriodFilterOptions.Contains(item))
                {
                    if (insertPos >= PeriodFilterOptions.Count) { PeriodFilterOptions.Add(item); }
                    else { PeriodFilterOptions.Insert(insertPos, item); }
                }
                insertPos++;
            }

            if (current != null && !PeriodFilterOptions.Contains(current))
            {
                _periodFilter = AllPeriods;
                OnPropertyChanged(nameof(PeriodFilter));
            }
        }

        private async Task RefreshSummaryAsync()
        {
            var draft = await Api.QueryBillsAsync(new BillQueryRequest { Status = BillStatus.Draft, PageSize = 1 });
            DraftText = draft.Total.ToString();

            int published = 0;
            foreach (BillStatus status in new[] { BillStatus.Pending, BillStatus.Partial, BillStatus.Overdue, BillStatus.Paid })
            {
                var page = await Api.QueryBillsAsync(new BillQueryRequest { Status = status, PageSize = 1 });
                published += page.Total;
            }
            PublishedText = published.ToString();
            int publishedBatches = _allBatches.Count(x => x.Status == "Published" || x.Status == "Partial");
            PublishedSubText = "本月 " + publishedBatches + " 批次";

            var partial = await Api.QueryBillsAsync(new BillQueryRequest { Status = BillStatus.Partial, PageSize = 1 });
            PartialText = partial.Total.ToString();
            var stats = await Api.GetPaymentStatisticsAsync();
            PartialSubText = "欠缴 ¥" + stats.UnpaidAmount.ToString("N0");

            int failed = _allBatches.Count(x => x.Status == "Failed");
            FailedText = failed.ToString();
        }

        private async Task GenerateAsync()
        {
            if (SelectedItem == null)
            {
                ErrorText = "请先选择收费项目";
                return;
            }
            if ((NewCycleStart.HasValue && !NewCycleEnd.HasValue) || (!NewCycleStart.HasValue && NewCycleEnd.HasValue))
            {
                ErrorText = "自定义周期需同时填写开始与结束日期";
                return;
            }
            if (NewCycleStart.HasValue && NewCycleEnd.HasValue && NewCycleEnd.Value < NewCycleStart.Value)
            {
                ErrorText = "周期结束日期不能早于开始日期";
                return;
            }
            await RunAsync(async () =>
            {
                if (NewCycleStart.HasValue && NewCycleEnd.HasValue)
                {
                    var created = await Api.CreateCycleAsync(new BillingCycleRequest
                    {
                        CycleType = BillingCycleType.Custom,
                        StartDate = NewCycleStart.Value,
                        EndDate = NewCycleEnd.Value
                    });
                    NewCycleStart = null;
                    NewCycleEnd = null;
                    await ReloadCyclesAsync(created);
                }
                if (SelectedCycle == null)
                {
                    throw new ApiClientException(422, "请先选择计费周期，或填写自定义周期日期后重新生成");
                }
                var log = await Api.GenerateBillsAsync(new BillGenerateRequest
                {
                    ChargeItemId = SelectedItem.Id,
                    CycleId = SelectedCycle.Id,
                    PropertyIds = new List<int>(),
                    ParkingIds = new List<int>()
                });
                await RefreshBatchesAsync();
                await RefreshSummaryAsync();
                IsGenerateVisible = false;
                if (log.Fail > 0)
                {
                    ErrorText = "生成完成：成功 " + log.Success + " 户，失败 " + log.Fail + " 户（见失败清单，BR-FIN-01）";
                }
                else
                {
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + "生成草稿 " + log.Success + " 户，等待发布";
                }
            }, null);
        }

        /// <summary>新增自定义计费周期（T4F-3-1 补充：生成账单周期不固定，可选任意起止日期）。</summary>
        private async Task AddCycleAsync()
        {
            if (!NewCycleStart.HasValue || !NewCycleEnd.HasValue)
            {
                ErrorText = "请选择自定义周期的开始与结束日期";
                return;
            }
            if (NewCycleEnd.Value < NewCycleStart.Value)
            {
                ErrorText = "周期结束日期不能早于开始日期";
                return;
            }
            await RunAsync(async () =>
            {
                var created = await Api.CreateCycleAsync(new BillingCycleRequest
                {
                    CycleType = BillingCycleType.Custom,
                    StartDate = NewCycleStart.Value,
                    EndDate = NewCycleEnd.Value
                });
                NewCycleStart = null;
                NewCycleEnd = null;
                await ReloadCyclesAsync(created);
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "自定义周期已添加并选中，可生成草稿";
            }, null);
        }
        /// <summary>T4F-2-1：发布前确认（文案对齐原型）。</summary>
        private void RequestPublish(BillBatchRow row)
        {
            if (row == null || !row.CanPublish) { return; }
            _pendingPublish = row;
            PublishConfirmText = "将向 " + row.Dto.HouseCount + " 户生成账单，确认发布？";
            IsPublishConfirmVisible = true;
        }

        private async Task ConfirmPublishAsync()
        {
            BillBatchRow row = _pendingPublish;
            IsPublishConfirmVisible = false;
            _pendingPublish = null;
            if (row == null) { return; }
            await RunAsync(async () =>
            {
                var log = await Api.PublishBillsAsync(new BillPublishRequest { BatchId = row.Dto.Id });
                await RefreshBatchesAsync();
                await RefreshSummaryAsync();
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "批次 " + log.Id + " 已发布（FL-FIN-01）";
            }, null);
        }

        /// <summary>T4F-2-1：草稿批次编辑（回填收费项目/周期后重新生成失败清单）。</summary>
        private void EditBatch(BillBatchRow row)
        {
            if (row == null || !row.CanEdit) { return; }
            if (row.Dto.ChargeItemId.HasValue)
            {
                SelectedItem = ChargeItems.FirstOrDefault(x => x.Id == row.Dto.ChargeItemId.Value) ?? SelectedItem;
            }
            if (row.Dto.CycleId.HasValue)
            {
                SelectedCycle = Cycles.FirstOrDefault(x => x.Id == row.Dto.CycleId.Value) ?? SelectedCycle;
            }
            IsGenerateVisible = true;
            StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已载入草稿批次 " + row.BatchNoText + "，可重选收费项目/周期后重新生成";
        }

        private void ViewBatch(BillBatchRow row)
        {
            if (row == null) { return; }
            ViewBatchRow = row;
            IsViewVisible = true;
        }

        /// <summary>T4F-2-1：部分缴纳批次催缴 → 跳转欠费台账。</summary>
        private void RemindBatch(BillBatchRow row)
        {
            if (row == null || !row.CanRemind) { return; }
            StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已跳转欠费台账，可对部分缴纳户执行催缴";
            if (_navigateToPage != null)
            {
                _navigateToPage("欠费台账");
            }
        }

        /// <summary>删除任意状态批次（CHG-M4-16 修订：软删批次及其账单，保留操作轨迹；已缴/部分缴流水不回退）。</summary>
        private void RequestDelete(BillBatchRow row)
        {
            if (row == null || !row.CanDelete) { return; }
            _pendingDelete = row;
            DeleteConfirmText = "将删除批次 " + row.BatchNoText + "（" + row.ChargeItemName + "，户数 " + row.HouseCountText + "）。删除将连同批次下账单一并软删并保留操作轨迹；已缴/部分缴金额对应流水不做回退，请谨慎操作。确认删除？";
            IsDeleteConfirmVisible = true;
        }

        private async Task ConfirmDeleteAsync()
        {
            BillBatchRow row = _pendingDelete;
            IsDeleteConfirmVisible = false;
            _pendingDelete = null;
            if (row == null) { return; }
            await RunAsync(async () =>
            {
                await Api.DeleteBillBatchAsync(row.Dto.Id);
                await RefreshBatchesAsync();
                await RefreshSummaryAsync();
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "批次 " + row.BatchNoText + " 已删除（软删，轨迹保留）";
            }, null);
        }
        private async Task RetryAsync(BillBatchRow row)
        {
            if (row == null) { return; }
            await RunAsync(async () =>
            {
                var log = await Api.RetryFailuresAsync(row.Dto.Id);
                await RefreshBatchesAsync();
                await RefreshSummaryAsync();
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "失败批次已重推（FL-FIN-01）";
            }, null);
        }

        private void ShowAllFailures()
        {
            var failed = Batches.FirstOrDefault(x => x.Dto.Status == "Failed");
            if (failed == null)
            {
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "当前没有发布失败的批次";
                return;
            }
            ShowFailures(failed);
        }

        private async void ShowFailures(BillBatchRow row)
        {
            if (row == null) { return; }
            FailureTitle = "失败清单 · " + row.Dto.BatchNo;
            Failures.Clear();
            try
            {
                var list = await Api.GetFailuresAsync(row.Dto.Id);
                foreach (var f in list)
                {
                    Failures.Add(f);
                }
                IsFailurePanelVisible = true;
            }
            catch (ApiClientException ex)
            {
                ErrorText = ex.Message;
            }
        }
    }
}
