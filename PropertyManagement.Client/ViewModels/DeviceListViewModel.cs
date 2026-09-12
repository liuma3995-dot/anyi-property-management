using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Equipment;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>状态变更目标选项（R8：人工状态变更弹层下拉项）。</summary>
    public class StatusTargetOption
    {
        public DeviceStatus Status { get; set; }
        public string Text { get; set; }
    }

    /// <summary>设备状态变更留痕行（R8，详情浮层「状态留痕」页签）。</summary>
    public class DeviceStatusLogRow
    {
        public DeviceStatusLogDto Dto { get; set; }
        public string ChangedAtText { get { return Dto.ChangedAt.ToString("yyyy-MM-dd HH:mm"); } }
        public string OldStatusText { get { return StatusName(Dto.OldStatus); } }
        public string NewStatusText { get { return StatusName(Dto.NewStatus); } }
        public string ReasonText { get { return string.IsNullOrWhiteSpace(Dto.Reason) ? "—" : Dto.Reason; } }
        /// <summary>状态文案；oldStatus=-1 为首次登记行（无前置状态）。</summary>
        private static string StatusName(DeviceStatus status)
        {
            switch ((int)status)
            {
                case -1: return "（新登记）";
                case 0: return "在用";
                case 1: return "维修中";
                case 2: return "停用";
                default: return "报废";
            }
        }
    }

    /// <summary>设备行（PG-EQP-01）。</summary>
    public class DeviceRow : ObservableObject
    {
        private bool _isChecked;
        public DeviceDto Dto { get; set; }
        /// <summary>批量删除勾选态（参照纠纷列表）。</summary>
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }
        public string DeviceNo { get { return string.IsNullOrEmpty(Dto.DeviceNo) ? "EQP-" + Dto.Id.ToString("0000") : Dto.DeviceNo; } }
        public string Name { get { return Dto.Name ?? string.Empty; } }
        public string TypeName { get { return Dto.TypeName ?? string.Empty; } }
        public string Location { get { return Dto.Location ?? string.Empty; } }
        public string BrandModel { get { return Dto.BrandModel ?? string.Empty; } }
        public string EnableDate { get { return Dto.EnableDate.HasValue ? Dto.EnableDate.Value.ToString("yyyy-MM-dd") : "—"; } }
        public string StatusText
        {
            get
            {
                if (!string.IsNullOrEmpty(Dto.StatusText)) return Dto.StatusText;
                switch (Dto.Status)
                {
                    case DeviceStatus.InUse: return "在用";
                    case DeviceStatus.Repairing: return "维修中";
                    case DeviceStatus.Disabled: return "停用";
                    case DeviceStatus.Scrapped: return "报废";
                    default: return "未知";
                }
            }
        }
        /// <summary>下次保养：非在用设备显示"—"（原型行-3/5/7）。</summary>
        public string NextMaintenance { get { return Dto.Status == DeviceStatus.InUse && !string.IsNullOrEmpty(Dto.NextMaintenanceText) ? Dto.NextMaintenanceText : "—"; } }
        /// <summary>状态判定键（派生展示态优先）：fault 故障 / repair 维修中 / inuse 在用 / disabled 停用 / scrapped 报废。</summary>
        private string StatusKey
        {
            get
            {
                string t = StatusText;
                if (t == "故障") return "fault";
                if (t == "维修中") return "repair";
                if (t == "在用") return "inuse";
                if (t == "报废") return "scrapped";
                return "disabled";
            }
        }
        /// <summary>原型 PG-EQP-01：操作列「报修」仅出现在状态为「故障」的行（存在待处理故障单）。</summary>
        public bool CanRepair { get { return StatusKey == "fault"; } }
        public Brush StatusBg
        {
            get
            {
                switch (StatusKey)
                {
                    case "fault": return Solid("#FDECEC");
                    case "repair": return Solid("#FFF5DC");
                    case "inuse": return Solid("#E8F7F1");
                    default: return Solid("#F2F4F8");
                }
            }
        }
        public Brush StatusFg
        {
            get
            {
                switch (StatusKey)
                {
                    case "fault": return Solid("#D64545");
                    case "repair": return Solid("#B76E00");
                    case "inuse": return Solid("#12805C");
                    default: return Solid("#667085");
                }
            }
        }
        private static Brush Solid(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>分页页码（原型 PG-EQP-01"共 186 条记录 · 第 1/2 页"右侧页码组）。</summary>
    /// <remarks>位置筛选项（原型"位置：全部"下拉，取值来自在册设备位置，不硬编码）。</remarks>
    public class LocationOption
    {
        public string Value { get; set; }
        public string Label { get; set; }
    }

    public class PageNumber : ObservableObject
    {
        private bool _isCurrent;
        public int Index { get; set; }
        public string Text { get { return Index.ToString(); } }
        public bool IsCurrent { get { return _isCurrent; } set { SetProperty(ref _isCurrent, value); } }
    }

    /// <summary>设备列表（PG-EQP-01，UC-EQP-001/007，BR-EQP-01/05）。</summary>
    public class DeviceListViewModel : BaseInfoPageViewModel
    {
        private const int PageSize = 100;
        /// <summary>自定义新增设备类别的默认保养周期（天，P-04 缺省口径）。</summary>
        private const int DefaultTypeCycle = 30;

        private string _keyword = string.Empty;
        private LocationOption _selectedLocation;
        private int _typeFilterId;
        private int _statusFilter;
        private int _pageIndex = 1;
        private int _totalCount;
        private int _pageCount = 1;
        private int _total;
        private int _inUse;
        private int _repairing;
        private int _disabled;
        private string _totalSub = "—";
        private string _inUseSub = "—";
        private string _repairingSub = "—";
        private string _disabledSub = "待处置";
        private bool _isFormVisible;
        private bool _isDetailVisible;
        private bool _isRepairVisible;
        private int _detailTabIndex;
        private int? _editId;
        private int _formTypeId;
        private string _formName = string.Empty;
        private string _formLocation = string.Empty;
        private string _formBrandModel = string.Empty;
        private DateTime? _formEnableDate = DateTime.Today;
        private DeviceRow _detailRow;
        private DateTime? _formWarrantyEnd;
        private DateTime? _formContractEnd;
        private int _repairTargetId;
        private string _repairTargetText = string.Empty;
        private string _repairSymptom = string.Empty;
        private int _repairLevelIndex;
        private string _repairReporter = string.Empty;
        private string _repairFTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        private bool _isBatchMode;
        private bool _isBatchConfirmVisible;
        private string _batchConfirmText = string.Empty;
        private string _formTypeText = string.Empty;
        private bool _isTypeAdd;
        private string _newTypeName = string.Empty;
        private bool _isTypeDeleteVisible;
        private string _deleteTypeName = string.Empty;
        private int _deleteTypeId;
        private readonly System.Windows.Threading.DispatcherTimer _searchTimer;
        // ---------- R6：详情浮层「下次保养自定义」+「自定义类型记录」 ----------
        private DateTime? _nextMaintenanceEdit;
        private string _nextMaintenanceSourceText = "—";
        private bool _isCustomRecordForm;
        private int _editingCustomRecordId;
        private string _customTypeName = string.Empty;
        private DateTime _customRDate = DateTime.Today;
        private string _customContent = string.Empty;
        private string _customResult = "合格";
        private string _customCostText = string.Empty;
        private int _customVendorId;
        private string _customOperator = string.Empty;
        private bool _isCustomRecordDeleteVisible;
        private string _customRecordDeleteText = string.Empty;
        private int _deleteCustomRecordId;
        // ---------- R8：人工状态变更（在用 / 停用 / 报废 / 维修中） ----------
        private bool _isStatusChangeVisible;
        private string _statusChangeTitle = string.Empty;
        private string _statusChangeSub = string.Empty;
        private StatusTargetOption _selectedStatusTarget;
        private string _statusChangeReason = string.Empty;
        private string _statusChangeError = string.Empty;
        private DeviceRow _statusChangeTarget;

        public DeviceListViewModel(IApiClient api) : base(api)
        {
            SearchCommand = new AsyncRelayCommand(() => { _pageIndex = 1; return LoadAsync(); });
            QueryCommand = SearchCommand;
            NewCommand = new RelayCommand(StartNew);
            EditCommand = new RelayCommand<DeviceRow>(StartEdit);
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(() => IsFormVisible = false);
            OpenNewTypeCommand = new RelayCommand(OpenNewType);
            ConfirmNewTypeCommand = new AsyncRelayCommand(ConfirmNewTypeAsync);
            CancelNewTypeCommand = new RelayCommand(() => { IsTypeAdd = false; NewTypeName = string.Empty; ErrorText = string.Empty; });
            RequestDeleteTypeCommand = new RelayCommand(RequestDeleteType);
            ConfirmDeleteTypeCommand = new AsyncRelayCommand(ConfirmDeleteTypeAsync);
            CancelDeleteTypeCommand = new RelayCommand(() => IsTypeDeleteVisible = false);
            EnterBatchModeCommand = new RelayCommand(EnterBatchMode);
            ConfirmSelectionCommand = new RelayCommand(RequestBatchConfirm);
            ConfirmBatchDeleteCommand = new AsyncRelayCommand(ConfirmBatchDeleteAsync);
            CancelBatchDeleteCommand = new RelayCommand(ExitBatchMode);
            DetailCommand = new RelayCommand<DeviceRow>(OpenDetail);
            CloseDetailCommand = new RelayCommand(() => IsDetailVisible = false);
            SetDetailTabCommand = new RelayCommand<string>(s => { int i; if (int.TryParse(s, out i)) DetailTabIndex = i; });
            EditDetailCommand = new RelayCommand(EditFromDetail);
            RepairCommand = new RelayCommand<DeviceRow>(OpenRepair);
            SubmitRepairCommand = new AsyncRelayCommand(SubmitRepairAsync);
            CancelRepairCommand = new RelayCommand(() => IsRepairVisible = false);
            // R6：下次保养自定义 + 自定义类型记录
            SaveNextMaintenanceCommand = new AsyncRelayCommand(SaveNextMaintenanceAsync);
            ClearNextMaintenanceCommand = new AsyncRelayCommand(ClearNextMaintenanceAsync);
            OpenAddCustomRecordCommand = new RelayCommand(OpenAddCustomRecord);
            EditCustomRecordCommand = new RelayCommand<DeviceCustomRecordDto>(LoadCustomRecordForEdit);
            SaveCustomRecordCommand = new AsyncRelayCommand(SaveCustomRecordAsync);
            CancelCustomRecordCommand = new RelayCommand(() => { IsCustomRecordForm = false; _editingCustomRecordId = 0; });
            RequestDeleteCustomRecordCommand = new RelayCommand<DeviceCustomRecordDto>(RequestDeleteCustomRecord);
            ConfirmDeleteCustomRecordCommand = new AsyncRelayCommand(ConfirmDeleteCustomRecordAsync);
            CancelDeleteCustomRecordCommand = new RelayCommand(() => IsCustomRecordDeleteVisible = false);
            // R8：人工状态变更（停用 / 启用 / 报废）
            ChangeStatusCommand = new RelayCommand<DeviceRow>(OpenStatusChange);
            ConfirmStatusChangeCommand = new AsyncRelayCommand(ConfirmStatusChangeAsync);
            CancelStatusChangeCommand = new RelayCommand(() => IsStatusChangeVisible = false);
            ExportCommand = new AsyncRelayCommand(ExportAsync);
            GoToPageCommand = new RelayCommand<PageNumber>(GoToPage);
            PrevPageCommand = new RelayCommand(() => { if (_pageIndex > 1) { _pageIndex--; _ = LoadAsync(); } }, () => _pageIndex > 1);
            NextPageCommand = new RelayCommand(() => { if (_pageIndex < PageCount) { _pageIndex++; _ = LoadAsync(); } }, () => _pageIndex < PageCount);
            // 输入即检索（防抖 300ms）：输入字符自动检索并按相关度重排，无需回车
            _searchTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); _pageIndex = 1; _ = LoadAsync(); };
            _ = LoadAsync();
        }

        public ObservableCollection<DeviceRow> Items { get; } = new ObservableCollection<DeviceRow>();
        /// <summary>设备类型（新增/编辑表单用）。</summary>
        public ObservableCollection<DeviceTypeDto> Types { get; } = new ObservableCollection<DeviceTypeDto>();
        /// <summary>类别筛选（首项"类别：全部"，BR-EQP-05 类型可扩展，不再硬编码）。</summary>
        public ObservableCollection<DeviceTypeDto> TypeFilterItems { get; } = new ObservableCollection<DeviceTypeDto>();
        /// <summary>位置筛选（首项"位置：全部"，其余为在册设备位置去重）。</summary>
        public ObservableCollection<LocationOption> LocationFilterItems { get; } = new ObservableCollection<LocationOption>();
        /// <summary>详情浮层-保养记录。</summary>
        public ObservableCollection<MaintenanceRecordDto> MaintenanceRecords { get; } = new ObservableCollection<MaintenanceRecordDto>();
        /// <summary>详情浮层-年检记录。</summary>
        public ObservableCollection<InspectionRecordDto> InspectionRecords { get; } = new ObservableCollection<InspectionRecordDto>();
        /// <summary>详情浮层-故障记录。</summary>
        public ObservableCollection<FaultRecordDto> FaultRecords { get; } = new ObservableCollection<FaultRecordDto>();
        /// <summary>详情浮层-自定义类型记录（R6）。</summary>
        public ObservableCollection<DeviceCustomRecordDto> CustomRecords { get; } = new ObservableCollection<DeviceCustomRecordDto>();
        /// <summary>详情浮层-状态留痕（R8）。</summary>
        public ObservableCollection<DeviceStatusLogRow> StatusLogs { get; } = new ObservableCollection<DeviceStatusLogRow>();
        /// <summary>自定义类型名称候选（历史自定义登记类型 + 已录入记录，R6）。</summary>
        public ObservableCollection<string> CustomRecordTypes { get; } = new ObservableCollection<string>();
        /// <summary>执行方（t_vendor，R6 自定义类型记录用）。</summary>
        public ObservableCollection<VendorDto> Vendors { get; } = new ObservableCollection<VendorDto>();
        /// <summary>分页页码按钮（最多 8 个，含当前页高亮）。</summary>
        public ObservableCollection<PageNumber> PageNumbers { get; } = new ObservableCollection<PageNumber>();

        public int Total { get { return _total; } private set { SetProperty(ref _total, value); } }
        public int InUse { get { return _inUse; } private set { SetProperty(ref _inUse, value); } }
        public int Repairing { get { return _repairing; } private set { SetProperty(ref _repairing, value); } }
        public int Disabled { get { return _disabled; } private set { SetProperty(ref _disabled, value); } }
        public string TotalSub { get { return _totalSub; } private set { SetProperty(ref _totalSub, value); } }
        public string InUseSub { get { return _inUseSub; } private set { SetProperty(ref _inUseSub, value); } }
        public string RepairingSub { get { return _repairingSub; } private set { SetProperty(ref _repairingSub, value); } }
        public string DisabledSub { get { return _disabledSub; } private set { SetProperty(ref _disabledSub, value); } }

        /// <summary>搜索关键字：输入即自动检索（300ms 防抖）并按相关度重排，无需回车（保留回车立即检索）。</summary>
        public string Keyword
        {
            get { return _keyword; }
            set
            {
                if (!SetProperty(ref _keyword, value)) return;
                if (_searchTimer == null) return;
                _searchTimer.Stop();
                _searchTimer.Start();
            }
        }
        public LocationOption SelectedLocation
        {
            get { return _selectedLocation; }
            set { if (SetProperty(ref _selectedLocation, value)) { _pageIndex = 1; _ = LoadAsync(); } }
        }
        public int TypeFilterId { get { return _typeFilterId; } set { if (SetProperty(ref _typeFilterId, value)) { _pageIndex = 1; _ = LoadAsync(); } } }
        public int StatusFilter { get { return _statusFilter; } set { if (SetProperty(ref _statusFilter, value)) { _pageIndex = 1; _ = LoadAsync(); } } }
        public int PageIndex { get { return _pageIndex; } private set { SetProperty(ref _pageIndex, value); } }
        public int PageCount { get { return _pageCount; } private set { SetProperty(ref _pageCount, value); } }
        public string PageText { get { return "共 " + _totalCount + " 条记录 · 第 " + _pageIndex + "/" + _pageCount + " 页"; } }

        public bool IsFormVisible { get { return _isFormVisible; } set { SetProperty(ref _isFormVisible, value); } }
        public bool IsDetailVisible { get { return _isDetailVisible; } set { SetProperty(ref _isDetailVisible, value); } }
        public bool IsRepairVisible { get { return _isRepairVisible; } set { SetProperty(ref _isRepairVisible, value); } }
        public int DetailTabIndex { get { return _detailTabIndex; } set { SetProperty(ref _detailTabIndex, value); } }
        public DeviceRow DetailRow { get { return _detailRow; } private set { SetProperty(ref _detailRow, value); } }
        public int FormTypeId { get { return _formTypeId; } set { SetProperty(ref _formTypeId, value); } }
        /// <summary>设备类别下拉文本（可自定义输入，保存时按名称解析/新建）。</summary>
        public string FormTypeText
        {
            get { return _formTypeText; }
            set
            {
                if (!SetProperty(ref _formTypeText, value)) return;
                var match = Types.FirstOrDefault(t => string.Equals(t.Name, value == null ? null : value.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null && match.Id != _formTypeId) FormTypeId = match.Id;
            }
        }
        /// <summary>新增类别内联行是否显示。</summary>
        public bool IsTypeAdd { get { return _isTypeAdd; } set { SetProperty(ref _isTypeAdd, value); } }
        public string NewTypeName { get { return _newTypeName; } set { SetProperty(ref _newTypeName, value); } }
        /// <summary>删除类别二次确认浮层。</summary>
        public bool IsTypeDeleteVisible { get { return _isTypeDeleteVisible; } set { SetProperty(ref _isTypeDeleteVisible, value); } }
        public string DeleteTypeName { get { return _deleteTypeName; } set { SetProperty(ref _deleteTypeName, value); } }
        /// <summary>批量删除模式（勾选列 + 确认/取消按钮显隐）。</summary>
        public bool IsBatchMode
        {
            get { return _isBatchMode; }
            set { if (SetProperty(ref _isBatchMode, value)) OnPropertyChanged(nameof(IsNotBatchMode)); }
        }
        public bool IsNotBatchMode { get { return !_isBatchMode; } }
        public bool HasChecked { get { return Items.Any(x => x.IsChecked); } }
        public bool IsAllChecked
        {
            get { return Items.Count > 0 && Items.All(x => x.IsChecked); }
            set
            {
                foreach (var row in Items) row.IsChecked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasChecked));
                OnPropertyChanged(nameof(CheckedCountText));
            }
        }
        public string CheckedCountText { get { return "已选 " + Items.Count(x => x.IsChecked) + " / " + Items.Count + " 项"; } }
        public bool IsBatchConfirmVisible { get { return _isBatchConfirmVisible; } set { SetProperty(ref _isBatchConfirmVisible, value); } }
        public string BatchConfirmText { get { return _batchConfirmText; } set { SetProperty(ref _batchConfirmText, value); } }
        public string FormName { get { return _formName; } set { SetProperty(ref _formName, value); } }
        public string FormLocation { get { return _formLocation; } set { SetProperty(ref _formLocation, value); } }
        public string FormBrandModel { get { return _formBrandModel; } set { SetProperty(ref _formBrandModel, value); } }
        public DateTime? FormEnableDate { get { return _formEnableDate; } set { SetProperty(ref _formEnableDate, value); } }
        /// <summary>质保到期（PG-EQP-04 质保到期提醒数据源）。</summary>
        public DateTime? FormWarrantyEnd { get { return _formWarrantyEnd; } set { SetProperty(ref _formWarrantyEnd, value); } }
        /// <summary>维保合同到期（PG-EQP-04 合同到期提醒数据源）。</summary>
        public DateTime? FormContractEnd { get { return _formContractEnd; } set { SetProperty(ref _formContractEnd, value); } }

        public string RepairTargetText { get { return _repairTargetText; } private set { SetProperty(ref _repairTargetText, value); } }
        public string RepairSymptom { get { return _repairSymptom; } set { SetProperty(ref _repairSymptom, value); } }
        public int RepairLevelIndex { get { return _repairLevelIndex; } set { SetProperty(ref _repairLevelIndex, value); } }
        public string RepairReporter { get { return _repairReporter; } set { SetProperty(ref _repairReporter, value); } }
        public string RepairFTimeText { get { return _repairFTimeText; } set { SetProperty(ref _repairFTimeText, value); } }

        public IAsyncRelayCommand SearchCommand { get; }
        public IAsyncRelayCommand QueryCommand { get; }
        public IRelayCommand NewCommand { get; }
        public IRelayCommand<DeviceRow> EditCommand { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand OpenNewTypeCommand { get; }
        public IAsyncRelayCommand ConfirmNewTypeCommand { get; }
        public IRelayCommand CancelNewTypeCommand { get; }
        public IRelayCommand RequestDeleteTypeCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteTypeCommand { get; }
        public IRelayCommand CancelDeleteTypeCommand { get; }
        public IRelayCommand EnterBatchModeCommand { get; }
        public IRelayCommand ConfirmSelectionCommand { get; }
        public IAsyncRelayCommand ConfirmBatchDeleteCommand { get; }
        public IRelayCommand CancelBatchDeleteCommand { get; }
        public IRelayCommand<DeviceRow> DetailCommand { get; }
        public IRelayCommand CloseDetailCommand { get; }
        public IRelayCommand<string> SetDetailTabCommand { get; }
        public IRelayCommand EditDetailCommand { get; }

        // ---------- R6：下次保养自定义（详情浮层日历 + 来源标注） ----------
        /// <summary>详情浮层「下次保养」日历选择值（空=按周期派生）。</summary>
        public DateTime? NextMaintenanceEdit
        {
            get { return _nextMaintenanceEdit; }
            set { SetProperty(ref _nextMaintenanceEdit, value); }
        }
        /// <summary>下次保养来源标注：自定义 / 周期派生（R6）。</summary>
        public string NextMaintenanceSourceText
        {
            get { return _nextMaintenanceSourceText; }
            private set { SetProperty(ref _nextMaintenanceSourceText, value); }
        }
        /// <summary>下次保养仅对在用设备开放（R6：停用/维修中/报废设备不参与保养计划）。</summary>
        public bool CanEditNextMaintenance
        {
            get { return DetailRow != null && DetailRow.Dto.Status == DeviceStatus.InUse; }
        }
        public bool CannotEditNextMaintenance { get { return !CanEditNextMaintenance; } }

        // ---------- R6：自定义类型记录（登记类型解耦） ----------
        public bool IsCustomRecordForm { get { return _isCustomRecordForm; } set { SetProperty(ref _isCustomRecordForm, value); } }
        public string CustomTypeName { get { return _customTypeName; } set { SetProperty(ref _customTypeName, value); } }
        public DateTime CustomRDate { get { return _customRDate; } set { SetProperty(ref _customRDate, value); } }
        public string CustomContent { get { return _customContent; } set { SetProperty(ref _customContent, value); } }
        public string CustomResult { get { return _customResult; } set { SetProperty(ref _customResult, value); } }
        public string CustomCostText { get { return _customCostText; } set { SetProperty(ref _customCostText, value); } }
        /// <summary>执行方 Id（t_vendor，空=未指定）。</summary>
        public int CustomVendorId { get { return _customVendorId; } set { SetProperty(ref _customVendorId, value); } }
        public string CustomOperator { get { return _customOperator; } set { SetProperty(ref _customOperator, value); } }
        public string CustomRecordFormTitle { get { return _editingCustomRecordId > 0 ? "编辑自定义类型记录" : "新增自定义类型记录"; } }
        public bool IsCustomRecordDeleteVisible { get { return _isCustomRecordDeleteVisible; } set { SetProperty(ref _isCustomRecordDeleteVisible, value); } }
        public string CustomRecordDeleteText { get { return _customRecordDeleteText; } set { SetProperty(ref _customRecordDeleteText, value); } }
        public IRelayCommand<DeviceRow> RepairCommand { get; }
        public IAsyncRelayCommand SubmitRepairCommand { get; }
        public IRelayCommand CancelRepairCommand { get; }
        // ---------- R6：详情浮层「下次保养自定义」+「自定义类型记录」 ----------
        public IAsyncRelayCommand SaveNextMaintenanceCommand { get; }
        public IAsyncRelayCommand ClearNextMaintenanceCommand { get; }
        public IRelayCommand OpenAddCustomRecordCommand { get; }
        public IRelayCommand<DeviceCustomRecordDto> EditCustomRecordCommand { get; }
        public IAsyncRelayCommand SaveCustomRecordCommand { get; }
        public IRelayCommand CancelCustomRecordCommand { get; }
        public IRelayCommand<DeviceCustomRecordDto> RequestDeleteCustomRecordCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteCustomRecordCommand { get; }
        public IRelayCommand CancelDeleteCustomRecordCommand { get; }

        // ---------- R8：人工状态变更（在用 / 维修中 / 停用 / 报废）+ 状态留痕 ----------
        /// <summary>状态变更目标候选（按 BR-EQP-01 合法迁移表生成）。</summary>
        public ObservableCollection<StatusTargetOption> StatusTargetOptions { get; } = new ObservableCollection<StatusTargetOption>();
        public bool IsStatusChangeVisible { get { return _isStatusChangeVisible; } set { SetProperty(ref _isStatusChangeVisible, value); } }
        public string StatusChangeTitle { get { return _statusChangeTitle; } private set { SetProperty(ref _statusChangeTitle, value); } }
        /// <summary>当前状态与变更规则的提示文本。</summary>
        public string StatusChangeSub { get { return _statusChangeSub; } private set { SetProperty(ref _statusChangeSub, value); } }
        public StatusTargetOption SelectedStatusTarget { get { return _selectedStatusTarget; } set { SetProperty(ref _selectedStatusTarget, value); } }
        /// <summary>变更原因（停用 / 报废必填，BR-EQP-01）。</summary>
        public string StatusChangeReason { get { return _statusChangeReason; } set { SetProperty(ref _statusChangeReason, value); } }
        public string StatusChangeError { get { return _statusChangeError; } private set { SetProperty(ref _statusChangeError, value); } }
        public IRelayCommand<DeviceRow> ChangeStatusCommand { get; }
        public IAsyncRelayCommand ConfirmStatusChangeCommand { get; }
        public IRelayCommand CancelStatusChangeCommand { get; }
        public IAsyncRelayCommand ExportCommand { get; }
        public IRelayCommand<PageNumber> GoToPageCommand { get; }
        public IRelayCommand PrevPageCommand { get; }
        public IRelayCommand NextPageCommand { get; }

        private DeviceQueryRequest BuildQuery()
        {
            var query = new DeviceQueryRequest { PageIndex = _pageIndex, PageSize = PageSize, Keyword = Keyword };
            if (_typeFilterId > 0) query.TypeId = _typeFilterId;
            if (_selectedLocation != null && !string.IsNullOrWhiteSpace(_selectedLocation.Value)) query.Location = _selectedLocation.Value.Trim();
            switch (_statusFilter)
            {
                case 1: query.Status = DeviceStatus.InUse; break;
                case 2: query.Status = DeviceStatus.Repairing; break;
                case 3: query.HasPendingFault = true; break; // 故障（存在待接单故障单的派生展示态）
                case 4: query.Status = DeviceStatus.Disabled; break;
                case 5: query.Status = DeviceStatus.Scrapped; break;
                case 6: query.StatusIn = new List<int> { (int)DeviceStatus.Disabled, (int)DeviceStatus.Scrapped }; break;
            }
            return query;
        }

        private void GoToPage(PageNumber page)
        {
            if (page == null || page.Index == _pageIndex) return;
            _pageIndex = page.Index;
            _ = LoadAsync();
        }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var page = await Api.QueryDevicesAsync(BuildQuery());
                Items.Clear(); foreach (var dto in page.Items) Items.Add(WrapRow(dto));
                _totalCount = page.Total;
                _pageCount = PageSize > 0 ? (page.Total + PageSize - 1) / PageSize : 1;
                if (_pageCount < 1) _pageCount = 1;
                if (_pageIndex > _pageCount)
                {
                    _pageIndex = _pageCount;
                    page = await Api.QueryDevicesAsync(BuildQuery());
                    Items.Clear(); foreach (var dto in page.Items) Items.Add(WrapRow(dto));
                    _totalCount = page.Total;
                }
                OnPropertyChanged(nameof(PageIndex)); OnPropertyChanged(nameof(PageCount)); OnPropertyChanged(nameof(PageText));
                PageNumbers.Clear();
                for (int i = 1; i <= (_pageCount > 8 ? 8 : _pageCount); i++)
                    PageNumbers.Add(new PageNumber { Index = i, IsCurrent = i == _pageIndex });
                if (Types.Count == 0)
                {
                    foreach (var t in await Api.GetDeviceTypesAsync()) Types.Add(t);
                    TypeFilterItems.Add(new DeviceTypeDto { Id = 0, Name = "类别：全部" });
                    foreach (var t in Types) TypeFilterItems.Add(new DeviceTypeDto { Id = t.Id, Name = "类别：" + t.Name });
                    // 位置下拉取值：一次性读取在册设备位置去重（原型"位置：全部"下拉）
                    var all = await Api.QueryDevicesAsync(new DeviceQueryRequest { PageIndex = 1, PageSize = 500 });
                    LocationFilterItems.Add(new LocationOption { Value = null, Label = "位置：全部" });
                    foreach (var loc in (all.Items ?? new List<DeviceDto>()).Select(x => x.Location)
                        .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Distinct().OrderBy(l => l))
                        LocationFilterItems.Add(new LocationOption { Value = loc, Label = "位置：" + loc });
                    SelectedLocation = LocationFilterItems[0];
                }
                // 统计卡走后端 summary 端点（不再拉 1000 条内存计算）
                var summary = await Api.GetDeviceSummaryAsync();
                Total = summary.Total;
                InUse = summary.InUse;
                Repairing = summary.Repairing;
                Disabled = summary.DisabledOrScrapped;
                TotalSub = summary.TypeCount + " 大类";
                InUseSub = summary.Total > 0 ? Math.Round(summary.InUse * 100.0 / summary.Total, 1).ToString("0.0") + "%" : "—";
                RepairingSub = "含故障 " + summary.FaultCount + " 台";
                DisabledSub = "待处置";
            }, "设备已加载");
        }

        private void StartNew()
        {
            _editId = null; FormTypeId = Types.Count > 0 ? Types[0].Id : 0; FormName = string.Empty; FormLocation = string.Empty; FormBrandModel = string.Empty; FormEnableDate = DateTime.Today; FormWarrantyEnd = null; FormContractEnd = null;
            FormTypeText = Types.Count > 0 ? Types[0].Name : string.Empty;
            IsTypeAdd = false; NewTypeName = string.Empty;
            IsDetailVisible = false; IsFormVisible = true;
        }

        private void StartEdit(DeviceRow row)
        {
            if (row == null) return;
            StartEditCore(row.Dto);
        }

        private void EditFromDetail()
        {
            if (DetailRow == null) return;
            StartEditCore(DetailRow.Dto);
        }

        private void StartEditCore(DeviceDto dto)
        {
            _editId = dto.Id; FormTypeId = dto.TypeId; FormName = dto.Name; FormLocation = dto.Location; FormBrandModel = dto.BrandModel; FormEnableDate = dto.EnableDate;
            var t = Types.FirstOrDefault(x => x.Id == dto.TypeId);
            FormTypeText = t == null ? (dto.TypeName ?? string.Empty) : t.Name;
            IsTypeAdd = false; IsTypeDeleteVisible = false;
            DateTime w, c;
            FormWarrantyEnd = DateTime.TryParse(dto.WarrantyEnd, out w) ? w : (DateTime?)null;
            FormContractEnd = DateTime.TryParse(dto.ContractEnd, out c) ? c : (DateTime?)null;
            IsDetailVisible = false;
            IsFormVisible = true;
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(FormName)) { ErrorText = "设备名称不能为空"; return; }
            int typeId = await ResolveTypeIdAsync();
            if (typeId <= 0) { if (string.IsNullOrEmpty(ErrorText)) ErrorText = "请选择或输入设备类别"; return; }
            var request = new DeviceRequest { TypeId = typeId, Name = FormName.Trim(), Location = FormLocation, BrandModel = FormBrandModel, EnableDate = FormEnableDate, WarrantyEnd = FormWarrantyEnd, ContractEnd = FormContractEnd };
            await RunAsync(async () =>
            {
                if (_editId.HasValue) await Api.UpdateDeviceAsync(_editId.Value, request); else await Api.CreateDeviceAsync(request);
                IsFormVisible = false; await LoadAsync();
            }, "设备已保存");
        }

        /// <summary>设备类别下拉文本解析：命中已有类别返回其 Id；未命中则按默认周期（30 天）新建自定义类别（BR-EQP-05）。</summary>
        private async Task<int> ResolveTypeIdAsync()
        {
            string text = (FormTypeText ?? string.Empty).Trim();
            if (text.Length == 0) return _formTypeId > 0 ? _formTypeId : -1;
            var existing = Types.FirstOrDefault(t => string.Equals(t.Name, text, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing.Id;
            try
            {
                var created = await Api.CreateDeviceTypeAsync(new DeviceTypeRequest { Name = text, MaintenanceCycle = 30 });
                if (created != null)
                {
                    Types.Add(created);
                    TypeFilterItems.Add(new DeviceTypeDto { Id = created.Id, Name = "类别：" + created.Name });
                    FormTypeId = created.Id;
                    return created.Id;
                }
            }
            catch (ApiClientException ex) { ErrorText = ex.Message; return -1; }
            catch (Exception ex) { ErrorText = "创建设备类别失败：" + ex.Message; return -1; }
            return -1;
        }

        /// <summary>打开"＋新增"内联行（设备类别自定义新增）。</summary>
        private void OpenNewType()
        {
            NewTypeName = string.Empty;
            ErrorText = string.Empty;
            IsTypeAdd = true;
        }

        /// <summary>
        /// 保存自定义设备类别：仅需名称，保养周期按 P-04 缺省口径取 30 天（后续可在类别维护里调整），
        /// 保存后自动选中新类别。
        /// </summary>
        private async Task ConfirmNewTypeAsync()
        {
            string name = NewTypeName == null ? string.Empty : NewTypeName.Trim();
            if (name.Length == 0) { ErrorText = "请输入设备类别名称"; return; }
            if (Types.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))) { ErrorText = "设备类别已存在：" + name; return; }
            await RunAsync(async () =>
            {
                var created = await Api.CreateDeviceTypeAsync(new DeviceTypeRequest { Name = name, MaintenanceCycle = DefaultTypeCycle });
                Types.Add(created);
                TypeFilterItems.Add(new DeviceTypeDto { Id = created.Id, Name = "类别：" + created.Name });
                IsTypeAdd = false;
                NewTypeName = string.Empty;
                FormTypeText = created.Name;
                FormTypeId = created.Id;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "设备类别「" + created.Name + "」已新增（默认保养周期 " + DefaultTypeCycle + " 天）";
            }, "设备类别已新增");
        }

        /// <summary>请求删除当前设备类别：打开二次确认浮层。</summary>
        private void RequestDeleteType()
        {
            string current = (FormTypeText ?? string.Empty).Trim();
            var type = Types.FirstOrDefault(t => string.Equals(t.Name, current, StringComparison.OrdinalIgnoreCase))
                       ?? Types.FirstOrDefault(t => t.Id == _formTypeId);
            if (type == null) { ErrorText = "请先选择需要删除的设备类别"; return; }
            _deleteTypeId = type.Id;
            DeleteTypeName = type.Name;
            IsTypeDeleteVisible = true;
        }

        /// <summary>确认删除设备类别（软删）：被在册设备引用时后端拒绝并回显原因。</summary>
        private async Task ConfirmDeleteTypeAsync()
        {
            int id = _deleteTypeId;
            string name = DeleteTypeName;
            await RunAsync(async () =>
            {
                await Api.DeleteDeviceTypeAsync(id);
                var removed = Types.FirstOrDefault(t => t.Id == id);
                if (removed != null) Types.Remove(removed);
                var filter = TypeFilterItems.FirstOrDefault(t => t.Id == id);
                if (filter != null) TypeFilterItems.Remove(filter);
                IsTypeDeleteVisible = false;
                if (Types.Count > 0)
                {
                    FormTypeId = Types[0].Id;
                    FormTypeText = Types[0].Name;
                }
                else
                {
                    FormTypeId = 0;
                    FormTypeText = string.Empty;
                }
                if (_typeFilterId == id) TypeFilterId = 0;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "设备类别「" + name + "」已删除";
                await LoadAsync();
            }, "设备类别已删除");
        }

        private DeviceRow WrapRow(DeviceDto dto)
        {
            var row = new DeviceRow { Dto = dto };
            row.PropertyChanged += Row_PropertyChanged;
            return row;
        }

        private void Row_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceRow.IsChecked))
            {
                OnPropertyChanged(nameof(HasChecked));
                OnPropertyChanged(nameof(IsAllChecked));
                OnPropertyChanged(nameof(CheckedCountText));
            }
        }

        /// <summary>进入批量删除模式：显示勾选列（参照纠纷列表）。</summary>
        private void EnterBatchMode()
        {
            foreach (var row in Items) row.IsChecked = false;
            ErrorText = string.Empty;
            IsBatchMode = true;
            OnPropertyChanged(nameof(HasChecked));
            OnPropertyChanged(nameof(IsAllChecked));
            OnPropertyChanged(nameof(CheckedCountText));
        }

        /// <summary>退出批量删除模式：隐藏勾选列并清空勾选。</summary>
        private void ExitBatchMode()
        {
            IsBatchMode = false;
            IsBatchConfirmVisible = false;
            foreach (var row in Items) row.IsChecked = false;
            OnPropertyChanged(nameof(HasChecked));
            OnPropertyChanged(nameof(IsAllChecked));
            OnPropertyChanged(nameof(CheckedCountText));
        }

        private void RequestBatchConfirm()
        {
            int count = Items.Count(x => x.IsChecked);
            if (count == 0) { ErrorText = "请先勾选要删除的设备"; return; }
            BatchConfirmText = "确认删除已勾选的 " + count + " 台设备？删除后不再展示，历史台账保留（软删）。";
            IsBatchConfirmVisible = true;
        }

        /// <summary>批量删除：逐台调用软删接口，汇总成功数并刷新列表（参照纠纷列表）。</summary>
        private async Task ConfirmBatchDeleteAsync()
        {
            var ids = Items.Where(x => x.IsChecked).Select(x => x.Dto.Id).ToList();
            if (ids.Count == 0) { IsBatchConfirmVisible = false; return; }
            int ok = 0;
            var failed = new List<string>();
            foreach (var id in ids)
            {
                var row = Items.FirstOrDefault(x => x.Dto.Id == id);
                try { await Api.DeleteDeviceAsync(id); ok++; }
                catch (ApiClientException ex) { failed.Add((row == null ? "设备" : row.DeviceNo) + "：" + ex.Message); }
                catch (Exception ex) { failed.Add((row == null ? "设备" : row.DeviceNo) + "：" + ex.Message); }
            }
            IsBatchConfirmVisible = false;
            await LoadAsync();
            StatusText = ok > 0
                ? DateTime.Now.ToString("HH:mm:ss ") + "已删除 " + ok + " 台设备" + (failed.Count > 0 ? "，跳过 " + failed.Count + " 台" : string.Empty)
                : DateTime.Now.ToString("HH:mm:ss ") + "未删除任何设备";
            if (failed.Count > 0) ErrorText = string.Join("；", failed);
            IsBatchMode = false;
        }

        /// <summary>详情浮层：设备信息 + 保养/年检/故障全记录（GetMaintenanceAsync/GetInspectionAsync/GetFaultsAsync）。</summary>
        private void OpenDetail(DeviceRow row)
        {
            if (row == null) return;
            DetailRow = row;
            DetailTabIndex = 0;
            MaintenanceRecords.Clear(); InspectionRecords.Clear(); FaultRecords.Clear(); CustomRecords.Clear();
            IsCustomRecordForm = false;
            _editingCustomRecordId = 0;
            NextMaintenanceEdit = row.Dto.NextMaintenanceOverride;
            NextMaintenanceSourceText = string.IsNullOrEmpty(row.Dto.NextMaintenanceSource) ? "—" : row.Dto.NextMaintenanceSource;
            OnPropertyChanged(nameof(CanEditNextMaintenance));
            OnPropertyChanged(nameof(CannotEditNextMaintenance));
            IsDetailVisible = true;
            _ = LoadDetailRecordsAsync(row.Dto.Id);
        }

        private async Task LoadDetailRecordsAsync(int deviceId)
        {
            await RunAsync(async () =>
            {
                var maint = await Api.GetMaintenanceAsync(deviceId);
                MaintenanceRecords.Clear();
                foreach (var m in maint.OrderByDescending(x => x.MDate)) MaintenanceRecords.Add(m);
                var insp = await Api.GetInspectionAsync(deviceId);
                InspectionRecords.Clear();
                foreach (var i in insp.OrderByDescending(x => x.IDate)) InspectionRecords.Add(i);
                var faults = await Api.GetFaultsAsync(deviceId);
                FaultRecords.Clear();
                foreach (var f in faults.OrderByDescending(x => x.FTime)) FaultRecords.Add(f);
                // R8：状态留痕（详情浮层第 5 个页签）
                var logs = await Api.GetDeviceStatusLogsAsync(deviceId);
                StatusLogs.Clear();
                foreach (var l in logs.OrderByDescending(x => x.ChangedAt)) StatusLogs.Add(new DeviceStatusLogRow { Dto = l });
                // R6：自定义类型记录 + 类型候选
                var customs = await Api.GetDeviceCustomRecordsAsync(deviceId);
                CustomRecords.Clear();
                foreach (var x in customs.OrderByDescending(x => x.RDate)) CustomRecords.Add(x);
                if (CustomRecordTypes.Count == 0)
                {
                    var typeNames = await Api.GetCustomRecordTypesAsync();
                    foreach (var n in typeNames) CustomRecordTypes.Add(n);
                }
                if (Vendors.Count == 0)
                {
                    var vendors = await Api.GetVendorsAsync();
                    foreach (var v in vendors) Vendors.Add(v);
                }
            }, null);
        }

        // ===================== R6：详情浮层「下次保养」自定义（日历组件） =====================

        /// <summary>保存自定义下次保养日期：仅提交该字段（其余字段沿用设备现值，避免误改）。</summary>
        private async Task SaveNextMaintenanceAsync()
        {
            if (DetailRow == null) return;
            if (!NextMaintenanceEdit.HasValue) { ErrorText = "请选择下次保养日期"; return; }
            if (NextMaintenanceEdit.Value.Date < DateTime.Today)
            {
                var ok = MessageBox.Show("所选日期早于今天（" + NextMaintenanceEdit.Value.ToString("yyyy-MM-dd") + "），仍要保存吗？",
                    "下次保养日期", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                if (!ok) return;
            }
            await RunAsync(async () =>
            {
                var dto = DetailRow.Dto;
                await Api.UpdateDeviceAsync(dto.Id, new DeviceRequest
                {
                    TypeId = dto.TypeId, Name = dto.Name, Location = dto.Location, EnableDate = dto.EnableDate,
                    BrandModel = dto.BrandModel, MaintenanceCycleOverride = dto.MaintenanceCycleOverride,
                    NextMaintenanceOverride = NextMaintenanceEdit.Value.Date
                });
                await RefreshDetailDeviceAsync(dto.Id);
            }, "下次保养时间已更新（来源：自定义）");
        }

        /// <summary>清空自定义日期 → 恢复按「最近保养 + 周期」派生（R6）。</summary>
        private async Task ClearNextMaintenanceAsync()
        {
            if (DetailRow == null) return;
            await RunAsync(async () =>
            {
                var dto = DetailRow.Dto;
                await Api.UpdateDeviceAsync(dto.Id, new DeviceRequest
                {
                    TypeId = dto.TypeId, Name = dto.Name, Location = dto.Location, EnableDate = dto.EnableDate,
                    BrandModel = dto.BrandModel, MaintenanceCycleOverride = dto.MaintenanceCycleOverride,
                    ClearNextMaintenance = true
                });
                await RefreshDetailDeviceAsync(dto.Id);
            }, "已恢复按保养周期派生下次保养时间");
        }

        /// <summary>重取设备（同步详情浮层与列表行，R6）。</summary>
        private async Task RefreshDetailDeviceAsync(int deviceId)
        {
            var fresh = await Api.GetDeviceAsync(deviceId);
            var row = Items.FirstOrDefault(x => x.Dto.Id == deviceId);
            if (row != null && fresh != null) row.Dto.NextMaintenanceOverride = fresh.NextMaintenanceOverride;
            if (row != null && fresh != null) row.Dto.NextMaintenanceSource = fresh.NextMaintenanceSource;
            if (fresh != null && DetailRow != null && DetailRow.Dto.Id == deviceId)
            {
                DetailRow.Dto.NextMaintenanceOverride = fresh.NextMaintenanceOverride;
                DetailRow.Dto.NextMaintenanceSource = fresh.NextMaintenanceSource;
                DetailRow.Dto.NextMaintenance = fresh.NextMaintenance;
                DetailRow.Dto.NextMaintenanceText = fresh.NextMaintenanceText;
            }
            NextMaintenanceEdit = fresh == null ? null : fresh.NextMaintenanceOverride;
            NextMaintenanceSourceText = fresh == null || string.IsNullOrEmpty(fresh.NextMaintenanceSource) ? "—" : fresh.NextMaintenanceSource;
            OnPropertyChanged(nameof(CanEditNextMaintenance));
            OnPropertyChanged(nameof(CannotEditNextMaintenance));
        }

        // ===================== R6：详情浮层「自定义类型记录」 =====================

        // ===================== R8：人工状态变更（BR-EQP-01，停用 / 启用 / 报废 / 维修中） =====================

        /// <summary>合法迁移表（与服务端 EquipmentService.AllowedTransitions 一致，用于目标候选与前置校验）。</summary>
        private static readonly Dictionary<DeviceStatus, DeviceStatus[]> AllowedStatusTransitions = new Dictionary<DeviceStatus, DeviceStatus[]>
        {
            { DeviceStatus.InUse, new[] { DeviceStatus.Repairing, DeviceStatus.Disabled, DeviceStatus.Scrapped } },
            { DeviceStatus.Repairing, new[] { DeviceStatus.InUse, DeviceStatus.Scrapped } },
            { DeviceStatus.Disabled, new[] { DeviceStatus.InUse, DeviceStatus.Scrapped } },
            { DeviceStatus.Scrapped, new DeviceStatus[0] }
        };

        private static string StatusName(DeviceStatus status)
        {
            switch (status)
            {
                case DeviceStatus.InUse: return "在用";
                case DeviceStatus.Repairing: return "维修中";
                case DeviceStatus.Disabled: return "停用";
                default: return "报废";
            }
        }

        /// <summary>打开状态变更弹层：按当前状态生成合法目标候选（报废为终态时给出提示）。</summary>
        private void OpenStatusChange(DeviceRow row)
        {
            if (row == null) return;
            _statusChangeTarget = row;
            DeviceStatus current = row.Dto.Status;
            StatusChangeTitle = row.DeviceNo + " · " + row.Name;
            StatusChangeSub = "当前状态：" + StatusName(current) + "；状态变更会写入设备状态留痕";
            StatusChangeReason = string.Empty;
            StatusChangeError = string.Empty;
            StatusTargetOptions.Clear();
            DeviceStatus[] allowed;
            if (AllowedStatusTransitions.TryGetValue(current, out allowed))
            {
                foreach (var s in allowed) StatusTargetOptions.Add(new StatusTargetOption { Status = s, Text = StatusName(s) });
            }
            SelectedStatusTarget = StatusTargetOptions.FirstOrDefault();
            if (StatusTargetOptions.Count == 0) StatusChangeError = "该设备已「报废」（终态），不可再变更状态";
            IsStatusChangeVisible = true;
        }

        /// <summary>确认变更：停用 / 报废必须填原因；报废为终态需二次确认。</summary>
        private async Task ConfirmStatusChangeAsync()
        {
            if (_statusChangeTarget == null || SelectedStatusTarget == null)
            {
                StatusChangeError = "请选择要变更到的状态";
                return;
            }
            DeviceStatus target = SelectedStatusTarget.Status;
            string reason = (StatusChangeReason ?? string.Empty).Trim();
            if ((target == DeviceStatus.Disabled || target == DeviceStatus.Scrapped) && reason.Length == 0)
            {
                StatusChangeError = target == DeviceStatus.Scrapped ? "报废必须填写原因（BR-EQP-01）" : "停用必须填写原因（BR-EQP-01）";
                return;
            }
            if (target == DeviceStatus.Scrapped)
            {
                var ok = MessageBox.Show("报废为终态：报废后不可再改为在用 / 停用 / 维修中。确认报废「" + _statusChangeTarget.Name + "」？",
                    "设备报废确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                if (!ok) return;
            }
            var row = _statusChangeTarget;
            string msg = null;
            await RunAsync(async () =>
            {
                var dto = await Api.ChangeDeviceStatusAsync(row.Dto.Id, new DeviceStatusRequest
                {
                    Status = target,
                    Reason = reason.Length == 0 ? null : reason
                });
                msg = "设备状态已变更为「" + (dto == null || string.IsNullOrEmpty(dto.StatusText) ? StatusName(target) : dto.StatusText) + "」";
                IsStatusChangeVisible = false;
                _statusChangeTarget = null;
                await LoadAsync();
                if (DetailRow != null && DetailRow.Dto.Id == row.Dto.Id) await RefreshDetailDeviceAsync(row.Dto.Id);
            }, null);
            if (!string.IsNullOrEmpty(msg)) StatusText = DateTime.Now.ToString("HH:mm:ss ") + msg;
        }

        // ===================== R6：详情浮层「自定义类型记录」（续） =====================

        private void OpenAddCustomRecord()
        {
            _editingCustomRecordId = 0;
            CustomTypeName = string.Empty;
            CustomRDate = DateTime.Today;
            CustomContent = string.Empty;
            CustomResult = "合格";
            CustomCostText = string.Empty;
            CustomVendorId = 0;
            CustomOperator = string.Empty;
            ErrorText = string.Empty;
            OnPropertyChanged(nameof(CustomRecordFormTitle));
            IsCustomRecordForm = true;
        }

        private void LoadCustomRecordForEdit(DeviceCustomRecordDto record)
        {
            if (record == null) return;
            _editingCustomRecordId = record.Id;
            CustomTypeName = record.TypeName;
            CustomRDate = record.RDate;
            CustomContent = record.Content ?? string.Empty;
            CustomResult = string.IsNullOrEmpty(record.Result) ? "合格" : record.Result;
            CustomCostText = record.Cost.HasValue ? record.Cost.Value.ToString("0.00") : string.Empty;
            CustomVendorId = record.VendorId ?? 0;
            CustomOperator = record.Operator ?? string.Empty;
            ErrorText = string.Empty;
            OnPropertyChanged(nameof(CustomRecordFormTitle));
            IsCustomRecordForm = true;
        }

        private async Task SaveCustomRecordAsync()
        {
            if (DetailRow == null) return;
            if (string.IsNullOrWhiteSpace(CustomTypeName)) { ErrorText = "自定义类型名称不能为空"; return; }
            decimal? cost = null;
            if (!string.IsNullOrWhiteSpace(CustomCostText))
            {
                decimal parsed;
                if (!decimal.TryParse(CustomCostText.Trim(), out parsed) || parsed < 0) { ErrorText = "费用应为不小于 0 的数字"; return; }
                cost = parsed;
            }
            int deviceId = DetailRow.Dto.Id;
            bool isEdit = _editingCustomRecordId > 0;
            int editId = _editingCustomRecordId;
            var request = new DeviceCustomRecordRequest
            {
                DeviceId = deviceId, TypeName = CustomTypeName.Trim(), RDate = CustomRDate,
                Content = (CustomContent ?? string.Empty).Trim(), Result = (CustomResult ?? string.Empty).Trim(),
                Cost = cost, VendorId = CustomVendorId > 0 ? (int?)CustomVendorId : null,
                Operator = (CustomOperator ?? string.Empty).Trim()
            };
            await RunAsync(async () =>
            {
                if (isEdit) await Api.UpdateDeviceCustomRecordAsync(editId, request);
                else await Api.CreateDeviceCustomRecordAsync(deviceId, request);
                IsCustomRecordForm = false;
                _editingCustomRecordId = 0;
                var list = await Api.GetDeviceCustomRecordsAsync(deviceId);
                CustomRecords.Clear();
                foreach (var x in list.OrderByDescending(x => x.RDate)) CustomRecords.Add(x);
                if (!string.IsNullOrWhiteSpace(request.TypeName) && !CustomRecordTypes.Contains(request.TypeName))
                    CustomRecordTypes.Add(request.TypeName);
            }, isEdit ? "自定义类型记录已更新" : "自定义类型记录已新增");
        }

        private void RequestDeleteCustomRecord(DeviceCustomRecordDto record)
        {
            if (record == null) return;
            _deleteCustomRecordId = record.Id;
            CustomRecordDeleteText = "确认删除自定义类型记录「" + record.TypeName + " · " + record.RDate.ToString("yyyy-MM-dd") + "」？删除后不再展示（软删，历史留存可审计）。";
            IsCustomRecordDeleteVisible = true;
        }

        private async Task ConfirmDeleteCustomRecordAsync()
        {
            if (DetailRow == null || _deleteCustomRecordId <= 0) return;
            int id = _deleteCustomRecordId;
            int deviceId = DetailRow.Dto.Id;
            await RunAsync(async () =>
            {
                await Api.DeleteDeviceCustomRecordAsync(id);
                IsCustomRecordDeleteVisible = false;
                _deleteCustomRecordId = 0;
                var list = await Api.GetDeviceCustomRecordsAsync(deviceId);
                CustomRecords.Clear();
                foreach (var x in list.OrderByDescending(x => x.RDate)) CustomRecords.Add(x);
            }, "自定义类型记录已删除");
        }

        /// <summary>报修：弹窗收集故障现象/级别/发现人/发现时间 → AddFaultAsync 生成 WX 故障单（原"维修"按钮直接翻状态已废弃）。</summary>
        private void OpenRepair(DeviceRow row)
        {
            if (row == null) return;
            if (!row.CanRepair) { ErrorText = "停用 / 报废设备不可报修"; return; }
            _repairTargetId = row.Dto.Id;
            RepairTargetText = row.DeviceNo + " · " + row.Name + " · " + row.Location;
            RepairSymptom = string.Empty;
            RepairLevelIndex = 0;
            RepairReporter = string.Empty;
            RepairFTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            IsRepairVisible = true;
        }

        private async Task SubmitRepairAsync()
        {
            if (_repairTargetId <= 0) { ErrorText = "请选择报修设备"; return; }
            if (string.IsNullOrWhiteSpace(RepairSymptom)) { ErrorText = "故障现象不能为空（BR-EQP-04）"; return; }
            DateTime ftime;
            if (!DateTime.TryParse(RepairFTimeText, out ftime)) { ErrorText = "发现时间格式应为 yyyy-MM-dd HH:mm"; return; }
            var request = new FaultRecordRequest
            {
                DeviceId = _repairTargetId,
                FTime = ftime,
                Symptom = RepairSymptom.Trim(),
                Level = _repairLevelIndex,
                Reporter = RepairReporter.Trim()
            };
            await RunAsync(async () =>
            {
                var dto = await Api.AddFaultAsync(_repairTargetId, request);
                IsRepairVisible = false;
                await LoadAsync();
                string no = dto == null ? null : dto.FaultNo;
                MessageBox.Show(string.IsNullOrEmpty(no)
                    ? "报修成功，已登记故障记录。"
                    : "报修成功，已生成故障单 " + no + "。可在「故障登记」页跟进处理。",
                    "报修成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }, "报修已提交");
        }

        /// <summary>
        /// 导出：服务端生成 Excel（列宽自适应防遮挡），默认另存到本机「文档」目录
        /// （C:\Users\&lt;用户&gt;\Documents），与房产列表/车位/业主档案导出保持同一口径。
        /// </summary>
        private async Task ExportAsync()
        {
            string message = null;
            await RunAsync(async () =>
            {
                var result = await Api.ExportDevicesAsync(BuildQuery());
                string directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string fileName = string.IsNullOrWhiteSpace(result.FileName)
                    ? "设备台账_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xlsx"
                    : result.FileName;
                string path = Path.Combine(directory, fileName);
                await Api.DownloadExportFileAsync(result.Id, path);
                message = "已导出 " + result.Total + " 条设备记录到：" + path;
                MessageBox.Show("已导出 " + result.Total + " 条设备记录至：\n" + path, "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }, null);
            // RunAsync 无消息参数时会清空状态行，故导出结果提示在链路结束后回写
            if (!string.IsNullOrEmpty(message)) StatusText = DateTime.Now.ToString("HH:mm:ss ") + message;
        }
    }
}
