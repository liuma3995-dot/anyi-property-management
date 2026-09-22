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
        private bool _isChecked;
        public BillBatchDto Dto { get; set; }
        /// <summary>批量选择标记。</summary>
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }

        public string StatusText
        {
            get
            {
                switch (Dto.Status)
                {
                    case "Draft": return "草稿";
                    case "Partial": return "部分缴纳";
                    case "Failed": return "发布失败";
                    case "Retried": return "已重推";
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
                    case "Retried": return Br("#12805C");
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
                    case "Retried": return Br("#E9F0EE");
                    default: return Br("#E8F7F1");
                }
            }
        }

        public string BatchNoText { get { return string.IsNullOrEmpty(Dto.BatchNo) ? "BILL-" + Dto.Id.ToString("D4") : Dto.BatchNo; } }

        public string ChargeItemName { get { return Dto.ChargeItemName ?? "—"; } }

        public string CyclePeriod { get { return Dto.CyclePeriod ?? "—"; } }

        /// <summary>
        /// CHG-v1.1.0-23：账单期间列显示文本 —— 同年省略结束年份、去掉分隔空格
        /// （`2026-09-01 ~ 2026-09-30` → `2026-09-01~09-30`），避免 110px 列宽下日期被省略号裁切；
        /// 跨年保持两侧年份，完整期间见悬停提示 <see cref="CyclePeriodFull"/>。
        /// </summary>
        public string CyclePeriodText
        {
            get
            {
                string raw = CyclePeriod;
                if (string.IsNullOrWhiteSpace(raw) || raw == "—") { return "—"; }
                string compact = raw.Replace(" ", string.Empty);
                string[] parts = compact.Split('~');
                if (parts.Length == 2 && parts[0].Length >= 10 && parts[1].Length >= 10 &&
                    parts[0].Substring(0, 4) == parts[1].Substring(0, 4))
                {
                    return parts[0] + "~" + parts[1].Substring(5);
                }
                return compact;
            }
        }

        /// <summary>CHG-v1.1.0-23：完整账单期间（悬停提示，避免紧凑显示丢失信息）。</summary>
        public string CyclePeriodFull { get { return CyclePeriod; } }

        public string HouseCountText { get { return Dto.HouseCount.ToString(); } }

        public string TotalAmountText { get { return "¥" + Dto.TotalAmount.ToString("N2"); } }

        public string PublishTimeText
        {
            get { return Dto.PublishedAt.HasValue ? Dto.PublishedAt.Value.ToString("yyyy-MM-dd HH:mm") : "—"; }
        }

        /// <summary>CHG-v1.1.0-10：本次生成范围摘要（历史批次无该字段时显示 —）。</summary>
        public string ScopeSummaryText
        {
            get { return string.IsNullOrWhiteSpace(Dto.ScopeSummary) ? "—" : Dto.ScopeSummary; }
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

    /// <summary>
    /// 缴费对象候选行（CHG-v1.1.0-10：生成账单选择器）。
    /// 只承载「显示 + 勾选」，不认识收费项目与计费周期。
    /// </summary>
    public partial class BillObjectRow : ObservableObject
    {
        private bool _isChecked;

        public BillObjectCandidateDto Dto { get; set; }

        /// <summary>勾选标记（单选／多选／全选均由该标记承载）。</summary>
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }

        public string No { get { return Dto == null ? string.Empty : Dto.No; } }

        public string SubText { get { return Dto == null ? string.Empty : Dto.SubText; } }

        public string OwnerText
        {
            get
            {
                if (Dto == null) { return "—"; }
                // CHG-v1.1.0-11：业主直缴行本身即缴费人，避免与「缴费对象」列重复展示姓名
                if (string.Equals(Dto.Kind, "owner", StringComparison.OrdinalIgnoreCase)) { return "本人"; }
                if (Dto.NoOwner) { return "未绑定业主"; }
                return string.IsNullOrWhiteSpace(Dto.OwnerName) ? "—" : Dto.OwnerName;
            }
        }

        public Brush OwnerBrush
        {
            get
            {
                return Dto != null && Dto.NoOwner
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D64545"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A6B7B"));
            }
        }
    }

    /// <summary>
    /// 自定义缴费对象手工填写行（CHG-v1.1.0-18）：租户/广告商/外部单位等无档案对象，
    /// 用户在生成账单表单中直接填写名称，一行生成一张账单。
    /// </summary>
    public partial class BillCustomPayerRow : ObservableObject
    {
        private string _name = string.Empty;

        /// <summary>缴费对象名称（必填）。</summary>
        public string Name { get { return _name; } set { SetProperty(ref _name, value); } }
    }

    /// <summary>账单工作台（PG-FIN-02，UC-FIN-002，FL-FIN-01，BR-FIN-01/03；T4F-2-1 统计卡/筛选/派生操作/发布确认）。</summary>
    public partial class BillWorkbenchViewModel : FinancePageViewModel
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
        private string _deleteConfirmTitle = "确认删除批次";
        private BillBatchRow _pendingDelete;
        /// <summary>CHG-v1.1.0-17：待删除的计费周期（与批次删除复用同一确认弹窗）。</summary>
        private BillingCycleDto _pendingCycleDelete;
        private List<BillBatchRow> _batchDeleteRows;
        private bool _isSelectAll;
        private DateTime? _newCycleStart;
        private DateTime? _newCycleEnd;

        // ---- CHG-v1.1.0-10：生成账单「缴费对象」选择器（默认不预选，全选作用域＝当前搜索结果） ----
        private int _objectKindIndex;
        private string _objectKeyword = string.Empty;
        private string _hiddenOwnerText = string.Empty;
        private string _objectListHint = string.Empty;
        private bool _isObjectLoading;
        private int _objectQuerySeq;
        /// <summary>
        /// CHG-v1.1.0-17：已勾选缴费对象的键集合（kind:id）。
        /// 勾选不再依赖「当前搜索结果行是否还在列表里」——搜索/清空关键字、切换筛选后勾选状态保持，
        /// 只有用户显式取消勾选（或重新打开生成弹窗）才会清除。
        /// </summary>
        private readonly HashSet<string> _checkedObjectKeys = new HashSet<string>();

        private List<BillBatchDto> _allBatches = new List<BillBatchDto>();

        public BillWorkbenchViewModel(IApiClient api, Action<string> navigateToPage = null) : base(api)
        {
            _navigateToPage = navigateToPage;
            // CHG-v1.1.2-26：出账预演（规格自动匹配 + 金额试算）与自定义对象计量卡
            InitBillingPreview();
            GenerateCommand = new AsyncRelayCommand(GenerateAsync);
            OpenGenerateCommand = new AsyncRelayCommand(OpenGenerateAsync);
            CloseGenerateCommand = new RelayCommand(() => IsGenerateVisible = false);
            PublishCommand = new RelayCommand<BillBatchRow>(RequestPublish);
            ConfirmPublishCommand = new AsyncRelayCommand(ConfirmPublishAsync);
            CancelPublishCommand = new RelayCommand(() => IsPublishConfirmVisible = false);
            EditCommand = new AsyncRelayCommand<BillBatchRow>(EditBatchAsync);
            ViewCommand = new RelayCommand<BillBatchRow>(ViewBatch);
            CloseViewCommand = new RelayCommand(() => IsViewVisible = false);
            RemindCommand = new RelayCommand<BillBatchRow>(RemindBatch);
            RetryCommand = new AsyncRelayCommand<BillBatchRow>(RetryAsync);
            RetryAllFailuresCommand = new AsyncRelayCommand(RetryAllFailuresAsync);
            ShowFailuresCommand = new RelayCommand<BillBatchRow>(ShowFailures);
            ShowAllFailuresCommand = new RelayCommand(ShowAllFailures);
            CloseFailuresCommand = new RelayCommand(() => IsFailurePanelVisible = false);
            AddCycleCommand = new AsyncRelayCommand(AddCycleAsync);
            DeleteCommand = new RelayCommand<BillBatchRow>(RequestDelete);
            ConfirmDeleteCommand = new AsyncRelayCommand(ConfirmDeleteAsync);
            CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirmVisible = false);
            BatchDeleteCommand = new RelayCommand(RequestBatchDelete);
            DeleteCycleCommand = new RelayCommand(RequestDeleteCycle);
            AddCustomPayerCommand = new RelayCommand(AddCustomPayerRow);
            RemoveCustomPayerCommand = new RelayCommand<BillCustomPayerRow>(RemoveCustomPayerRow);
            _ = LoadAsync();
        }

        public ObservableCollection<ChargeItemDto> ChargeItems { get; } = new ObservableCollection<ChargeItemDto>();

        public ObservableCollection<BillingCycleDto> Cycles { get; } = new ObservableCollection<BillingCycleDto>();

        public ObservableCollection<BillBatchRow> Batches { get; } = new ObservableCollection<BillBatchRow>();

        public ObservableCollection<BillFailureDto> Failures { get; } = new ObservableCollection<BillFailureDto>();

        public ObservableCollection<string> PeriodFilterOptions { get; } = new ObservableCollection<string>();

        /// <summary>缴费对象候选（CHG-v1.1.0-10）。</summary>
        public ObservableCollection<BillObjectRow> BillObjects { get; } = new ObservableCollection<BillObjectRow>();

        /// <summary>CHG-v1.1.0-18：自定义缴费对象手工填写行（一行一张账单）。</summary>
        public ObservableCollection<BillCustomPayerRow> CustomPayers { get; } = new ObservableCollection<BillCustomPayerRow>();

        public string DraftText { get { return _draftText; } private set { SetProperty(ref _draftText, value); } }

        public string PublishedText { get { return _publishedText; } private set { SetProperty(ref _publishedText, value); } }

        public string PublishedSubText { get { return _publishedSubText; } private set { SetProperty(ref _publishedSubText, value); } }

        public string PartialText { get { return _partialText; } private set { SetProperty(ref _partialText, value); } }

        public string PartialSubText { get { return _partialSubText; } private set { SetProperty(ref _partialSubText, value); } }

        public string FailedText { get { return _failedText; } private set { SetProperty(ref _failedText, value); } }

        /// <summary>CHG-v1.1.0-21：是否存在发布失败批次（控制「一键重推」入口显隐）。</summary>
        public bool HasFailedBatches
        {
            get { return _allBatches.Any(x => string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase)); }
        }

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
        /// <summary>CHG-v1.1.0-17：删除确认弹窗标题（批次/周期共用同一弹窗）。</summary>
        public string DeleteConfirmTitle { get { return _deleteConfirmTitle; } private set { SetProperty(ref _deleteConfirmTitle, value); } }

        public ChargeItemDto SelectedItem
        {
            get { return _selectedItem; }
            set
            {
                if (SetProperty(ref _selectedItem, value) && IsGenerateVisible)
                {
                    ApplyDefaultObjectKind();
                    _ = ReloadObjectsAndMeasuresAsync();
                }
            }
        }

        public BillingCycleDto SelectedCycle { get { return _selectedCycle; } set { SetProperty(ref _selectedCycle, value); } }
        public DateTime? NewCycleStart { get { return _newCycleStart; } set { SetProperty(ref _newCycleStart, value); } }
        public DateTime? NewCycleEnd { get { return _newCycleEnd; } set { SetProperty(ref _newCycleEnd, value); } }

        // ---------- CHG-v1.1.0-10：缴费对象选择器 ----------

        /// <summary>缴费对象类型：0 房产（默认）／1 车位／2 业主（直缴：办卡费/清理费/维修费等）。仅代表本次出账范围，不写回收费项目。</summary>
        public int ObjectKindIndex
        {
            get { return _objectKindIndex; }
            set
            {
                if (SetProperty(ref _objectKindIndex, value) && IsGenerateVisible)
                {
                    _ = ReloadBillObjectsAsync();
                }
                // CHG-v1.1.2-49：搜索框引导提示随「缴费对象」类型变化（房产/车位/业主各给对应提示）
                OnPropertyChanged(nameof(ObjectSearchPlaceholder));
            }
        }

        /// <summary>
        /// CHG-v1.1.2-49：缴费对象搜索框的引导提示 —— 按当前选择的缴费对象类型给出对应口径，
        /// 不再三种对象共用一句「房产搜楼栋/房号 · 车位搜编号 · 业主搜姓名/手机」。
        /// </summary>
        public string ObjectSearchPlaceholder
        {
            get
            {
                switch (_objectKindIndex)
                {
                    case 1: return "搜索车位编号，如 B1-001";
                    case 2: return "搜索业主姓名 / 手机号（同名业主看楼栋/房号）";
                    default: return "搜索楼栋 / 房号，如 1栋101";
                }
            }
        }

        /// <summary>服务端候选类型标识。</summary>
        public string ObjectKind
        {
            get
            {
                switch (_objectKindIndex)
                {
                    case 1: return "parking";
                    case 2: return "owner";
                    default: return "property";
                }
            }
        }

        /// <summary>候选搜索关键字（楼栋／单元／房号／车位编号；回车或输入即刷新）。</summary>
        public string ObjectKeyword
        {
            get { return _objectKeyword; }
            set
            {
                if (SetProperty(ref _objectKeyword, value) && IsGenerateVisible)
                {
                    _ = ReloadBillObjectsAsync();
                }
            }
        }

        /// <summary>全选：作用域为「当前搜索结果」，不会波及未显示的对象；勾选结果写入勾选集合（跨搜索保持）。</summary>
        public bool IsAllObjectsChecked
        {
            get { return BillObjects.Count > 0 && BillObjects.All(x => x.IsChecked); }
            set
            {
                foreach (BillObjectRow row in BillObjects) { row.IsChecked = value; }
                NotifyObjectSelectionChanged();
            }
        }

        public string SelectedObjectCountText
        {
            get { return "已选 " + SelectedObjectCount + " 个 / 当前结果 " + BillObjects.Count + " 个"; }
        }

        /// <summary>CHG-v1.1.0-17：当前缴费对象类型下已勾选数量（含被关键字过滤掉但已勾选的对象）。</summary>
        public int SelectedObjectCount
        {
            get
            {
                string prefix = ObjectKind + ":";
                return _checkedObjectKeys.Count(k => k.StartsWith(prefix, StringComparison.Ordinal));
            }
        }

        public bool HasSelectedObjects { get { return SelectedObjectCount > 0; } }

        /// <summary>CHG-v1.1.0-17：所选收费项目是否为自定义缴费对象（无基础信息档案，暂不支持批量出账）。</summary>
        public bool IsCustomChargeObject
        {
            get { return SelectedItem != null && SelectedItem.ObjectType == ChargeObjectType.Custom; }
        }

        /// <summary>CHG-v1.1.0-18：非自定义缴费对象（房产/车位/业主）——走候选勾选口径。</summary>
        public bool IsStandardChargeObject { get { return !IsCustomChargeObject; } }

        /// <summary>CHG-v1.1.0-17：自定义缴费对象显示名。</summary>
        public string CustomObjectName
        {
            get
            {
                if (SelectedItem == null) { return string.Empty; }
                return string.IsNullOrWhiteSpace(SelectedItem.ObjectName) ? "自定义" : SelectedItem.ObjectName.Trim();
            }
        }

        /// <summary>CHG-v1.1.0-18：自定义缴费对象的手工填写提示。</summary>
        private string CustomObjectHintMessage
        {
            get
            {
                return "收费项目「" + (SelectedItem == null ? string.Empty : SelectedItem.Name) +
                    "」的缴费对象为自定义（" + CustomObjectName +
                    "）：请在下方表格中手工填写缴费对象名称（一行一张账单），填写后生成草稿并发布";
            }
        }

        /// <summary>房产口径：未绑定有效业主被隐藏的数量提示（车位口径恒为空）。</summary>
        public string HiddenOwnerText { get { return _hiddenOwnerText; } private set { SetProperty(ref _hiddenOwnerText, value); } }

        /// <summary>结果截断等提示。</summary>
        public string ObjectListHint { get { return _objectListHint; } private set { SetProperty(ref _objectListHint, value); } }

        public bool IsObjectLoading { get { return _isObjectLoading; } private set { SetProperty(ref _isObjectLoading, value); } }

        public IAsyncRelayCommand GenerateCommand { get; }
        public IAsyncRelayCommand OpenGenerateCommand { get; }
        public IRelayCommand CloseGenerateCommand { get; }
        public IRelayCommand<BillBatchRow> PublishCommand { get; }
        public IAsyncRelayCommand ConfirmPublishCommand { get; }
        public IRelayCommand CancelPublishCommand { get; }

        /// <summary>
        /// CHG-v1.2.0-23：账单数据发生变化（发布 / 删除 / 重推）→ 通知外层刷新顶部铃铛待办数。
        /// 待办中心的「欠费催缴」直接由 t_bill 派生，出账发布后必须即时同步，否则要手动点铃铛才更新。
        /// </summary>
        public event Action DataChanged;

        private void RaiseDataChanged()
        {
            Action handler = DataChanged;
            if (handler != null)
            {
                handler();
            }
        }
        public IAsyncRelayCommand<BillBatchRow> EditCommand { get; }
        public IRelayCommand<BillBatchRow> ViewCommand { get; }
        public IRelayCommand CloseViewCommand { get; }
        public IRelayCommand<BillBatchRow> RemindCommand { get; }
        public IAsyncRelayCommand<BillBatchRow> RetryCommand { get; }
        /// <summary>CHG-v1.1.0-21：顶部「发布失败」卡片一键重推全部失败批次（FL-FIN-01）。</summary>
        public IAsyncRelayCommand RetryAllFailuresCommand { get; }
        public IRelayCommand<BillBatchRow> ShowFailuresCommand { get; }
        public IRelayCommand ShowAllFailuresCommand { get; }
        public IRelayCommand CloseFailuresCommand { get; }
        public IAsyncRelayCommand AddCycleCommand { get; }
        public IRelayCommand<BillBatchRow> DeleteCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteCommand { get; }
        public IRelayCommand CancelDeleteCommand { get; }
        public IRelayCommand BatchDeleteCommand { get; }
        /// <summary>CHG-v1.1.0-17：删除当前选中的自定义计费周期（删除后从下拉框消失）。</summary>
        public IRelayCommand DeleteCycleCommand { get; }
        /// <summary>CHG-v1.1.0-18：自定义缴费对象手工填写行 —— 新增 / 删除。</summary>
        public IRelayCommand AddCustomPayerCommand { get; }
        public IRelayCommand<BillCustomPayerRow> RemoveCustomPayerCommand { get; }

        /// <summary>全选：勾选/取消勾选当前批次列表全部行。</summary>
        public bool IsSelectAll
        {
            get { return _isSelectAll; }
            set
            {
                if (SetProperty(ref _isSelectAll, value))
                {
                    foreach (var r in Batches) { r.IsChecked = value; }
                }
            }
        }

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
            _isSelectAll = false;
            OnPropertyChanged(nameof(IsSelectAll));
            // CHG-v1.1.0-21：失败批次集合变化 → 同步「一键重推」入口与统计卡
            OnPropertyChanged(nameof(HasFailedBatches));
            FailedText = _allBatches.Count(x => string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase)).ToString();
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

        /// <summary>
        /// CHG-v1.1.0-10：打开生成账单弹窗——每次进入都是干净状态
        /// （关键字与勾选清空，「清空选择」由关闭弹窗重新进入代替），并加载当前类型候选。
        /// </summary>
        private async Task OpenGenerateAsync()
        {
            // CHG-v1.1.0-17：每次进入生成弹窗都是干净状态（勾选集合一并清空）
            _checkedObjectKeys.Clear();
            CustomPayers.Clear();   // CHG-v1.1.0-18：自定义缴费对象手工行同样清空
            ClearObjectMeasures();  // CHG-v1.1.2-34：档案对象的行内手填计量参数同样清空
            ClearPriceOverrides();  // CHG-v1.1.2-50：出账改价同样清空（每次进入弹窗从价目表单价起步）
            ClearSpecPicks();       // CHG-v1.2.0-12：手选规格同样清空（每次进入弹窗从「自动匹配」起步）
            _objectKeyword = string.Empty;
            OnPropertyChanged(nameof(ObjectKeyword));
            ApplyDefaultObjectKind();
            IsGenerateVisible = true;
            await ReloadBillObjectsAsync();
            // CHG-v1.1.2-26：自定义缴费对象按收费标准铺「规格候选 + 计量参数」；
            // CHG-v1.1.2-34：档案对象铺「手填」变量输入行（计划 T-10：手填 → 输入框）；随后做一次出账预演
            if (IsCustomChargeObject) { await ReloadCustomPayerTemplateAsync(); }
            else { await ReloadMeasureTemplateAsync(); }
            await PreviewAsync();
        }

        /// <summary>
        /// CHG-v1.1.0-16：缴费对象类型默认取所选收费项目的「缴费对象」（项目未设置时按计价方式回落）。
        /// 该类型决定本次可出账的对象；与项目不一致时会在生成前拦截。
        /// </summary>
        private void ApplyDefaultObjectKind()
        {
            ChargeObjectType? objectType = SelectedItem == null ? (ChargeObjectType?)null : SelectedItem.ObjectType;
            if (objectType.HasValue && objectType.Value == ChargeObjectType.Custom)
            {
                // CHG-v1.1.0-17：自定义缴费对象无房产/车位/业主候选，保持默认口径并由生成前置校验拦截
                _objectKindIndex = 0;
            }
            else if (objectType.HasValue)
            {
                _objectKindIndex = objectType.Value == ChargeObjectType.Parking ? 1
                    : (objectType.Value == ChargeObjectType.Owner ? 2 : 0);
            }
            else
            {
                string methodCode = SelectedItem == null ? null : SelectedItem.MethodCode;
                _objectKindIndex = string.Equals(methodCode, "parking", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }
            OnPropertyChanged(nameof(ObjectKindIndex));
            OnPropertyChanged(nameof(ObjectKind));
            OnPropertyChanged(nameof(IsCustomChargeObject));
            OnPropertyChanged(nameof(IsStandardChargeObject));
            OnPropertyChanged(nameof(CustomObjectName));
            OnPropertyChanged(nameof(SelectedObjectCountText));
            // CHG-v1.1.2-50：收费项目切换 → 「出账时可改价」开关与提示同步刷新
            NotifyPriceOverrideStateChanged();
            // CHG-v1.1.2-38：自定义缴费对象行的创建统一放到 ReloadCustomPayerTemplateAsync ——
            // 先把价目表（收费标准）取回来再建行，避免「先建空行 → 再补规格」被下拉框回写打断。
        }

        /// <summary>加载缴费对象候选（只读查询；房产侧隐藏未绑定业主的房产）。</summary>
        private async Task ReloadBillObjectsAsync()
        {
            await ReloadBillObjectsCoreAsync();
            // CHG-v1.1.2-34：候选列表重算后重新铺「手填」计量参数输入行
            ApplyMeasureTemplateToRows();
        }

        /// <summary>收费项目切换：先解析手填变量，再拉候选并铺参数（避免用旧变量集渲染）。</summary>
        private async Task ReloadObjectsAndMeasuresAsync()
        {
            // CHG-v1.1.2-38：自定义缴费对象切项目时同样要重取价目表（此前只刷新候选，规格会停留在上一条标准）
            if (IsCustomChargeObject)
            {
                ClearPriceOverrides();   // CHG-v1.1.2-50：切换收费项目不沿用上一个项目的改后价
                ClearSpecPicks();        // CHG-v1.2.0-12：手选规格同样不沿用
                await ReloadCustomPayerTemplateAsync();
                return;
            }
            ClearPriceOverrides();   // CHG-v1.1.2-50：切换收费项目不沿用上一个项目的改后价
            ClearSpecPicks();        // CHG-v1.2.0-12：手选规格同样不沿用
            await LoadStandardMeasureVarsAsync();
            await ReloadBillObjectsAsync();
        }

        private async Task ReloadBillObjectsCoreAsync()
        {
            // CHG-v1.1.0-18：自定义缴费对象（租户/广告商/外部单位）无基础信息档案，改为手工填写名称
            if (IsCustomChargeObject && IsGenerateVisible)
            {
                foreach (BillObjectRow row in BillObjects) { row.PropertyChanged -= OnObjectRowChanged; }
                BillObjects.Clear();
                HiddenOwnerText = string.Empty;
                ObjectListHint = CustomObjectHintMessage;
                NotifyObjectSelectionChanged();
                return;
            }

            int seq = ++_objectQuerySeq;
            IsObjectLoading = true;
            try
            {
                BillObjectQueryResult result = await Api.QueryBillObjectsAsync(new BillObjectQueryRequest
                {
                    Kind = ObjectKind,
                    Keyword = ObjectKeyword
                });
                if (seq != _objectQuerySeq) { return; }   // 只采用最后一次查询结果（输入快速变化时防抖）

                foreach (BillObjectRow row in BillObjects) { row.PropertyChanged -= OnObjectRowChanged; }
                BillObjects.Clear();
                foreach (BillObjectCandidateDto dto in result.Items ?? new List<BillObjectCandidateDto>())
                {
                    var row = new BillObjectRow
                    {
                        Dto = dto,
                        // CHG-v1.1.0-17：勾选状态来自勾选集合（搜索关键字变化后仍保持）
                        IsChecked = _checkedObjectKeys.Contains(ObjectKey(dto.Id))
                    };
                    row.PropertyChanged += OnObjectRowChanged;
                    BillObjects.Add(row);
                }

                HiddenOwnerText = ObjectKindIndex != 0 || result.HiddenCount <= 0
                    ? string.Empty
                    : "已隐藏 " + result.HiddenCount + " 个未绑定业主的房产";
                ObjectListHint = result.Truncated
                    ? "结果较多，仅显示前 " + BillObjects.Count + " 条，请用关键字缩小范围"
                    : string.Empty;
                NotifyObjectSelectionChanged();
            }
            finally
            {
                IsObjectLoading = false;
            }
        }

        private void OnObjectRowChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BillObjectRow.IsChecked))
            {
                var row = sender as BillObjectRow;
                if (row != null && row.Dto != null)
                {
                    string key = ObjectKey(row.Dto.Id);
                    if (row.IsChecked) { _checkedObjectKeys.Add(key); }
                    else { _checkedObjectKeys.Remove(key); }
                }
                NotifyObjectSelectionChanged();
            }
            else if (e.PropertyName == nameof(BillObjectRow.UnitPriceOverrideText))
            {
                // CHG-v1.1.2-50：出账改价输入 → 写回「类型:ID」并重新试算
                var row = sender as BillObjectRow;
                if (row != null && row.Dto != null)
                {
                    OnPriceOverrideTextChanged(ObjectKey(row.Dto.Id), row.UnitPriceOverrideText);
                }
            }
            else if (e.PropertyName == nameof(BillObjectRow.SelectedSpec))
            {
                // CHG-v1.2.0-12：行内手选规格 → 写回「类型:ID」并重新试算（试算与生成同口径）
                var row = sender as BillObjectRow;
                if (row != null && row.Dto != null)
                {
                    OnSpecSelectionChanged(ObjectKey(row.Dto.Id), row.SelectedSpecId);
                }
            }
        }

        private void NotifyObjectSelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedObjectCountText));
            OnPropertyChanged(nameof(HasSelectedObjects));
            OnPropertyChanged(nameof(IsAllObjectsChecked));
            // CHG-v1.1.2-26：勾选变化后自动试算（规格命中 + 预估金额）
            SchedulePreview();
        }

        private List<int> SelectedObjectIds()
        {
            string prefix = ObjectKind + ":";
            var ids = new List<int>();
            foreach (string key in _checkedObjectKeys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) { continue; }
                int id;
                if (int.TryParse(key.Substring(prefix.Length), out id)) { ids.Add(id); }
            }
            ids.Sort();
            return ids;
        }

        /// <summary>CHG-v1.1.0-17：勾选集合键（kind:id），保证不同缴费对象类型的勾选互不串味。</summary>
        private string ObjectKey(int id)
        {
            return ObjectKind + ":" + id;
        }

        /// <summary>CHG-v1.1.0-18：手工填写的缴费对象名称（去空、按顺序去重）。</summary>
        private List<string> CustomPayerNames()
        {
            var names = new List<string>();
            foreach (BillCustomPayerRow row in CustomPayers)
            {
                string name = row == null || row.Name == null ? null : row.Name.Trim();
                if (string.IsNullOrWhiteSpace(name)) { continue; }
                if (!names.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) { names.Add(name); }
            }
            return names;
        }

        /// <summary>CHG-v1.1.0-18：新增一行自定义缴费对象。</summary>
        private void AddCustomPayerRow()
        {
            var row = new BillCustomPayerRow();
            row.PropertyChanged += OnCustomPayerRowChanged;
            // CHG-v1.1.2-36：手填计量参数变化 → 重算出账预演
            row.MeasuresChanged += (s, e) => SchedulePreview();
            // CHG-v1.1.2-50：自定义缴费对象的出账改价输入（随收费项目开关显示）
            row.IsPriceEditable = IsPriceOverrideEnabled;
            row.ApplyStandard(_customStandard);
            CustomPayers.Add(row);
        }

        /// <summary>CHG-v1.1.0-18：删除一行（至少保留一行空白行，便于继续填写）。</summary>
        private void RemoveCustomPayerRow(BillCustomPayerRow row)
        {
            if (row != null) { CustomPayers.Remove(row); }
            EnsureCustomPayerRow();
        }

        private void EnsureCustomPayerRow()
        {
            if (CustomPayers.Count == 0) { AddCustomPayerRow(); }
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
            // CHG-v1.1.0-18：自定义缴费对象走「手工填写名称」链路（一行一张账单）；
            // 标准缴费对象（房产/车位/业主）不携带手填名称，避免与服务端口径混用
            List<string> customPayerNames = IsCustomChargeObject ? CustomPayerNames() : new List<string>();
            List<int> selectedObjectIds = IsCustomChargeObject ? new List<int>() : SelectedObjectIds();
            if (IsCustomChargeObject)
            {
                if (customPayerNames.Count == 0)
                {
                    ErrorText = "请至少填写一个缴费对象名称";
                    return;
                }
            }
            else if (selectedObjectIds.Count == 0)
            {
                // CHG-v1.1.0-10：缴费对象必须由用户显式选择，空选择不出账（消除「隐式全量出账」）。
                ErrorText = "请至少选择一个缴费对象";
                return;
            }
            // CHG-v1.1.0-16：缴费对象类型必须与收费项目的「缴费对象」一致（服务端同样拦截）
            if (SelectedItem != null && !IsCustomChargeObject)
            {
                int expectedIndex = SelectedItem.ObjectType == ChargeObjectType.Parking ? 1
                    : (SelectedItem.ObjectType == ChargeObjectType.Owner ? 2 : 0);
                if (ObjectKindIndex != expectedIndex)
                {
                    string expected = expectedIndex == 1 ? "车位" : (expectedIndex == 2 ? "业主" : "房产");
                    ErrorText = "收费项目「" + SelectedItem.Name + "」的缴费对象为「" + expected + "」，请切换到对应类型后再生成";
                    return;
                }
            }
            BillGenerateLogDto log = null;
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
                log = await Api.GenerateBillsAsync(new BillGenerateRequest
                {
                    ChargeItemId = SelectedItem.Id,
                    CycleId = SelectedCycle.Id,
                    PropertyIds = !IsCustomChargeObject && ObjectKindIndex == 0 ? selectedObjectIds : new List<int>(),
                    ParkingIds = !IsCustomChargeObject && ObjectKindIndex == 1 ? selectedObjectIds : new List<int>(),
                    OwnerIds = !IsCustomChargeObject && ObjectKindIndex == 2 ? selectedObjectIds : new List<int>(),
                    CustomPayerNames = customPayerNames,
                    // CHG-v1.1.2-26：自定义缴费对象的规格手选与计量参数一并提交（服务端按此计价并落快照）
                    CustomPayers = IsCustomChargeObject ? BuildCustomPayerRequests() : null,
                    // CHG-v1.1.2-34：档案对象（房产/车位/业主）的行内手填计量参数一并提交
                    ObjectMeasures = IsCustomChargeObject ? null : BuildObjectMeasures()
                });
                await RefreshBatchesAsync();
                await RefreshSummaryAsync();
                IsGenerateVisible = false;
            }, null);

            // 说明：RunAsync 在 successMessage 为空时会清空 StatusText，
            // 故生成结果提示必须在 RunAsync 返回之后再写入（否则提示被清掉、界面无反馈）。
            if (log != null)
            {
                if (log.Fail > 0)
                {
                    ErrorText = "生成完成：成功 " + log.Success + " 户，失败 " + log.Fail + " 户（见失败清单）";
                }
                else
                {
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + "生成草稿 " + log.Success + " 户，等待发布";
                }
            }
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
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "批次 " + log.Id + " 已发布";
            }, null);
            // CHG-v1.2.0-23：发布成功后即时刷新顶部铃铛待办数
            RaiseDataChanged();
        }

        /// <summary>
        /// T4F-2-1：草稿批次编辑（回填收费项目/周期后重新生成失败清单）。
        /// CHG-v1.1.0-10：同时回填该批次既有缴费对象，避免「重新生成必定全失败」的误解。
        /// </summary>
        private async Task EditBatchAsync(BillBatchRow row)
        {
            if (row == null || !row.CanEdit) { return; }
            if (row.Dto.ChargeItemId.HasValue)
            {
                // 直接赋值，避免触发 SelectedItem 的候选重载（随后统一重载一次）
                _selectedItem = ChargeItems.FirstOrDefault(x => x.Id == row.Dto.ChargeItemId.Value) ?? _selectedItem;
                OnPropertyChanged(nameof(SelectedItem));
            }
            if (row.Dto.CycleId.HasValue)
            {
                SelectedCycle = Cycles.FirstOrDefault(x => x.Id == row.Dto.CycleId.Value) ?? SelectedCycle;
            }
            _objectKeyword = string.Empty;
            OnPropertyChanged(nameof(ObjectKeyword));
            ApplyDefaultObjectKind();
            IsGenerateVisible = true;
            // CHG-v1.1.0-18：自定义缴费对象批次重开时给一行空白行（名称需重新填写）
            if (IsCustomChargeObject)
            {
                CustomPayers.Clear();
                EnsureCustomPayerRow();
                await ReloadBillObjectsAsync();
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已载入草稿批次 " + row.BatchNoText + "，请重新填写自定义缴费对象名称后生成";
                return;
            }

            _checkedObjectKeys.Clear();
            try
            {
                BillObjectSelectionDto selection = await Api.GetBatchBillObjectsAsync(row.Dto.Id);
                int batchKind = 0;   // 0 房产 / 1 车位 / 2 业主（CHG-v1.1.0-11）
                if (selection != null)
                {
                    if (selection.OwnerIds != null && selection.OwnerIds.Count > 0) { batchKind = 2; }
                    else if (selection.ParkingIds != null && selection.ParkingIds.Count > 0) { batchKind = 1; }
                }
                _objectKindIndex = batchKind;
                OnPropertyChanged(nameof(ObjectKindIndex));
                OnPropertyChanged(nameof(ObjectKind));
                // CHG-v1.1.0-17：既有选择写入勾选集合（kind:id），后续搜索/清空关键字不回退勾选
                List<int> preselected = selection == null
                    ? null
                    : (batchKind == 2 ? selection.OwnerIds
                        : (batchKind == 1 ? selection.ParkingIds : selection.PropertyIds));
                if (preselected != null)
                {
                    foreach (int objectId in preselected) { _checkedObjectKeys.Add(ObjectKey(objectId)); }
                }
            }
            catch (Exception)
            {
                _checkedObjectKeys.Clear();   // 回填失败不阻断编辑，用户可重新勾选
            }
            await ReloadBillObjectsAsync();
            StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已载入草稿批次 " + row.BatchNoText + "，可重选收费项目/周期/缴费对象后重新生成";
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
        /// <summary>
        /// CHG-v1.1.0-17：删除计费周期（自定义周期栏）。
        /// 内置周期与已被账单引用的周期由服务端拦截，前端先行提示，避免无效操作。
        /// </summary>
        private void RequestDeleteCycle()
        {
            if (SelectedCycle == null)
            {
                ErrorText = "请先在「计费周期」下拉框中选择要删除的周期";
                return;
            }
            if (SelectedCycle.CycleType != BillingCycleType.Custom)
            {
                ErrorText = "系统内置周期不可删除（仅自定义周期支持删除）";
                return;
            }
            _pendingCycleDelete = SelectedCycle;
            _pendingDelete = null;
            _batchDeleteRows = null;
            DeleteConfirmTitle = "确认删除周期";
            DeleteConfirmText = "将删除计费周期 " + PeriodText(SelectedCycle) +
                "，删除后该周期会从下拉框中消失。已被账单引用的周期无法删除（系统会自动拦截）。确认删除？";
            IsDeleteConfirmVisible = true;
        }

        /// <summary>CHG-v1.1.0-17：周期显示文本（起止日期，截断到日）。</summary>
        private static string PeriodText(BillingCycleDto cycle)
        {
            return cycle.StartDate.ToString("yyyy-MM-dd") + " ~ " + cycle.EndDate.ToString("yyyy-MM-dd");
        }

        private void RequestDelete(BillBatchRow row)
        {
            if (row == null || !row.CanDelete) { return; }
            _pendingDelete = row;
            DeleteConfirmTitle = "确认删除批次";
            // CHG-v1.1.2-02：删除口径更新 —— 下游模块（收款登记/退款记录/欠费台账/财务报表/收支明细流水）
            // 对该批次账单的记录将同步不再计入，避免「删了账单、报表还照算」的账实不一致。
            int paidHouseholds = (row.Dto == null ? 0 : row.Dto.PaidCount) + (row.Dto == null ? 0 : row.Dto.PartialCount);
            DeleteConfirmText = "将删除批次 " + row.BatchNoText + "（" + row.ChargeItemName + "，户数 " + row.HouseCountText +
                (paidHouseholds > 0 ? "，其中已缴/部分缴 " + paidHouseholds + " 户" : string.Empty) + "）。\n" +
                "删除后：该批次账单，以及它在收款登记、退款记录、欠费台账、财务报表、收支明细流水中的记录会同步不再显示（原始流水数据保留留痕，不做物理销毁）。\n" +
                "请确认后再删除。";
            IsDeleteConfirmVisible = true;
        }

        private async Task ConfirmDeleteAsync()
        {
            // CHG-v1.1.0-17：删除计费周期（复用同一确认弹窗）
            BillingCycleDto pendingCycle = _pendingCycleDelete;
            _pendingCycleDelete = null;
            if (pendingCycle != null)
            {
                IsDeleteConfirmVisible = false;
                await RunAsync(async () =>
                {
                    await Api.DeleteCycleAsync(pendingCycle.Id);
                    await ReloadCyclesAsync();
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + "计费周期 " + PeriodText(pendingCycle) + " 已删除";
                }, null);
                return;
            }

            BillBatchRow row = _pendingDelete;
            IsDeleteConfirmVisible = false;
            _pendingDelete = null;
            if (_batchDeleteRows != null && _batchDeleteRows.Count > 0)
            {
                var rows = _batchDeleteRows;
                _batchDeleteRows = null;
                await RunAsync(async () =>
                {
                    foreach (var r in rows) { await Api.DeleteBillBatchAsync(r.Dto.Id); }
                    await RefreshBatchesAsync();
                    await RefreshSummaryAsync();
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已批量删除 " + rows.Count + " 个批次（软删，轨迹保留）";
                }, null);
                RaiseDataChanged(); // CHG-v1.2.0-23：批次删除后待办数同步
                return;
            }
            if (row == null) { return; }
            await RunAsync(async () =>
            {
                await Api.DeleteBillBatchAsync(row.Dto.Id);
                await RefreshBatchesAsync();
                await RefreshSummaryAsync();
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "批次 " + row.BatchNoText + " 已删除（软删，轨迹保留）";
            }, null);
            RaiseDataChanged(); // CHG-v1.2.0-23：批次删除后待办数同步
        }

        /// <summary>批量删除批次（任意状态均可删；软删批次及其账单，已缴/部分缴流水不回退）。</summary>
        private void RequestBatchDelete()
        {
            var rows = Batches.Where(x => x.IsChecked).ToList();
            if (rows.Count == 0) { ErrorText = "请先勾选要删除的批次"; return; }
            _batchDeleteRows = rows;
            _pendingDelete = null;
            DeleteConfirmTitle = "确认删除批次";
            DeleteConfirmText = "将批量删除 " + rows.Count + " 个批次。\n" +
                "删除后：这些批次账单，以及它们在收款登记、退款记录、欠费台账、财务报表、收支明细流水中的记录会同步不再显示（原始流水数据保留留痕）。\n" +
                "请确认后再删除。";
            IsDeleteConfirmVisible = true;
        }
        private async Task RetryAsync(BillBatchRow row)
        {
            if (row == null) { return; }
            BillGenerateLogDto log = null;
            await RunAsync(async () =>
            {
                log = await Api.RetryFailuresAsync(row.Dto.Id);
                await RefreshBatchesAsync();
                await RefreshSummaryAsync();
            }, null);
            if (log != null)
            {
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "失败批次已重推（批次 " + row.BatchNoText +
                    "：本次成功 " + log.Success + " 户、失败 " + log.Fail + " 户）";
            }
        }

        /// <summary>
        /// CHG-v1.1.0-21：顶部「发布失败」卡片一键重推 —— 按批次逐个重推全部发布失败批次（FL-FIN-01）。
        /// 口径与单批次「重推」一致（取批次失败清单重新出账），逐批次汇总成功/失败后给出提示；
        /// 无失败批次时提示并不发请求。
        /// </summary>
        private async Task RetryAllFailuresAsync()
        {
            var failedBatches = _allBatches
                .Where(x => string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Id)
                .ToList();
            if (failedBatches.Count == 0)
            {
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "当前没有发布失败的批次，无需重推";
                return;
            }

            // 说明：RunAsync 在 successMessage 为空时会清空 StatusText，
            // 故一键重推结果提示在 RunAsync 返回之后再写入（否则提示被清掉、界面无反馈）。
            string resultMessage = null;
            await RunAsync(async () =>
            {
                int successBatches = 0;
                int successBills = 0;
                int failBills = 0;
                var failedNames = new List<string>();
                foreach (var batch in failedBatches)
                {
                    try
                    {
                        var log = await Api.RetryFailuresAsync(batch.Id);
                        successBatches++;
                        successBills += log.Success;
                        failBills += log.Fail;
                    }
                    catch (Exception ex)
                    {
                        string batchNo = string.IsNullOrWhiteSpace(batch.BatchNo)
                            ? "BILL-" + batch.Id.ToString("D4")
                            : batch.BatchNo;
                        failedNames.Add(batchNo + "（" + ex.Message + "）");
                    }
                }
                await RefreshBatchesAsync();
                await RefreshSummaryAsync();

                string message = "一键重推完成：" + failedBatches.Count + " 个失败批次，" +
                    successBatches + " 个已重推（成功 " + successBills + " 户 / 失败 " + failBills + " 户）";
                if (failedNames.Count > 0)
                {
                    message += "；" + failedNames.Count + " 个批次重推未通过：" + string.Join("、", failedNames.Take(3));
                }
                resultMessage = message;
            }, null);
            RaiseDataChanged(); // CHG-v1.2.0-23：重推成功后待办数同步
            if (resultMessage != null)
            {
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + resultMessage;
            }
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
