using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Org;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>筛选下拉项（Value=null 表示「全部」）。</summary>
    public class OrgFilterItem
    {
        public int? Value { get; set; }
        public string Label { get; set; }
    }

    /// <summary>员工列表行（PG-ORG-01）：状态彩色标签（在岗绿/休假蓝/离岗橙/离职灰）+ 按状态区分操作列。</summary>
    public class EmployeeRow : ObservableObject
    {
        private bool _isChecked;
        public EmployeeDto Dto { get; set; }

        /// <summary>批量选择标记。</summary>
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }

        public string EmpNo { get { return Dto.EmpNo ?? ("YG-" + Dto.Id.ToString("000")); } }
        public string Name { get { return Dto.Name ?? string.Empty; } }
        public string DeptName { get { return Dto.DeptName ?? string.Empty; } }
        public string PositionName { get { return Dto.PositionName ?? string.Empty; } }
        public string HireDate { get { return Dto.HireDate == default ? "—" : Dto.HireDate.ToString("yyyy-MM-dd"); } }

        /// <summary>联系电话：展示完整号码（不再对中间 4 位做 * 脱敏）。</summary>
        public string Phone
        {
            get
            {
                if (!string.IsNullOrEmpty(Dto.Phone)) { return Dto.Phone; }
                return Dto.PhoneMask ?? string.Empty;
            }
        }

        public string StatusText { get { return string.IsNullOrEmpty(Dto.StatusText) ? StatusName(Dto.Status) : Dto.StatusText; } }

        public Brush StatusBg
        {
            get
            {
                switch (Dto.Status)
                {
                    case EmployeeStatus.Active: return Solid("#E8F7F1");   // 在岗 绿
                    case EmployeeStatus.Vacation: return Solid("#EAF3FF"); // 休假 蓝
                    case EmployeeStatus.OffDuty: return Solid("#FFF5DC");  // 离岗 橙
                    default: return Solid("#F2F4F8");                      // 离职 灰
                }
            }
        }

        public Brush StatusFg
        {
            get
            {
                switch (Dto.Status)
                {
                    case EmployeeStatus.Active: return Solid("#12805C");
                    case EmployeeStatus.Vacation: return Solid("#2B7DE9");
                    case EmployeeStatus.OffDuty: return Solid("#B76E00");
                    default: return Solid("#98A2B3");
                }
            }
        }

        /// <summary>非离职行显示「离职」入口（ResignEmployeeAsync 离职流程）。</summary>
        public bool ShowResign { get { return Dto.Status != EmployeeStatus.Resigned; } }

        /// <summary>离职行显示「交接 / 状态记录」与「删除档案」（DeleteEmployeeAsync 仅允许对已离职员工）。</summary>
        public bool ShowResignedOps { get { return Dto.Status == EmployeeStatus.Resigned; } }

        public Brush ResignFg { get { return Solid("#D64545"); } }

        public string RowKey { get { return EmpNo + " " + Name; } }

        internal static string StatusName(EmployeeStatus s)
        {
            switch (s)
            {
                case EmployeeStatus.Active: return "在岗";
                case EmployeeStatus.OffDuty: return "离岗";
                case EmployeeStatus.Vacation: return "休假";
                default: return "离职";
            }
        }

        private static Brush Solid(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    /// <summary>在岗状态变更留痕行（交接 / 状态记录弹窗，t_employee_status_log）。</summary>
    public class EmployeeStatusLogRow
    {
        public string ChangedAtText { get; set; }
        public string StatusText { get; set; }
    }

    /// <summary>员工列表页（PG-ORG-01，UC-ORG-002/005/006，BR-ORG-02）。
    /// 离职走 ResignEmployeeAsync（档案保留+账号停用+电话簿联动，服务端事务）；删除档案仅对已离职员工可见。</summary>
    public class EmployeeListViewModel : BaseInfoPageViewModel
    {
        private const int PageSize = 20;

        private string _keyword = string.Empty;
        private int? _deptFilter;
        private int? _positionFilter;
        private int? _statusFilter;
        private int _positionLoadVersion;
        private int _pageIndex = 1;
        private int _total;
        private bool _isFormVisible;
        private string _formTitle = "新增员工";
        private bool _isEdit;
        private int? _editId;
        private int _formDeptId;
        private int _formPositionId;
        private string _formName = string.Empty;
        private string _formPhone = string.Empty;
        private DateTime _formHireDate = DateTime.Today;
        private EmployeeRow _resignRow;
        private EmployeeRow _deleteRow;
        private EmployeeRow _logRow;
        private readonly DispatcherTimer _searchDebounce;
        private bool _isDeptAdd;
        private string _newDeptName = string.Empty;
        private bool _isPositionAdd;
        private string _newPositionName = string.Empty;
        private bool _isSelectAll;
        private bool _isBatchMode;
        private bool _isBatchConfirmVisible;
        private string _batchConfirmMessage = string.Empty;
        private List<EmployeeRow> _batchRows;
        private bool _isDeptDeleteVisible;
        private int _deleteDeptId;
        private string _deleteDeptName = string.Empty;
        private bool _isPositionDeleteVisible;
        private int _deletePositionId;
        private string _deletePositionName = string.Empty;
        private bool _filterDefaultsApplied;

        public EmployeeListViewModel(IApiClient api) : base(api)
        {
            QueryCommand = new AsyncRelayCommand(() => SearchAsync());
            SearchCommand = new AsyncRelayCommand(() => SearchAsync()); // 回车触发（防抖：不做每键查询）
            NewCommand = new RelayCommand(StartNew);
            EditCommand = new RelayCommand<EmployeeRow>(r => StartEdit(r));
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(() => IsFormVisible = false);
            ResignCommand = new RelayCommand<EmployeeRow>(r => ResignRow = r);
            ConfirmResignCommand = new AsyncRelayCommand(ConfirmResignAsync);
            CancelResignCommand = new RelayCommand(() => ResignRow = null);
            DeleteArchiveCommand = new RelayCommand<EmployeeRow>(r => DeleteRow = r);
            ConfirmDeleteArchiveCommand = new AsyncRelayCommand(ConfirmDeleteArchiveAsync);
            CancelDeleteArchiveCommand = new RelayCommand(() => DeleteRow = null);
            HandoverCommand = new AsyncRelayCommand<EmployeeRow>(OpenHandoverAsync);
            CloseHandoverCommand = new RelayCommand(() => LogRow = null);
            BatchDeleteCommand = new RelayCommand(EnterBatchMode);
            ConfirmSelectionCommand = new RelayCommand(RequestBatchConfirm);
            ConfirmBatchDeleteCommand = new AsyncRelayCommand(ConfirmBatchDeleteAsync);
            CancelBatchDeleteCommand = new RelayCommand(ExitBatchMode);
            ExportCommand = new AsyncRelayCommand(ExportCsvAsync);
            PrevPageCommand = new RelayCommand(() => { if (_pageIndex > 1) { _pageIndex--; _ = SearchAsync(); } });
            NextPageCommand = new RelayCommand(() => { if (_pageIndex < TotalPages) { _pageIndex++; _ = SearchAsync(); } });
            OpenNewDeptCommand = new RelayCommand(() => { NewDeptName = string.Empty; IsDeptAdd = true; });
            CancelNewDeptCommand = new RelayCommand(() => IsDeptAdd = false);
            ConfirmNewDeptCommand = new AsyncRelayCommand(ConfirmNewDeptAsync);
            DeleteDeptCommand = new RelayCommand(RequestDeleteDept);
            CancelDeleteDeptCommand = new RelayCommand(() => IsDeptDeleteVisible = false);
            ConfirmDeleteDeptCommand = new AsyncRelayCommand(ConfirmDeleteDeptAsync);
            OpenNewPositionCommand = new RelayCommand(() => { NewPositionName = string.Empty; IsPositionAdd = true; });
            CancelNewPositionCommand = new RelayCommand(() => IsPositionAdd = false);
            ConfirmNewPositionCommand = new AsyncRelayCommand(ConfirmNewPositionAsync);
            DeletePositionCommand = new RelayCommand(RequestDeletePosition);
            CancelDeletePositionCommand = new RelayCommand(() => IsPositionDeleteVisible = false);
            ConfirmDeletePositionCommand = new AsyncRelayCommand(ConfirmDeletePositionAsync);
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounce.Tick += async (s, e) =>
            {
                _searchDebounce.Stop();
                _pageIndex = 1;
                await LoadAsync();
            };
            _ = LoadAsync();
        }

        public ObservableCollection<EmployeeRow> Items { get; } = new ObservableCollection<EmployeeRow>();
        public ObservableCollection<OrgFilterItem> DeptFilterItems { get; } = new ObservableCollection<OrgFilterItem>();
        public ObservableCollection<OrgFilterItem> PositionFilterItems { get; } = new ObservableCollection<OrgFilterItem>();
        public ObservableCollection<OrgFilterItem> StatusFilterItems { get; } = new ObservableCollection<OrgFilterItem>();
        public ObservableCollection<DepartmentDto> Departments { get; } = new ObservableCollection<DepartmentDto>();
        public ObservableCollection<PositionDto> Positions { get; } = new ObservableCollection<PositionDto>();
        public ObservableCollection<EmployeeStatusLogRow> LogRows { get; } = new ObservableCollection<EmployeeStatusLogRow>();

        public string Keyword
        {
            get { return _keyword; }
            set
            {
                if (SetProperty(ref _keyword, value))
                {
                    // 输入即自动排列数据：300ms 防抖重新查询
                    _searchDebounce?.Stop();
                    _searchDebounce?.Start();
                }
            }
        }

        public int? DeptFilter
        {
            get { return _deptFilter; }
            set
            {
                if (SetProperty(ref _deptFilter, value))
                {
                    _ = LoadPositionFilterAsync();
                    _pageIndex = 1;
                    _ = SearchAsync();
                }
            }
        }

        public int? PositionFilter { get { return _positionFilter; } set { if (SetProperty(ref _positionFilter, value)) { _pageIndex = 1; _ = SearchAsync(); } } }
        public int? StatusFilter { get { return _statusFilter; } set { if (SetProperty(ref _statusFilter, value)) { _pageIndex = 1; _ = SearchAsync(); } } }

        public int Total
        {
            get { return _total; }
            private set { SetProperty(ref _total, value); OnPropertyChanged(nameof(TotalText)); OnPropertyChanged(nameof(TotalPages)); }
        }

        public int TotalPages { get { return Math.Max(1, (int)Math.Ceiling(_total / (double)PageSize)); } }
        public string TotalText { get { return "共 " + _total + " 条记录 · 第 " + _pageIndex + "/" + TotalPages + " 页"; } }

        public bool IsFormVisible { get { return _isFormVisible; } set { SetProperty(ref _isFormVisible, value); } }
        public string FormTitle { get { return _formTitle; } private set { SetProperty(ref _formTitle, value); } }
        public int FormDeptId { get { return _formDeptId; } set { if (SetProperty(ref _formDeptId, value)) { _ = LoadPositionsAsync(); } } }
        public int FormPositionId { get { return _formPositionId; } set { SetProperty(ref _formPositionId, value); } }
        public string FormName { get { return _formName; } set { SetProperty(ref _formName, value); } }
        public string FormPhone { get { return _formPhone; } set { SetProperty(ref _formPhone, value); } }
        public DateTime FormHireDate { get { return _formHireDate; } set { SetProperty(ref _formHireDate, value); } }
        public bool IsDeptAdd { get { return _isDeptAdd; } set { SetProperty(ref _isDeptAdd, value); } }
        public string NewDeptName { get { return _newDeptName; } set { SetProperty(ref _newDeptName, value); } }
        public bool IsPositionAdd { get { return _isPositionAdd; } set { SetProperty(ref _isPositionAdd, value); } }
        public string NewPositionName { get { return _newPositionName; } set { SetProperty(ref _newPositionName, value); } }
        public bool IsDeptDeleteVisible { get { return _isDeptDeleteVisible; } set { SetProperty(ref _isDeptDeleteVisible, value); } }
        public string DeleteDeptName { get { return _deleteDeptName; } set { SetProperty(ref _deleteDeptName, value); } }
        public bool IsPositionDeleteVisible { get { return _isPositionDeleteVisible; } set { SetProperty(ref _isPositionDeleteVisible, value); } }
        public string DeletePositionName { get { return _deletePositionName; } set { SetProperty(ref _deletePositionName, value); } }

        public EmployeeRow ResignRow { get { return _resignRow; } set { SetProperty(ref _resignRow, value); OnPropertyChanged(nameof(IsResignVisible)); } }
        public bool IsResignVisible { get { return _resignRow != null; } }

        public EmployeeRow DeleteRow { get { return _deleteRow; } set { SetProperty(ref _deleteRow, value); OnPropertyChanged(nameof(IsDeleteVisible)); } }
        public bool IsDeleteVisible { get { return _deleteRow != null; } }

        public EmployeeRow LogRow { get { return _logRow; } set { SetProperty(ref _logRow, value); OnPropertyChanged(nameof(IsLogVisible)); } }
        public bool IsLogVisible { get { return _logRow != null; } }

        public bool IsSelectAll
        {
            get { return _isSelectAll; }
            set
            {
                if (SetProperty(ref _isSelectAll, value))
                {
                    foreach (var r in Items) { r.IsChecked = value; }
                    OnPropertyChanged(nameof(IsAllChecked));
                    OnPropertyChanged(nameof(CheckedCountText));
                }
            }
        }

        public bool IsBatchMode { get { return _isBatchMode; } set { SetProperty(ref _isBatchMode, value); OnPropertyChanged(nameof(IsNotBatchMode)); } }
        public bool IsNotBatchMode { get { return !_isBatchMode; } }
        public bool HasItemsChecked { get { return Items.Any(x => x.IsChecked); } }
        public bool IsAllChecked
        {
            get { return Items.Count > 0 && Items.All(x => x.IsChecked); }
            set
            {
                foreach (var r in Items) { r.IsChecked = value; }
                OnPropertyChanged(nameof(IsAllChecked));
                OnPropertyChanged(nameof(CheckedCountText));
            }
        }
        public string CheckedCountText { get { return "已选 " + Items.Count(x => x.IsChecked) + " / " + Items.Count + " 项"; } }

        public bool IsBatchConfirmVisible { get { return _isBatchConfirmVisible; } set { SetProperty(ref _isBatchConfirmVisible, value); } }
        public string BatchConfirmMessage { get { return _batchConfirmMessage; } private set { SetProperty(ref _batchConfirmMessage, value); } }

        public IAsyncRelayCommand QueryCommand { get; }
        public IAsyncRelayCommand SearchCommand { get; }
        public IRelayCommand NewCommand { get; }
        public IRelayCommand<EmployeeRow> EditCommand { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand<EmployeeRow> ResignCommand { get; }
        public IAsyncRelayCommand ConfirmResignCommand { get; }
        public IRelayCommand CancelResignCommand { get; }
        public IRelayCommand<EmployeeRow> DeleteArchiveCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteArchiveCommand { get; }
        public IRelayCommand CancelDeleteArchiveCommand { get; }
        public IAsyncRelayCommand<EmployeeRow> HandoverCommand { get; }
        public IRelayCommand CloseHandoverCommand { get; }
        public IRelayCommand BatchDeleteCommand { get; }
        public IRelayCommand ConfirmSelectionCommand { get; }
        public IAsyncRelayCommand ConfirmBatchDeleteCommand { get; }
        public IRelayCommand CancelBatchDeleteCommand { get; }
        public IAsyncRelayCommand ExportCommand { get; }
        public IRelayCommand PrevPageCommand { get; }
        public IRelayCommand NextPageCommand { get; }
        public IRelayCommand OpenNewDeptCommand { get; }
        public IRelayCommand CancelNewDeptCommand { get; }
        public IAsyncRelayCommand ConfirmNewDeptCommand { get; }
        public IRelayCommand DeleteDeptCommand { get; }
        public IRelayCommand CancelDeleteDeptCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteDeptCommand { get; }
        public IRelayCommand OpenNewPositionCommand { get; }
        public IRelayCommand CancelNewPositionCommand { get; }
        public IAsyncRelayCommand ConfirmNewPositionCommand { get; }
        public IRelayCommand DeletePositionCommand { get; }
        public IRelayCommand CancelDeletePositionCommand { get; }
        public IAsyncRelayCommand ConfirmDeletePositionCommand { get; }

        /// <summary>按当前筛选/页码查询（回车或筛选变化触发）。</summary>
        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var query = new EmployeeQueryRequest { PageIndex = _pageIndex, PageSize = PageSize, Keyword = Keyword };
                if (_deptFilter.HasValue) { query.DeptId = _deptFilter.Value; }
                if (_positionFilter.HasValue) { query.PositionId = _positionFilter.Value; }
                if (_statusFilter.HasValue) { query.Status = (EmployeeStatus)_statusFilter.Value; }
                var page = await Api.QueryEmployeesAsync(query);
                foreach (var r in Items) { r.PropertyChanged -= Row_PropertyChanged; }
                Items.Clear();
                foreach (var dto in page.Items) { Items.Add(CreateRow(dto)); }
                _isSelectAll = false;
                OnPropertyChanged(nameof(IsSelectAll));
                OnPropertyChanged(nameof(HasItemsChecked));
                OnPropertyChanged(nameof(IsAllChecked));
                OnPropertyChanged(nameof(CheckedCountText));
                Total = page.Total;
                if (Departments.Count == 0)
                {
                    foreach (var d in await Api.GetDepartmentsAsync()) { Departments.Add(d); }
                    BuildDeptFilterItems();
                }
                if (PositionFilterItems.Count == 0) { await LoadPositionFilterAsync(); }
                if (!_filterDefaultsApplied)
                {
                    // 下拉框默认选中「全部」（在岗状态同部门/岗位，避免首屏空白）
                    BuildStatusFilterItems();
                    _deptFilter = null; OnPropertyChanged(nameof(DeptFilter));
                    _positionFilter = null; OnPropertyChanged(nameof(PositionFilter));
                    _filterDefaultsApplied = true;
                }
            }, null);
        }

        /// <summary>回车搜索：重置到第一页。</summary>
        private async Task SearchAsync()
        {
            _pageIndex = 1;
            await LoadAsync();
        }

        private void BuildDeptFilterItems()
        {
            DeptFilterItems.Clear();
            DeptFilterItems.Add(new OrgFilterItem { Value = null, Label = "部门：全部" });
            foreach (var d in Departments) { DeptFilterItems.Add(new OrgFilterItem { Value = d.Id, Label = "部门：" + d.Name }); }
            // 默认选中「全部」（同岗位下拉：重置为 null 并通知，确保 ComboBox 定位到“全部”项）
            _deptFilter = null;
            OnPropertyChanged(nameof(DeptFilter));
        }

        /// <summary>打开表单前确保部门数据已加载（避免部门为空导致岗位下拉无数据）。</summary>
        private async Task EnsureDepartmentsLoadedAsync()
        {
            if (Departments.Count > 0) { return; }
            foreach (var d in await Api.GetDepartmentsAsync()) { Departments.Add(d); }
            BuildDeptFilterItems();
        }

        /// <summary>在岗状态筛选项：全部 / 在岗 / 休假 / 离岗 / 离职。</summary>
        private void BuildStatusFilterItems()
        {
            StatusFilterItems.Clear();
            StatusFilterItems.Add(new OrgFilterItem { Value = null, Label = "在岗状态：全部" });
            StatusFilterItems.Add(new OrgFilterItem { Value = (int)EmployeeStatus.Active, Label = "在岗状态：在岗" });
            StatusFilterItems.Add(new OrgFilterItem { Value = (int)EmployeeStatus.Vacation, Label = "在岗状态：休假" });
            StatusFilterItems.Add(new OrgFilterItem { Value = (int)EmployeeStatus.OffDuty, Label = "在岗状态：离岗" });
            StatusFilterItems.Add(new OrgFilterItem { Value = (int)EmployeeStatus.Resigned, Label = "在岗状态：离职" });
            _statusFilter = null;
            OnPropertyChanged(nameof(StatusFilter));
        }

        /// <summary>岗位筛选项随部门联动（全部 / 该部门岗位）。</summary>
        private async Task LoadPositionFilterAsync()
        {
            var positions = await Api.GetPositionsAsync(_deptFilter.HasValue ? _deptFilter.Value : (int?)null);
            PositionFilterItems.Clear();
            PositionFilterItems.Add(new OrgFilterItem { Value = null, Label = "岗位：全部" });
            foreach (var p in positions) { PositionFilterItems.Add(new OrgFilterItem { Value = p.Id, Label = "岗位：" + p.Name }); }
            _positionFilter = null;
            OnPropertyChanged(nameof(PositionFilter));
        }

        private async Task LoadPositionsAsync(int? selectId = null)
        {
            int version = ++_positionLoadVersion;
            int deptId = _formDeptId;
            Positions.Clear();
            // 重置岗位，避免陈旧值在选项切换/清空时触发 ComboBox“未能转换值”
            _formPositionId = 0;
            OnPropertyChanged(nameof(FormPositionId));
            if (deptId <= 0) { return; }
            var list = await Api.GetPositionsAsync(deptId);
            if (version != _positionLoadVersion) { return; } // 已被更新加载取代，丢弃旧结果
            Positions.Clear();
            foreach (var p in list) { Positions.Add(p); }
            if (Positions.Count > 0)
            {
                int target = selectId ?? Positions[0].Id;
                FormPositionId = Positions.Any(p => p.Id == target) ? target : Positions[0].Id;
            }
        }

        private async void StartNew()
        {
            FormTitle = "新增员工";
            _isEdit = false; _editId = null;
            await EnsureDepartmentsLoadedAsync();
            _formPositionId = 0; OnPropertyChanged(nameof(FormPositionId));
            _formDeptId = Departments.Count > 0 ? Departments[0].Id : 0;
            OnPropertyChanged(nameof(FormDeptId));
            FormName = string.Empty; FormPhone = string.Empty; FormHireDate = DateTime.Today;
            await LoadPositionsAsync();
            IsFormVisible = true;
        }

        private async void StartEdit(EmployeeRow row)
        {
            if (row == null) { return; }
            FormTitle = "编辑员工";
            _isEdit = true; _editId = row.Dto.Id;
            await EnsureDepartmentsLoadedAsync();
            _formPositionId = 0; OnPropertyChanged(nameof(FormPositionId));
            _formDeptId = row.Dto.DeptId;
            OnPropertyChanged(nameof(FormDeptId));
            FormName = row.Dto.Name; FormPhone = row.Dto.Phone; FormHireDate = row.Dto.HireDate;
            await LoadPositionsAsync(row.Dto.PositionId);
            IsFormVisible = true;
            // 拉取完整档案回填（列表接口电话脱敏，避免把掩码值存回）
            await RunAsync(async () =>
            {
                var dto = await Api.GetEmployeeAsync(row.Dto.Id);
                if (dto != null)
                {
                    FormName = dto.Name; FormPhone = dto.Phone; FormHireDate = dto.HireDate;
                    if (dto.DeptId > 0) { FormDeptId = dto.DeptId; }
                    await LoadPositionsAsync(dto.PositionId);
                }
            }, null);
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(FormName)) { ErrorText = "员工姓名不能为空"; return; }
            if (FormDeptId <= 0) { ErrorText = "请选择部门"; return; }
            if (FormPositionId <= 0) { ErrorText = "请选择岗位"; return; }
            var request = new EmployeeRequest
            {
                DeptId = FormDeptId, PositionId = FormPositionId,
                Name = FormName.Trim(), Phone = FormPhone ?? string.Empty, HireDate = FormHireDate
            };
            await RunAsync(async () =>
            {
                if (_isEdit && _editId.HasValue)
                {
                    await Api.UpdateEmployeeAsync(_editId.Value, request);
                    IsFormVisible = false;
                    await LoadAsync();
                    return;
                }

                await Api.CreateEmployeeAsync(request);
                IsFormVisible = false;
                await LoadAsync();
            }, "员工已保存");
        }

        /// <summary>新增自定义部门：录入名称后落库，并选中新部门。</summary>
        private async Task ConfirmNewDeptAsync()
        {
            if (string.IsNullOrWhiteSpace(NewDeptName)) { ErrorText = "请输入部门名称"; return; }
            await RunAsync(async () =>
            {
                var created = await Api.CreateDepartmentAsync(new DepartmentRequest { Name = NewDeptName.Trim() });
                IsDeptAdd = false;
                Departments.Clear();
                foreach (var d in await Api.GetDepartmentsAsync()) { Departments.Add(d); }
                BuildDeptFilterItems();
                _formDeptId = created.Id;
                OnPropertyChanged(nameof(FormDeptId));
                await LoadPositionsAsync();
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "部门「" + created.Name + "」已新增";
            }, null);
        }

        /// <summary>新增自定义岗位：录入名称后落库到当前部门，并选中新岗位。</summary>
        private async Task ConfirmNewPositionAsync()
        {
            if (string.IsNullOrWhiteSpace(NewPositionName)) { ErrorText = "请输入岗位名称"; return; }
            if (FormDeptId <= 0) { ErrorText = "请先选择/新增部门"; return; }
            await RunAsync(async () =>
            {
                var created = await Api.CreatePositionAsync(new PositionRequest { DeptId = FormDeptId, Name = NewPositionName.Trim() });
                IsPositionAdd = false;
                await LoadPositionsAsync(created.Id);
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "岗位「" + created.Name + "」已新增";
            }, null);
        }

        /// <summary>离职（BR-ORG-02）：服务端事务完成 档案保留+账号停用+权限回收+电话簿联动停用。</summary>
        private async Task ConfirmResignAsync()
        {
            var row = ResignRow;
            if (row == null) { return; }
            await RunAsync(async () =>
            {
                await Api.ResignEmployeeAsync(row.Dto.Id);
                ResignRow = null;
                await LoadAsync();
            }, "员工已离职（档案保留，账号停用，电话簿联动停用）");
        }

        /// <summary>删除档案：仅已离职员工可用（服务端校验，在职返回 409）。</summary>
        private async Task ConfirmDeleteArchiveAsync()
        {
            var row = DeleteRow;
            if (row == null) { return; }
            await RunAsync(async () =>
            {
                await Api.DeleteEmployeeAsync(row.Dto.Id);
                DeleteRow = null;
                await LoadAsync();
            }, "员工档案已删除");
        }

        private EmployeeRow CreateRow(EmployeeDto dto)
        {
            var row = new EmployeeRow { Dto = dto };
            row.PropertyChanged += Row_PropertyChanged;
            return row;
        }

        private void Row_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EmployeeRow.IsChecked))
            {
                OnPropertyChanged(nameof(HasItemsChecked));
                OnPropertyChanged(nameof(IsAllChecked));
                OnPropertyChanged(nameof(CheckedCountText));
            }
        }

        /// <summary>进入批量删除模式：显示勾选框并清除已勾选。</summary>
        private void EnterBatchMode()
        {
            IsBatchMode = true;
            foreach (var r in Items) { r.IsChecked = false; }
            OnPropertyChanged(nameof(HasItemsChecked));
            OnPropertyChanged(nameof(IsAllChecked));
            OnPropertyChanged(nameof(CheckedCountText));
            ErrorText = string.Empty;
        }

        /// <summary>退出批量删除模式：隐藏勾选框并清除已勾选与确认层。</summary>
        private void ExitBatchMode()
        {
            IsBatchMode = false;
            IsBatchConfirmVisible = false;
            _batchRows = null;
            foreach (var r in Items) { r.IsChecked = false; }
            OnPropertyChanged(nameof(HasItemsChecked));
            OnPropertyChanged(nameof(IsAllChecked));
            OnPropertyChanged(nameof(CheckedCountText));
        }

        /// <summary>批量删除：打开二次确认浮层（仅已离职员工可删除，在职员工服务端 409 跳过并汇总）。</summary>
        private void RequestBatchConfirm()
        {
            var rows = Items.Where(x => x.IsChecked).ToList();
            if (rows.Count == 0) { ErrorText = "请先勾选要删除的员工"; return; }
            string desc = string.Join("、", rows.Take(3).Select(r => r.Name + "(" + r.EmpNo + ")"));
            if (rows.Count > 3) { desc += " 等 " + rows.Count + " 人"; }
            _batchRows = rows;
            BatchConfirmMessage = "将删除 " + rows.Count + " 名员工（仅已离职员工可删除，在职员工将跳过）：\n" + desc;
            IsBatchConfirmVisible = true;
        }

        private async Task ConfirmBatchDeleteAsync()
        {
            var rows = _batchRows;
            if (rows == null || rows.Count == 0) { IsBatchConfirmVisible = false; return; }
            int ok = 0;
            var blocked = new List<string>();
            await RunAsync(async () =>
            {
                foreach (var r in rows)
                {
                    try { await Api.DeleteEmployeeAsync(r.Dto.Id); ok++; }
                    catch (ApiClientException ex) when (ex.Code == ErrorCode.Conflict)
                    {
                        blocked.Add(r.Name + "(" + r.EmpNo + ")：" + ex.Message);
                    }
                }
                IsBatchConfirmVisible = false;
                _batchRows = null;
                IsBatchMode = false;
                await LoadAsync();
                if (blocked.Count > 0)
                {
                    throw new ApiClientException(ErrorCode.Conflict,
                        "已删除 " + ok + " 条；跳过 " + string.Join("、", blocked.Take(3))
                        + (blocked.Count > 3 ? " 等 " + blocked.Count + " 条" : string.Empty));
                }
            }, "已批量删除 " + ok + " 条员工档案");
        }

        /// <summary>表单内删除部门：仅当部门下无员工时服务端允许（否则 409）。</summary>
        private void RequestDeleteDept()
        {
            if (_formDeptId <= 0) { ErrorText = "请先选择部门"; return; }
            var dept = Departments.FirstOrDefault(d => d.Id == _formDeptId);
            if (dept == null) { ErrorText = "请先选择部门"; return; }
            _deleteDeptId = dept.Id;
            DeleteDeptName = dept.Name;
            IsDeptDeleteVisible = true;
        }

        private async Task ConfirmDeleteDeptAsync()
        {
            await RunAsync(async () =>
            {
                await Api.DeleteDepartmentAsync(_deleteDeptId);
                IsDeptDeleteVisible = false;
                Departments.Clear();
                foreach (var d in await Api.GetDepartmentsAsync()) { Departments.Add(d); }
                BuildDeptFilterItems();
                _formDeptId = Departments.Count > 0 ? Departments[0].Id : 0;
                OnPropertyChanged(nameof(FormDeptId));
                await LoadPositionsAsync();
            }, "部门已删除");
        }

        /// <summary>表单内删除岗位：仅当岗位下无员工时服务端允许（否则 409）。</summary>
        private void RequestDeletePosition()
        {
            if (_formPositionId <= 0) { ErrorText = "请先选择岗位"; return; }
            var pos = Positions.FirstOrDefault(p => p.Id == _formPositionId);
            if (pos == null) { ErrorText = "请先选择岗位"; return; }
            _deletePositionId = pos.Id;
            DeletePositionName = pos.Name;
            IsPositionDeleteVisible = true;
        }

        private async Task ConfirmDeletePositionAsync()
        {
            await RunAsync(async () =>
            {
                await Api.DeletePositionAsync(_deletePositionId);
                IsPositionDeleteVisible = false;
                await LoadPositionsAsync();
            }, "岗位已删除");
        }

        /// <summary>交接 / 状态记录（离职行入口，t_employee_status_log 只追加留痕）。</summary>
        private async Task OpenHandoverAsync(EmployeeRow row)
        {
            if (row == null) { return; }
            await RunAsync(async () =>
            {
                var logs = await Api.GetEmployeeStatusLogsAsync(row.Dto.Id);
                LogRows.Clear();
                foreach (var l in logs.OrderByDescending(l => l.ChangedAt))
                {
                    LogRows.Add(new EmployeeStatusLogRow
                    {
                        ChangedAtText = l.ChangedAt == default ? "—" : l.ChangedAt.ToString("yyyy-MM-dd HH:mm"),
                        StatusText = EmployeeRow.StatusName(l.Status)
                    });
                }
                if (LogRows.Count == 0) { LogRows.Add(new EmployeeStatusLogRow { ChangedAtText = "—", StatusText = "暂无状态变更记录" }); }
                LogRow = row;
            }, null);
        }

        /// <summary>导出当前筛选结果为 CSV（SaveFileDialog + UTF-8；服务端 Excel 通道待中央扩展）。</summary>
        private async Task ExportCsvAsync()
        {
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                var all = new List<EmployeeDto>();
                int pageIndex = 1;
                int total;
                do
                {
                    var query = new EmployeeQueryRequest { PageIndex = pageIndex, PageSize = 200, Keyword = Keyword };
                    if (_deptFilter.HasValue) { query.DeptId = _deptFilter.Value; }
                    if (_positionFilter.HasValue) { query.PositionId = _positionFilter.Value; }
                    if (_statusFilter.HasValue) { query.Status = (EmployeeStatus)_statusFilter.Value; }
                    var page = await Api.QueryEmployeesAsync(query);
                    total = page.Total;
                    all.AddRange(page.Items);
                    pageIndex++;
                }
                while (all.Count < total && pageIndex <= 50);

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV 文件|*.csv",
                    FileName = "员工列表_" + DateTime.Now.ToString("yyyyMMdd") + ".csv"
                };
                if (dialog.ShowDialog() != true) { return; }

                var sb = new StringBuilder();
                sb.AppendLine("工号,姓名,部门,岗位,入职日期,联系电话,状态");
                foreach (var dto in all)
                {
                    var row = new EmployeeRow { Dto = dto };
                    sb.AppendLine(string.Join(",",
                        Csv(row.EmpNo), Csv(row.Name), Csv(row.DeptName), Csv(row.PositionName),
                        Csv(row.HireDate), Csv(row.Phone), Csv(row.StatusText)));
                }
                System.IO.File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已导出 " + all.Count + " 条：" + dialog.FileName;
            }
            catch (ApiClientException ex) { ErrorText = ex.Message; }
            catch (Exception ex) { ErrorText = "导出失败：" + ex.Message; }
            finally { IsBusy = false; }
        }

        private static string Csv(object value)
        {
            string s = value == null ? string.Empty : value.ToString();
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }
    }
}
