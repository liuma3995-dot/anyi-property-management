using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>房产列表页行（PG-INF-01）。</summary>
    public class PropertyRow : ObservableObject
    {
        private bool _isChecked;
        public PropertyDto Dto { get; set; }
        /// <summary>批量选择标记。</summary>
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }
        public int Id { get { return Dto.Id; } }
        public string NoText
        {
            get
            {
                string bld = Digits(Dto.BuildingNo).PadLeft(2, '0');
                string unit = Digits(Dto.UnitNo).PadLeft(2, '0');
                return "FC-" + (string.IsNullOrEmpty(bld + unit) ? Dto.Id.ToString("D4") : (bld + unit + "-" + Dto.RoomNo));
            }
        }
        public string RoomNo { get { return Dto.RoomNo; } }
        /// <summary>完整房号：楼栋(原值) + 单元(原值) + 房号；与表单/楼栋主数据同一口径（不做任何改写）。</summary>
        public string Path
        {
            get
            {
                string bld = Dto.BuildingNo ?? string.Empty;
                string unit = Dto.UnitNo ?? string.Empty;
                string room = Dto.RoomNo ?? string.Empty;
                return bld + unit + room;
            }
        }
        /// <summary>F-02：展示即存值（最多 2 位、去尾零、不四舍五入），不再强制 1 位小数。</summary>
        public string AreaText { get { return AreaValue.FormatWithUnit(Dto.Area); } }
        public string OwnerName { get { return string.IsNullOrEmpty(Dto.OwnerName) ? "未绑定" : Dto.OwnerName; } }
        public string OwnerPhone { get { return Dto.OwnerPhone ?? string.Empty; } }
        public string ArrearText { get { return "¥" + Dto.CurrentArrear.ToString("#,0.00"); } }
        public Brush ArrearBrush { get { return Dto.CurrentArrear > 0 ? DangerBrush : TextBrush; } }
        public string StatusText
        {
            get
            {
                switch (Dto.Status)
                {
                    case PropertyStatus.Occupied: return "已入住";
                    case PropertyStatus.Renovating: return "装修中";
                    default: return "空置";
                }
            }
        }

        /// <summary>
        /// CHG-v1.1.2-48：用途文案（列表新增「用途」列）—— 收费规格的「适用条件 → 房产用途」按同一口径取值。
        /// 注意与「状态（空置/已入住/装修中）」是两个维度。
        /// </summary>
        public string UsageText
        {
            get
            {
                switch (Dto.Usage)
                {
                    case PropertyUsage.Commercial: return "商铺";
                    case PropertyUsage.Vacant: return "空置";
                    default: return "住宅";
                }
            }
        }
        public Brush StatusBrush { get { return Dto.Status == PropertyStatus.Occupied ? OkBrush : (Dto.Status == PropertyStatus.Renovating ? WarnBrush : MutedBrush); } }
        public Brush StatusBg { get { return Dto.Status == PropertyStatus.Occupied ? OkBg : (Dto.Status == PropertyStatus.Renovating ? WarnBg : MutedBg); } }
        public decimal Arrear { get { return Dto.CurrentArrear; } }
        public bool HasArrear { get { return Dto.CurrentArrear > 0; } }

        private static readonly Brush OkBrush = Br("#12805C");
        private static readonly Brush OkBg = Br("#E8F7F1");
        private static readonly Brush WarnBrush = Br("#B54708");
        private static readonly Brush WarnBg = Br("#FEF0C7");
        private static readonly Brush MutedBrush = Br("#667085");
        private static readonly Brush MutedBg = Br("#F1F3F7");
        private static readonly Brush DangerBrush = Br("#D64545");
        private static readonly Brush TextBrush = Br("#172033");
        private static Brush Br(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        private static string Digits(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return new string(s.Where(char.IsDigit).ToArray());
        }
    }

    /// <summary>房产列表页（PG-INF-01，UC-INF-002，BR-INF-01）。</summary>
    public class PropertyListViewModel : BaseInfoPageViewModel
    {
        private int _pageIndex = 1;
        private int _pageSize = 20;
        private int _total;
        private string _searchText = string.Empty;
        private int? _buildingFilter = 0;
        private int _statusFilter;
        private int _arrearFilter;
        private bool _isFormVisible;
        private int _editingId;
        private string _formRoomNo = string.Empty;
        private int? _formBuildingId;
        private int? _formUnitId;
        private decimal _formArea;
        private string _formAreaText = string.Empty;
        private string _formAreaError = string.Empty;
        /// <summary>F-01：进入编辑时的原始面积，用于识别「存量高精度值原样提交」场景。</summary>
        private decimal _loadedArea;
        private PropertyUsage _formUsage = PropertyUsage.Residential;
        private PropertyStatus _formStatus = PropertyStatus.Vacant;
        private bool _isBuildingAdd;
        private bool _isUnitAdd;
        private string _newBuildingNo = string.Empty;
        private string _newUnitNo = string.Empty;
        private PropertyRow _viewRow;
        private bool _isDeleteConfirmVisible;
        private string _deleteConfirmTitle = string.Empty;
        private string _deleteConfirmMessage = string.Empty;
        private string _deleteConfirmSuccess = string.Empty;
        private Func<Task> _deleteConfirmAction;
        private bool _isSelectAll;
        /// <summary>
        /// 全选作用域标记（v1.2.0 第 3 轮）：勾选表头全选框 = 选中**当前筛选条件下的全部房产**（含其它页面）。
        /// 现场口径：勾了全选却只删掉当前页 → 必须删除当前筛选结果的全部数据。
        /// </summary>
        private bool _isSelectAllFiltered;
        private readonly DispatcherTimer _searchDebounce;

        public PropertyListViewModel(IApiClient api) : base(api)
        {
            NewCommand = new RelayCommand(StartNew);
            EditCommand = new RelayCommand<PropertyRow>(StartEdit);
            ViewCommand = new RelayCommand<PropertyRow>(r => { ViewRow = r; });
            QueryCommand = new AsyncRelayCommand(LoadAsync);
            DeleteCommand = new RelayCommand<PropertyRow>(r =>
            {
                if (r == null) return;
                ConfigureDeleteConfirm("确认删除房产？", "房产：" + r.Path + "\n删除为软删除，且需该房产无业主绑定关系（有绑定关系请先解除）。", "房产已删除（软删除）", () => DeletePropertyCoreAsync(r));
            });
            BatchDeleteCommand = new RelayCommand(RequestBatchDelete);
            ExportCommand = new AsyncRelayCommand(ExportAsync);
            PrevPageCommand = new RelayCommand(() => { if (_pageIndex > 1) { _pageIndex--; _ = LoadAsync(); } });
            NextPageCommand = new RelayCommand(() => { if (_pageIndex * _pageSize < _total) { _pageIndex++; _ = LoadAsync(); } });
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(() => IsFormVisible = false);
            CloseViewCommand = new RelayCommand(() => ViewRow = null);
            OpenNewBuildingCommand = new RelayCommand(() => { NewBuildingNo = string.Empty; IsBuildingAdd = true; });
            CancelNewBuildingCommand = new RelayCommand(() => IsBuildingAdd = false);
            ConfirmNewBuildingCommand = new AsyncRelayCommand(ConfirmNewBuildingAsync);
            OpenNewUnitCommand = new RelayCommand(() => { NewUnitNo = string.Empty; IsUnitAdd = true; });
            CancelNewUnitCommand = new RelayCommand(() => IsUnitAdd = false);
            ConfirmNewUnitCommand = new AsyncRelayCommand(ConfirmNewUnitAsync);
            DeleteBuildingCommand = new RelayCommand(() =>
            {
                if (!FormBuildingId.HasValue) { ErrorText = "请先选择要删除的楼栋"; return; }
                var building = Buildings.FirstOrDefault(x => x.Id == FormBuildingId.Value);
                if (building == null) return;
                int bid = building.Id;
                ConfigureDeleteConfirm("确认删除楼栋？", "楼栋：" + building.BuildingNo + "\n删除为软删除，且需该楼栋下无单元。", "楼栋已删除", () => DeleteBuildingCoreAsync(bid));
            });
            DeleteUnitCommand = new RelayCommand(() =>
            {
                if (!FormUnitId.HasValue || FormUnitId.Value <= 0) { ErrorText = "请先选择要删除的单元"; return; }
                if (!FormBuildingId.HasValue) { ErrorText = "请先选择/新增楼栋"; return; }
                var unit = Units.FirstOrDefault(x => x.Id == FormUnitId.Value);
                if (unit == null) return;
                int uid = unit.Id;
                ConfigureDeleteConfirm("确认删除单元？", "单元：" + unit.UnitNo + "\n删除为软删除，且需该单元下无房产。", "单元已删除", () => DeleteUnitCoreAsync(uid));
            });
            ConfirmDeleteCommand = new AsyncRelayCommand(ConfirmDeleteAsync);
            CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirmVisible = false);
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounce.Tick += async (s, e) =>
            {
                _searchDebounce.Stop();
                _pageIndex = 1;
                await LoadAsync();
            };
            _ = InitAsync();
        }

        public ObservableCollection<PropertyRow> Items { get; } = new ObservableCollection<PropertyRow>();
        public ObservableCollection<BuildingDto> Buildings { get; } = new ObservableCollection<BuildingDto>();
        public ObservableCollection<BuildingDto> BuildingFilterItems { get; } = new ObservableCollection<BuildingDto>();
        public ObservableCollection<UnitDto> Units { get; } = new ObservableCollection<UnitDto>();

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _searchDebounce?.Stop();
                    _searchDebounce?.Start();
                }
            }
        }
        public int? BuildingFilter { get { return _buildingFilter; } set { if (SetProperty(ref _buildingFilter, value)) { _ = LoadAsync(); } } }
        public int StatusFilter { get { return _statusFilter; } set { if (SetProperty(ref _statusFilter, value)) { _ = LoadAsync(); } } }
        public int ArrearFilter { get { return _arrearFilter; } set { if (SetProperty(ref _arrearFilter, value)) { _ = LoadAsync(); } } }
        public int Total { get { return _total; } private set { SetProperty(ref _total, value); OnPropertyChanged(nameof(PageInfo)); OnPropertyChanged(nameof(TotalText)); } }
        public int PageIndex { get { return _pageIndex; } set { _pageIndex = value; } }
        public string PageInfo { get { return _total == 0 ? "共 0 条记录 · 第 0/1 页" : ("共 " + _total + " 条记录 · 第 " + _pageIndex + "/" + ((_total + _pageSize - 1) / _pageSize) + " 页"); } }
        public string TotalText { get { return PageInfo; } }
        public string BuildingFilterText { get { return _buildingFilter.HasValue && _buildingFilter.Value > 0 ? ("楼栋：" + (Buildings.FirstOrDefault(x => x.Id == _buildingFilter.Value)?.BuildingNo ?? "")) : "楼栋：全部"; } }
        public string StatusFilterText
        {
            get
            {
                switch (_statusFilter) { case 1: return "状态：已入住"; case 2: return "状态：装修中"; case 3: return "状态：空置"; default: return "状态：全部"; }
            }
        }
        public string ArrearFilterText { get { return _arrearFilter == 1 ? "欠费：仅看欠费" : "欠费：全部"; } }
        public bool CanPrev { get { return _pageIndex > 1; } }
        public bool CanNext { get { return _pageIndex * _pageSize < _total; } }
        public bool IsFormVisible { get { return _isFormVisible; } private set { SetProperty(ref _isFormVisible, value); } }
        public bool IsEditing { get { return _editingId > 0; } }
        public string FormTitle { get { return _editingId > 0 ? "编辑房产" : "新增房产"; } }
        public string FormRoomNo { get { return _formRoomNo; } set { SetProperty(ref _formRoomNo, value); } }
        public int? FormBuildingId { get { return _formBuildingId; } set { if (SetProperty(ref _formBuildingId, value)) { _ = LoadUnitsAsync(); } } }
        public int? FormUnitId { get { return _formUnitId; } set { SetProperty(ref _formUnitId, value); } }
        public decimal FormArea { get { return _formArea; } set { SetProperty(ref _formArea, value); } }
        /// <summary>
        /// F-01：输入框绑定文本（而非 decimal）——中间态 <c>123.</c> 属合法状态，不再被绑定回滚；
        /// 每个字符变更即做行内校验（最多 2 位小数／非法字符），保存时再走一次。
        /// </summary>
        public string FormAreaText
        {
            get { return _formAreaText; }
            set
            {
                if (!SetProperty(ref _formAreaText, value)) return;
                decimal parsed;
                string error;
                if (AreaValue.TryParseInput(value, out parsed, out error))
                {
                    FormAreaError = string.Empty;
                    if (parsed > 0) FormArea = parsed;
                }
                else if (IsPreservedLegacyArea(value))
                {
                    // 存量 >2 位小数的面积：允许原样保留，避免「编辑房号却被迫改写面积」
                    FormAreaError = string.Empty;
                }
                else
                {
                    FormAreaError = string.IsNullOrEmpty(value) ? string.Empty : error;
                }
            }
        }
        /// <summary>建筑面积行内错误提示（不占用页面级 ErrorText）。</summary>
        public string FormAreaError { get { return _formAreaError; } private set { SetProperty(ref _formAreaError, value); } }

        /// <summary>编辑态下文本框内容与原始存量面积一致（且该值超出 2 位小数）→ 视为合法保留。</summary>
        private bool IsPreservedLegacyArea(string text)
        {
            if (_editingId <= 0 || _loadedArea <= 0 || !AreaValue.HasExcessDecimals(_loadedArea)) return false;
            decimal loose;
            return AreaValue.TryParseLoose(text, out loose) && loose == _loadedArea;
        }
        public PropertyUsage FormUsage { get { return _formUsage; } set { SetProperty(ref _formUsage, value); } }
        public PropertyStatus FormStatus { get { return _formStatus; } set { SetProperty(ref _formStatus, value); } }
        public PropertyRow ViewRow { get { return _viewRow; } set { SetProperty(ref _viewRow, value); OnPropertyChanged(nameof(IsViewVisible)); } }
        public bool IsViewVisible { get { return _viewRow != null; } }
        public bool IsBuildingAdd { get { return _isBuildingAdd; } set { SetProperty(ref _isBuildingAdd, value); } }
        public bool IsUnitAdd { get { return _isUnitAdd; } set { SetProperty(ref _isUnitAdd, value); } }
        public string NewBuildingNo { get { return _newBuildingNo; } set { SetProperty(ref _newBuildingNo, value); } }
        public string NewUnitNo { get { return _newUnitNo; } set { SetProperty(ref _newUnitNo, value); } }
        public bool IsDeleteConfirmVisible { get { return _isDeleteConfirmVisible; } private set { SetProperty(ref _isDeleteConfirmVisible, value); } }
        public string DeleteConfirmTitle { get { return _deleteConfirmTitle; } private set { SetProperty(ref _deleteConfirmTitle, value); } }
        public string DeleteConfirmMessage { get { return _deleteConfirmMessage; } private set { SetProperty(ref _deleteConfirmMessage, value); } }
        /// <summary>
        /// 全选（v1.2.0 第 3 轮修正作用域）：勾选 = 选中**当前筛选条件下的全部房产**（含其它页面），
        /// 删除时按整个筛选结果执行，而不是只删当前页；取消勾选 = 清空全部勾选。
        /// </summary>
        public bool IsSelectAll
        {
            get { return _isSelectAll; }
            set
            {
                if (SetProperty(ref _isSelectAll, value))
                {
                    _isSelectAllFiltered = value;
                    foreach (var r in Items) { r.IsChecked = value; }
                    OnPropertyChanged(nameof(SelectionHint));
                }
            }
        }

        /// <summary>选择提示（v1.2.0 第 3 轮）：让用户看清「全选」到底是几户、是否跨页。</summary>
        public string SelectionHint
        {
            get
            {
                if (_isSelectAllFiltered)
                    return "已全选当前筛选条件下的全部 " + Total + " 户房产（删除时含其它页面）";
                int checkedCount = Items.Count(x => x.IsChecked);
                return checkedCount > 0 ? "已勾选 " + checkedCount + " 户房产（仅当前页）" : string.Empty;
            }
        }

        /// <summary>行勾选变化：任一行被取消勾选即退出「全选全部」作用域（避免误删整库）。</summary>
        private void OnRowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PropertyRow.IsChecked)) return;
            var row = sender as PropertyRow;
            if (row == null || row.IsChecked) return;
            if (_isSelectAllFiltered)
            {
                _isSelectAllFiltered = false;
                OnPropertyChanged(nameof(SelectionHint));
            }
        }

        public IRelayCommand NewCommand { get; }
        public IRelayCommand<PropertyRow> EditCommand { get; }
        public IRelayCommand<PropertyRow> ViewCommand { get; }
        public IAsyncRelayCommand QueryCommand { get; }
        public IRelayCommand<PropertyRow> DeleteCommand { get; }
        public IRelayCommand BatchDeleteCommand { get; }
        public IAsyncRelayCommand ExportCommand { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand PrevPageCommand { get; }
        public IRelayCommand NextPageCommand { get; }
        public IRelayCommand CloseViewCommand { get; }
        public IRelayCommand OpenNewBuildingCommand { get; }
        public IRelayCommand CancelNewBuildingCommand { get; }
        public IAsyncRelayCommand ConfirmNewBuildingCommand { get; }
        public IRelayCommand OpenNewUnitCommand { get; }
        public IRelayCommand CancelNewUnitCommand { get; }
        public IAsyncRelayCommand ConfirmNewUnitCommand { get; }
        public IRelayCommand DeleteBuildingCommand { get; }
        public IRelayCommand DeleteUnitCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteCommand { get; }
        public IRelayCommand CancelDeleteCommand { get; }

        private async Task InitAsync()
        {
            await RunAsync(async () =>
            {
                var buildings = await Api.GetBuildingsAsync();
                BuildingFilterItems.Add(new BuildingDto { Id = 0, BuildingNo = "全部" });
                foreach (var b in buildings)
                {
                    Buildings.Add(b);
                    BuildingFilterItems.Add(b);
                }
                await LoadCoreAsync();
            }, "房产已加载");
        }

        public async Task LoadAsync() => await RunAsync(LoadCoreAsync, "房产已加载");

        private async Task LoadCoreAsync()
        {
            var query = BuildQuery(_pageIndex, _pageSize);
            var page = await Api.QueryPropertiesAsync(query);
            Items.Clear();
            foreach (var dto in page.Items)
            {
                var row = new PropertyRow { Dto = dto };
                row.PropertyChanged += OnRowPropertyChanged;
                Items.Add(row);
            }
            Total = page.Total;
            _isSelectAll = false;
            _isSelectAllFiltered = false;
            OnPropertyChanged(nameof(IsSelectAll));
            OnPropertyChanged(nameof(SelectionHint));
            OnPropertyChanged(nameof(CanPrev));
            OnPropertyChanged(nameof(CanNext));
        }

        private async Task LoadUnitsAsync()
        {
            Units.Clear();
            // 「（无单元）」选项：部分楼栋无单元，单元为选填
            Units.Add(new UnitDto { Id = 0, UnitNo = "（无单元）" });
            int firstRealUnitId = 0;
            if (_formBuildingId.HasValue && _formBuildingId.Value > 0)
            {
                var units = await Api.GetUnitsAsync(_formBuildingId.Value);
                foreach (var u in units)
                {
                    Units.Add(u);
                    if (firstRealUnitId == 0) firstRealUnitId = u.Id;
                }
            }
            FormUnitId = firstRealUnitId;
        }

        private async Task ReloadBuildingsAsync(int? selectId)
        {
            var buildings = await Api.GetBuildingsAsync();
            Buildings.Clear();
            BuildingFilterItems.Clear();
            BuildingFilterItems.Add(new BuildingDto { Id = 0, BuildingNo = "全部" });
            foreach (var b in buildings)
            {
                Buildings.Add(b);
                BuildingFilterItems.Add(b);
            }
            FormBuildingId = selectId ?? Buildings.FirstOrDefault()?.Id;
        }

        private async Task ConfirmNewBuildingAsync()
        {
            if (string.IsNullOrWhiteSpace(NewBuildingNo)) { ErrorText = "请输入楼栋号"; return; }
            await RunAsync(async () =>
            {
                int communityId = Buildings.FirstOrDefault()?.CommunityId ?? 1;
                var created = await Api.CreateBuildingAsync(new BuildingRequest { CommunityId = communityId, BuildingNo = NewBuildingNo.Trim(), Floors = 1 });
                IsBuildingAdd = false;
                await ReloadBuildingsAsync(created.Id);
            }, "楼栋已新增");
        }

        private async Task ConfirmNewUnitAsync()
        {
            if (string.IsNullOrWhiteSpace(NewUnitNo)) { ErrorText = "请输入单元号"; return; }
            if (!FormBuildingId.HasValue) { ErrorText = "请先选择/新增楼栋"; return; }
            await RunAsync(async () =>
            {
                var created = await Api.CreateUnitAsync(new UnitRequest { BuildingId = FormBuildingId.Value, UnitNo = NewUnitNo.Trim() });
                IsUnitAdd = false;
                await LoadUnitsAsync();
                FormUnitId = created.Id;
            }, "单元已新增");
        }

        private void StartNew()
        {
            _editingId = 0;
            OnPropertyChanged(nameof(FormTitle));
            OnPropertyChanged(nameof(IsEditing));
            FormRoomNo = string.Empty;
            // 楼栋直接赋值（跳过 setter 触发），仅由下方 LoadUnitsAsync 加载一次，避免并发重复
            _formBuildingId = Buildings.FirstOrDefault()?.Id;
            OnPropertyChanged(nameof(FormBuildingId));
            FormArea = 0;
            _loadedArea = 0;
            FormAreaText = string.Empty;   // F-01：新增时留空，由用户逐键输入（含小数点）
            FormUsage = PropertyUsage.Residential;
            FormStatus = PropertyStatus.Vacant;
            IsFormVisible = true;
            _ = LoadUnitsAsync();
        }

        private async void StartEdit(PropertyRow row)
        {
            if (row == null) return;
            _editingId = row.Id;
            OnPropertyChanged(nameof(FormTitle));
            OnPropertyChanged(nameof(IsEditing));
            FormRoomNo = row.Dto.RoomNo;
            _formBuildingId = row.Dto.BuildingId
                ?? Buildings.FirstOrDefault(x => string.Equals(x.BuildingNo, row.Dto.BuildingNo, StringComparison.Ordinal))?.Id;
            OnPropertyChanged(nameof(FormBuildingId));
            await LoadUnitsAsync();
            FormUnitId = row.Dto.UnitId ?? 0;   // 无单元回显为「（无单元）」
            FormArea = row.Dto.Area;
            _loadedArea = row.Dto.Area;
            FormAreaText = AreaValue.Format(row.Dto.Area);   // F-01/F-02：按存值回显（最多 2 位）
            FormUsage = row.Dto.Usage;
            FormStatus = row.Dto.Status;
            IsFormVisible = true;
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(FormRoomNo)) { ErrorText = "房号不能为空"; return; }
            if (!FormBuildingId.HasValue || FormBuildingId.Value <= 0) { ErrorText = "请选择所属楼栋"; return; }
            // F-01：以输入框文本为准解析（容忍尾随小数点与全角字符），失败给出行内提示
            decimal area;
            string areaError;
            if (!AreaValue.TryParseInput(FormAreaText, out area, out areaError))
            {
                // 存量高精度面积原样提交（编辑场景）不受 2 位限制约束
                decimal loose;
                if (IsPreservedLegacyArea(FormAreaText) && AreaValue.TryParseLoose(FormAreaText, out loose))
                {
                    area = loose;
                }
                else
                {
                    FormAreaError = string.IsNullOrEmpty(FormAreaText) ? string.Empty : areaError;
                    ErrorText = areaError;
                    return;
                }
            }
            if (area <= 0) { ErrorText = "建筑面积必须大于 0"; return; }
            FormArea = area;
            FormAreaError = string.Empty;
            // 单元选填：Id<=0 视为「无单元」
            int? unitId = FormUnitId.HasValue && FormUnitId.Value > 0 ? FormUnitId : (int?)null;
            var request = new PropertyRequest
            {
                BuildingId = FormBuildingId.Value,
                UnitId = unitId,
                RoomNo = FormRoomNo.Trim(),
                Area = area,
                Usage = FormUsage,
                Status = FormStatus
            };
            await RunAsync(async () =>
            {
                if (_editingId > 0) await Api.UpdatePropertyAsync(_editingId, request);
                else await Api.CreatePropertyAsync(request);
                IsFormVisible = false;
                await LoadCoreAsync();
            }, _editingId > 0 ? "房产已更新" : "房产已新增");
        }

        private void ConfigureDeleteConfirm(string title, string message, string success, Func<Task> action)
        {
            if (action == null) return;
            _deleteConfirmAction = action;
            _deleteConfirmSuccess = success;
            DeleteConfirmTitle = title;
            DeleteConfirmMessage = message;
            IsDeleteConfirmVisible = true;
        }

        private async Task ConfirmDeleteAsync()
        {
            if (_deleteConfirmAction == null) { IsDeleteConfirmVisible = false; return; }
            var action = _deleteConfirmAction;
            var success = _deleteConfirmSuccess;
            await RunAsync(async () =>
            {
                await action();
                IsDeleteConfirmVisible = false;
            }, success);
        }

        private async Task DeletePropertyCoreAsync(PropertyRow row)
        {
            if (row == null) return;
            await Api.DeletePropertyAsync(row.Id);
            await LoadCoreAsync();
        }

        /// <summary>批量删除：对勾选房产逐个软删，存在业主绑定关系的房产跳过并汇总提示。</summary>
        private async Task DeletePropertiesCoreAsync(IEnumerable<PropertyRow> rows)
        {
            int ok = 0;
            var blocked = new List<string>();
            foreach (var r in rows)
            {
                try { await Api.DeletePropertyAsync(r.Id); ok++; }
                catch (ApiClientException ex) when (ex.Code == ErrorCode.Conflict)
                {
                    blocked.Add(r.Path + "（" + ex.Message + "）");
                }
            }
            await LoadCoreAsync();
            if (blocked.Count > 0)
            {
                string blockedDesc = string.Join("、", blocked.Take(3)) + (blocked.Count > 3 ? " 等 " + blocked.Count + " 户" : string.Empty);
                throw new ApiClientException(ErrorCode.Conflict, "已删除 " + ok + " 户；跳过 " + blockedDesc + "（存在业主绑定关系，请先解除再删除）");
            }
        }

        private void RequestBatchDelete()
        {
            // v1.2.0 第 3 轮：勾了表头「全选」→ 删除当前筛选条件下的全部房产（含其它页面）
            if (_isSelectAllFiltered) { _ = RequestBatchDeleteAllAsync(); return; }
            var rows = Items.Where(x => x.IsChecked).ToList();
            if (rows.Count == 0) { ErrorText = "请先勾选要删除的房产"; return; }
            string houseDesc = string.Join("、", rows.Take(3).Select(r => r.Path));
            if (rows.Count > 3) { houseDesc += " 等 " + rows.Count + " 户"; }
            ConfigureDeleteConfirm(
                "确认批量删除房产？",
                "将删除 " + rows.Count + " 户房产（软删除，存在业主绑定关系的房产将跳过）：\n" + houseDesc,
                "已批量删除 " + rows.Count + " 户房产（软删除）",
                () => DeletePropertiesCoreAsync(rows));
        }

        /// <summary>
        /// 「全选」删除（v1.2.0 第 3 轮）：先取回当前筛选条件下的**全部**房产（跨页），再逐户软删。
        /// 取回数量与列表「共 N 条记录」一致；存在业主绑定关系的户跳过并在提示里汇总。
        /// </summary>
        private async Task RequestBatchDeleteAllAsync()
        {
            var rows = new List<PropertyRow>();
            await RunAsync(async () =>
            {
                var query = BuildQuery(1, 100000);
                var page = await Api.QueryPropertiesAsync(query);
                rows.AddRange(page.Items.Select(dto => new PropertyRow { Dto = dto }));
            }, string.Empty);
            if (rows.Count == 0)
            {
                ErrorText = "当前筛选条件下没有可删除的房产";
                return;
            }
            var targets = rows;
            ConfigureDeleteConfirm(
                "确认批量删除房产？",
                "已全选当前筛选条件下的全部 " + targets.Count + " 户房产（含其它页面）：\n将全部软删除，存在业主绑定关系的房产将跳过。",
                "已批量删除 " + targets.Count + " 户房产（软删除）",
                () => DeletePropertiesCoreAsync(targets));
        }

        /// <summary>按当前筛选条件构造查询（列表分页与「全选删除」共用同一口径）。</summary>
        private BaseInfoQueryRequest BuildQuery(int pageIndex, int pageSize)
        {
            var query = new BaseInfoQueryRequest
            {
                PageIndex = pageIndex,
                PageSize = pageSize,
                Keyword = SearchText,
                BuildingId = _buildingFilter.HasValue && _buildingFilter.Value > 0 ? _buildingFilter : null,
                OnlyArrear = _arrearFilter == 1
            };
            if (_statusFilter != 0)
                query.Status = _statusFilter == 1 ? PropertyStatus.Occupied
                    : (_statusFilter == 2 ? PropertyStatus.Renovating : PropertyStatus.Vacant);
            return query;
        }

        private async Task DeleteBuildingCoreAsync(int buildingId)
        {
            await Api.DeleteBuildingAsync(buildingId);
            int? nextId = Buildings.FirstOrDefault(x => x.Id != buildingId)?.Id;
            await ReloadBuildingsAsync(nextId);
        }

        private async Task DeleteUnitCoreAsync(int unitId)
        {
            await Api.DeleteUnitAsync(unitId);
            await LoadUnitsAsync();
            FormUnitId = Units.FirstOrDefault(x => x.Id > 0)?.Id ?? 0;
        }

        private async Task ExportAsync()
        {
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                var log = await Api.ExportAsync(new BaseInfoExportRequest
                {
                    ExportType = "property",
                    Format = ExportFormat.Excel,
                    Filter = new BaseInfoQueryRequest { PageIndex = 1, PageSize = 100000, BuildingId = _buildingFilter.HasValue && _buildingFilter.Value > 0 ? _buildingFilter : null, Keyword = SearchText, OnlyArrear = _arrearFilter == 1, Status = (_statusFilter == 1 ? PropertyStatus.Occupied : (_statusFilter == 2 ? PropertyStatus.Renovating : (_statusFilter == 3 ? PropertyStatus.Vacant : (PropertyStatus?)null))) }
                });
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "房产列表_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xlsx");
                await Api.DownloadExportFileAsync(log.Id, path);
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已导出到：" + path;
                MessageBox.Show("已导出到：\n" + path, "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (ApiClientException ex)
            {
                ErrorText = ex.Message;
            }
            catch (Exception ex)
            {
                ErrorText = "导出失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

    }
}
