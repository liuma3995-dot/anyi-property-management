using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Dispute;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>业主选项（甲方/乙方下拉：选业主带出姓名电话；「外部登记」= null 手填）。</summary>
    public class DisputeOwnerOption
    {
        public int? OwnerId { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        /// <summary>当事人房产的完整房号（跨模块：楼栋+单元+房号，如「1号楼1单元101」）。</summary>
        public string FullPath { get; set; }
        public string Display
        {
            get
            {
                if (!OwnerId.HasValue) return "外部登记（手动填写姓名 / 电话）";
                var path = AddressText.Build(BuildingNo, UnitNo, RoomNo);
                if (string.IsNullOrWhiteSpace(path)) { path = FullPath ?? string.Empty; }
                var name = Name ?? string.Empty;
                var phone = Phone ?? string.Empty;
                var head = string.IsNullOrEmpty(path) ? name : (path + " " + name);
                return phone.Length > 0 ? head + " · " + phone : head;
            }
        }
        public string BuildingNo { get; set; }
        public string UnitNo { get; set; }
        public string RoomNo { get; set; }
    }

    /// <summary>关联房产选项（「不关联」= null）。</summary>
    public class DisputePropertyOption
    {
        public int? PropertyId { get; set; }
        public string Display { get; set; }
    }

    /// <summary>调解员推荐项（BR-DIS-03：点选设 MediatorId，首项带「推荐」标签）。</summary>
    public class DisputeMediatorOption : ObservableObject
    {
        private bool _isSelected;

        public DisputeMediatorDto Dto { get; set; }
        public int EmployeeId { get { return Dto.EmployeeId; } }
        public string Display { get; set; }
        public string AvatarText { get; set; }
        /// <summary>推荐理由（如「漏水类成功率 92% · 结案 11 例」）。</summary>
        public string ReasonText { get; set; }
        public bool IsRecommended { get; set; }
        public bool IsSelected { get { return _isSelected; } set { SetProperty(ref _isSelected, value); } }
    }

    /// <summary>纠纷登记页（PG-DIS-02，UC-DIS-001，BR-DIS-01/05）：
    /// 类型 / 关联房产 / 甲乙当事人（业主引用或外部登记，带出电话）/ 等级 / 期望时间 / 描述，
    /// 右栏调解员智能推荐（点选分配），提交走 CreateDisputeAsync（90 天同类软提示弹窗）。</summary>
    public class DisputeCreateViewModel : BaseInfoPageViewModel
    {
        private int _typeId;
        private int? _propertyId;
        private int? _partyAOwnerId;
        private string _partyAName = string.Empty;
        private string _partyAPhone = string.Empty;
        private int? _partyBOwnerId;
        private string _partyBName = string.Empty;
        private string _partyBPhone = string.Empty;
        private int _level;
        private DateTime? _expectedAt;
        private string _location = string.Empty;
        private string _detail = string.Empty;
        private int? _selectedMediatorId;
        private bool _mediatorsLoadedOnce;
        private string _formTypeText = string.Empty;
        private string _propertySearch = string.Empty;
        private string _partyASearch = string.Empty;
        private string _partyBSearch = string.Empty;
        private bool _isTypeAdd;
        private string _newTypeName = string.Empty;
        private bool _isTypeDeleteVisible;
        private string _deleteTypeName = string.Empty;
        private int _deleteTypeId;
        private const int PickerPageSize = 20;
        private readonly DispatcherTimer _propertySearchDebounce;
        private readonly DispatcherTimer _ownerASearchDebounce;
        private readonly DispatcherTimer _ownerBSearchDebounce;
        private bool _suppressPropertySearch;
        private bool _suppressOwnerASearch;
        private bool _suppressOwnerBSearch;
        private int _propertyPage = 1;
        private int _propertyTotal;
        private int _propertySearchSeq;
        private int _ownerAPage = 1;
        private int _ownerATotal;
        private int _ownerASearchSeq;
        private int _ownerBPage = 1;
        private int _ownerBTotal;
        private int _ownerBSearchSeq;
        private DisputePropertyOption _selectedPropertyOption;
        private DisputeOwnerOption _selectedOwnerAOption;
        private DisputeOwnerOption _selectedOwnerBOption;
        private DisputeMediatorOption _selectedMediatorOption;

        public DisputeCreateViewModel(IApiClient api) : base(api)
        {
            _propertySearchDebounce = NewSearchDebounce(async () => await SearchPropertiesCoreAsync(CurrentPropertyKeyword(), 1));
            _ownerASearchDebounce = NewSearchDebounce(async () => await SearchOwnersCoreAsync(true, CurrentOwnerAKeyword(), 1));
            _ownerBSearchDebounce = NewSearchDebounce(async () => await SearchOwnersCoreAsync(false, CurrentOwnerBKeyword(), 1));
            SubmitCommand = new AsyncRelayCommand(SubmitAsync);
            ResetCommand = new RelayCommand(Reset);
            SelectMediatorCommand = new RelayCommand<DisputeMediatorOption>(SelectMediator);
            OpenNewTypeCommand = new RelayCommand(OpenNewType);
            ConfirmNewTypeCommand = new AsyncRelayCommand(ConfirmNewTypeAsync);
            CancelNewTypeCommand = new RelayCommand(() => { IsTypeAdd = false; NewTypeName = string.Empty; ErrorText = string.Empty; });
            RequestDeleteTypeCommand = new RelayCommand(RequestDeleteType);
            ConfirmDeleteTypeCommand = new AsyncRelayCommand(ConfirmDeleteTypeAsync);
            CancelDeleteTypeCommand = new RelayCommand(() => IsTypeDeleteVisible = false);
            PropertyPrevPageCommand = new AsyncRelayCommand(
                () => SearchPropertiesCoreAsync(CurrentPropertyKeyword(), _propertyPage - 1), () => CanPropertyPrev);
            PropertyNextPageCommand = new AsyncRelayCommand(
                () => SearchPropertiesCoreAsync(CurrentPropertyKeyword(), _propertyPage + 1), () => CanPropertyNext);
            OwnerAPrevPageCommand = new AsyncRelayCommand(
                () => SearchOwnersCoreAsync(true, CurrentOwnerAKeyword(), _ownerAPage - 1), () => CanOwnerAPrev);
            OwnerANextPageCommand = new AsyncRelayCommand(
                () => SearchOwnersCoreAsync(true, CurrentOwnerAKeyword(), _ownerAPage + 1), () => CanOwnerANext);
            OwnerBPrevPageCommand = new AsyncRelayCommand(
                () => SearchOwnersCoreAsync(false, CurrentOwnerBKeyword(), _ownerBPage - 1), () => CanOwnerBPrev);
            OwnerBNextPageCommand = new AsyncRelayCommand(
                () => SearchOwnersCoreAsync(false, CurrentOwnerBKeyword(), _ownerBPage + 1), () => CanOwnerBNext);
            _ = InitAsync();
        }

        public ObservableCollection<DisputeTypeDto> Types { get; } = new ObservableCollection<DisputeTypeDto>();
        public ObservableCollection<DisputePropertyOption> PropertyOptions { get; } = new ObservableCollection<DisputePropertyOption>();
        public ObservableCollection<DisputeOwnerOption> OwnerAOptions { get; } = new ObservableCollection<DisputeOwnerOption>();
        public ObservableCollection<DisputeOwnerOption> OwnerBOptions { get; } = new ObservableCollection<DisputeOwnerOption>();
        public ObservableCollection<DisputeMediatorOption> Mediators { get; } = new ObservableCollection<DisputeMediatorOption>();

        public int TypeId
        {
            get { return _typeId; }
            set { if (SetProperty(ref _typeId, value)) { _ = LoadRecommendationsAsync(); } }
        }

        public int? FormPropertyId { get { return _propertyId; } set { SetProperty(ref _propertyId, value); } }

        public int? PartyAOwnerId
        {
            get { return _partyAOwnerId; }
            set { if (SetProperty(ref _partyAOwnerId, value)) { ApplyPartyOwner(true); OnPropertyChanged(nameof(PartyAExternal)); } }
        }

        public string PartyAName { get { return _partyAName; } set { SetProperty(ref _partyAName, value); } }
        public string PartyAPhone { get { return _partyAPhone; } set { SetProperty(ref _partyAPhone, value); } }
        /// <summary>甲方未引用业主（外部登记）时姓名/电话可手填。</summary>
        public bool PartyAExternal { get { return !_partyAOwnerId.HasValue; } }

        public int? PartyBOwnerId
        {
            get { return _partyBOwnerId; }
            set { if (SetProperty(ref _partyBOwnerId, value)) { ApplyPartyOwner(false); OnPropertyChanged(nameof(PartyBExternal)); } }
        }

        public string PartyBName { get { return _partyBName; } set { SetProperty(ref _partyBName, value); } }
        public string PartyBPhone { get { return _partyBPhone; } set { SetProperty(ref _partyBPhone, value); } }
        /// <summary>乙方未引用业主（外部登记）时姓名/电话可手填。</summary>
        public bool PartyBExternal { get { return !_partyBOwnerId.HasValue; } }

        /// <summary>纠纷等级：0 一般 / 1 较大 / 2 重大。</summary>
        public int Level { get { return _level; } set { SetProperty(ref _level, value); } }
        public DateTime? ExpectedAt { get { return _expectedAt; } set { SetProperty(ref _expectedAt, value); } }
        public string Location { get { return _location; } set { SetProperty(ref _location, value); } }
        public string Detail { get { return _detail; } set { SetProperty(ref _detail, value); } }
        public int? SelectedMediatorId { get { return _selectedMediatorId; } private set { SetProperty(ref _selectedMediatorId, value); } }

        /// <summary>调解员卡片选中项（ListBox 选择，点选即分配）。</summary>
        public DisputeMediatorOption SelectedMediatorOption
        {
            get { return _selectedMediatorOption; }
            set
            {
                if (SetProperty(ref _selectedMediatorOption, value) && value != null)
                    SelectedMediatorId = value.EmployeeId;
            }
        }

        /// <summary>纠纷类型下拉文本（可自定义输入，保存时按名称解析/新建）。</summary>
        public string FormTypeText
        {
            get { return _formTypeText; }
            set
            {
                if (SetProperty(ref _formTypeText, value))
                {
                    var match = Types.FirstOrDefault(t => string.Equals(t.Name, value?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (match != null && match.Id != _typeId) TypeId = match.Id;
                }
            }
        }

        /// <summary>新增自定义类型内联行是否显示。</summary>
        public bool IsTypeAdd { get { return _isTypeAdd; } set { SetProperty(ref _isTypeAdd, value); } }
        public string NewTypeName { get { return _newTypeName; } set { SetProperty(ref _newTypeName, value); } }
        /// <summary>删除类型二次确认浮层。</summary>
        public bool IsTypeDeleteVisible { get { return _isTypeDeleteVisible; } set { SetProperty(ref _isTypeDeleteVisible, value); } }
        public string DeleteTypeName { get { return _deleteTypeName; } set { SetProperty(ref _deleteTypeName, value); } }

        /// <summary>关联房产搜索词：防抖后走服务端检索。</summary>
        public string PropertySearch
        {
            get { return _propertySearch; }
            set
            {
                if (!SetProperty(ref _propertySearch, value) || _suppressPropertySearch) { return; }
                _propertySearchDebounce.Stop();
                _propertySearchDebounce.Start();
            }
        }

        /// <summary>甲方搜索词：独立防抖后走服务端检索。</summary>
        public string PartyASearch
        {
            get { return _partyASearch; }
            set
            {
                if (!SetProperty(ref _partyASearch, value) || _suppressOwnerASearch) { return; }
                _ownerASearchDebounce.Stop();
                _ownerASearchDebounce.Start();
            }
        }

        /// <summary>乙方搜索词：独立防抖后走服务端检索。</summary>
        public string PartyBSearch
        {
            get { return _partyBSearch; }
            set
            {
                if (!SetProperty(ref _partyBSearch, value) || _suppressOwnerBSearch) { return; }
                _ownerBSearchDebounce.Stop();
                _ownerBSearchDebounce.Start();
            }
        }

        public string PropertyPageText { get { return "共 " + _propertyTotal + " 条 · 第 " + _propertyPage + "/" + LastPageOf(_propertyTotal) + " 页"; } }
        public string OwnerAPageText { get { return "共 " + _ownerATotal + " 条 · 第 " + _ownerAPage + "/" + LastPageOf(_ownerATotal) + " 页"; } }
        public string OwnerBPageText { get { return "共 " + _ownerBTotal + " 条 · 第 " + _ownerBPage + "/" + LastPageOf(_ownerBTotal) + " 页"; } }
        public bool CanPropertyPrev { get { return _propertyPage > 1; } }
        public bool CanPropertyNext { get { return _propertyPage * PickerPageSize < _propertyTotal; } }
        public bool CanOwnerAPrev { get { return _ownerAPage > 1; } }
        public bool CanOwnerANext { get { return _ownerAPage * PickerPageSize < _ownerATotal; } }
        public bool CanOwnerBPrev { get { return _ownerBPage > 1; } }
        public bool CanOwnerBNext { get { return _ownerBPage * PickerPageSize < _ownerBTotal; } }

        /// <summary>关联房产选中项（下拉内部检索；输入过程中的瞬时 null 忽略，避免误清空）。</summary>
        public DisputePropertyOption SelectedPropertyOption
        {
            get { return _selectedPropertyOption; }
            set
            {
                if (value == null) return;
                if (SetProperty(ref _selectedPropertyOption, value)) FormPropertyId = value.PropertyId;
                // 选中后回填展示文本，避免输入框残留检索关键字（与业主-房产关系/车位维护同口径）
                string display = value.Display ?? string.Empty;
                SetSearchText(ref _propertySearch, display, nameof(PropertySearch), true);
            }
        }

        /// <summary>甲方选中项（下拉内部检索；输入过程中的瞬时 null 忽略）。</summary>
        public DisputeOwnerOption SelectedOwnerAOption
        {
            get { return _selectedOwnerAOption; }
            set
            {
                if (value == null) return;
                if (SetProperty(ref _selectedOwnerAOption, value)) PartyAOwnerId = value.OwnerId;
                string display = value.Display ?? string.Empty;
                SetSearchText(ref _partyASearch, display, nameof(PartyASearch), true);
            }
        }

        /// <summary>乙方选中项（下拉内部检索；输入过程中的瞬时 null 忽略）。</summary>
        public DisputeOwnerOption SelectedOwnerBOption
        {
            get { return _selectedOwnerBOption; }
            set
            {
                if (value == null) return;
                if (SetProperty(ref _selectedOwnerBOption, value)) PartyBOwnerId = value.OwnerId;
                string display = value.Display ?? string.Empty;
                SetSearchText(ref _partyBSearch, display, nameof(PartyBSearch), false);
            }
        }

        public IAsyncRelayCommand SubmitCommand { get; }
        public IRelayCommand ResetCommand { get; }
        public IRelayCommand<DisputeMediatorOption> SelectMediatorCommand { get; }
        public IRelayCommand OpenNewTypeCommand { get; }
        public IAsyncRelayCommand ConfirmNewTypeCommand { get; }
        public IRelayCommand CancelNewTypeCommand { get; }
        public IRelayCommand RequestDeleteTypeCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteTypeCommand { get; }
        public IRelayCommand CancelDeleteTypeCommand { get; }
        public IAsyncRelayCommand PropertyPrevPageCommand { get; }
        public IAsyncRelayCommand PropertyNextPageCommand { get; }
        public IAsyncRelayCommand OwnerAPrevPageCommand { get; }
        public IAsyncRelayCommand OwnerANextPageCommand { get; }
        public IAsyncRelayCommand OwnerBPrevPageCommand { get; }
        public IAsyncRelayCommand OwnerBNextPageCommand { get; }

        private async Task InitAsync()
        {
            try
            {
                foreach (var t in await Api.GetDisputeTypesAsync()) Types.Add(t);
                if (Types.Count > 0) TypeId = Types[0].Id;
                else if (Types.Count == 0) TypeId = 0;
                // 兜底：确保调解员推荐一定会加载（不依赖 TypeId 变化触发）
                if (Mediators.Count == 0) await LoadRecommendationsAsync();
                await SearchPropertiesCoreAsync(string.Empty, 1);
                await SearchOwnersCoreAsync(true, string.Empty, 1);
                await SearchOwnersCoreAsync(false, string.Empty, 1);
            }
            catch (Exception ex)
            {
                ErrorText = "基础数据加载失败：" + ex.Message;
            }
        }

        private DispatcherTimer NewSearchDebounce(Func<Task> search)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += async (s, e) =>
            {
                timer.Stop();
                try
                {
                    await search();
                }
                catch (Exception ex)
                {
                    ErrorText = "检索失败：" + ex.Message;
                }
            };
            return timer;
        }

        private string CurrentPropertyKeyword()
        {
            return string.IsNullOrWhiteSpace(_propertySearch) ? string.Empty : _propertySearch.Trim();
        }

        private string CurrentOwnerAKeyword()
        {
            return string.IsNullOrWhiteSpace(_partyASearch) ? string.Empty : _partyASearch.Trim();
        }

        private string CurrentOwnerBKeyword()
        {
            return string.IsNullOrWhiteSpace(_partyBSearch) ? string.Empty : _partyBSearch.Trim();
        }

        private async Task SearchPropertiesCoreAsync(string keyword, int pageIndex)
        {
            int target = pageIndex < 1 ? 1 : pageIndex;
            int seq = ++_propertySearchSeq;
            try
            {
                var page = await Api.QueryPropertiesAsync(new BaseInfoQueryRequest
                {
                    PageIndex = target,
                    PageSize = PickerPageSize,
                    Keyword = keyword
                });
                if (seq != _propertySearchSeq) { return; }
                int last = LastPageOf(page.Total);
                if (target > last)
                {
                    target = last;
                    page = await Api.QueryPropertiesAsync(new BaseInfoQueryRequest
                    {
                        PageIndex = target,
                        PageSize = PickerPageSize,
                        Keyword = keyword
                    });
                    if (seq != _propertySearchSeq) { return; }
                }

                _propertyPage = target;
                _propertyTotal = page.Total;
                PropertyOptions.Clear();
                PropertyOptions.Add(new DisputePropertyOption { PropertyId = null, Display = "不关联" });
                foreach (var p in page.Items)
                {
                    string path = !string.IsNullOrWhiteSpace(p.UnitPath)
                        ? p.UnitPath
                        : AddressText.Build(p.BuildingNo, p.UnitNo, p.RoomNo);
                    if (string.IsNullOrWhiteSpace(path)) { path = "房产 #" + p.Id; }
                    PropertyOptions.Add(new DisputePropertyOption
                    {
                        PropertyId = p.Id,
                        Display = path + (string.IsNullOrWhiteSpace(p.OwnerName) ? string.Empty : " · " + p.OwnerName)
                    });
                }
                RaisePropertyPageState();
            }
            catch (Exception ex)
            {
                if (seq == _propertySearchSeq) { ErrorText = "关联房产加载失败：" + ex.Message; }
            }
        }

        private async Task SearchOwnersCoreAsync(bool isPartyA, string keyword, int pageIndex)
        {
            int target = pageIndex < 1 ? 1 : pageIndex;
            int seq = isPartyA ? ++_ownerASearchSeq : ++_ownerBSearchSeq;
            try
            {
                var page = await Api.QueryOwnersAsync(new BaseInfoQueryRequest
                {
                    PageIndex = target,
                    PageSize = PickerPageSize,
                    Keyword = keyword
                });
                if (!IsCurrentOwnerSearch(isPartyA, seq)) { return; }
                int last = LastPageOf(page.Total);
                if (target > last)
                {
                    target = last;
                    page = await Api.QueryOwnersAsync(new BaseInfoQueryRequest
                    {
                        PageIndex = target,
                        PageSize = PickerPageSize,
                        Keyword = keyword
                    });
                    if (!IsCurrentOwnerSearch(isPartyA, seq)) { return; }
                }

                if (isPartyA)
                {
                    _ownerAPage = target;
                    _ownerATotal = page.Total;
                }
                else
                {
                    _ownerBPage = target;
                    _ownerBTotal = page.Total;
                }

                ObservableCollection<DisputeOwnerOption> options = isPartyA ? OwnerAOptions : OwnerBOptions;
                options.Clear();
                options.Add(new DisputeOwnerOption { OwnerId = null });
                foreach (var o in page.Items)
                {
                    options.Add(new DisputeOwnerOption
                    {
                        OwnerId = o.Id,
                        Name = o.Name,
                        Phone = o.Phone,
                        BuildingNo = o.BuildingNo,
                        UnitNo = o.UnitNo,
                        RoomNo = o.RoomNo
                    });
                }
                RaiseOwnerPageState(isPartyA);
            }
            catch (Exception ex)
            {
                if (IsCurrentOwnerSearch(isPartyA, seq))
                {
                    ErrorText = (isPartyA ? "甲方" : "乙方") + "业主加载失败：" + ex.Message;
                }
            }
        }

        private bool IsCurrentOwnerSearch(bool isPartyA, int seq)
        {
            return isPartyA ? seq == _ownerASearchSeq : seq == _ownerBSearchSeq;
        }

        private static int LastPageOf(int total)
        {
            return total <= 0 ? 1 : ((total + PickerPageSize - 1) / PickerPageSize);
        }

        private void RaisePropertyPageState()
        {
            OnPropertyChanged(nameof(PropertyPageText));
            OnPropertyChanged(nameof(CanPropertyPrev));
            OnPropertyChanged(nameof(CanPropertyNext));
            PropertyPrevPageCommand.NotifyCanExecuteChanged();
            PropertyNextPageCommand.NotifyCanExecuteChanged();
        }

        private void RaiseOwnerPageState(bool isPartyA)
        {
            OnPropertyChanged(isPartyA ? nameof(OwnerAPageText) : nameof(OwnerBPageText));
            OnPropertyChanged(isPartyA ? nameof(CanOwnerAPrev) : nameof(CanOwnerBPrev));
            OnPropertyChanged(isPartyA ? nameof(CanOwnerANext) : nameof(CanOwnerBNext));
            IAsyncRelayCommand prev = isPartyA ? (IAsyncRelayCommand)OwnerAPrevPageCommand : OwnerBPrevPageCommand;
            IAsyncRelayCommand next = isPartyA ? (IAsyncRelayCommand)OwnerANextPageCommand : OwnerBNextPageCommand;
            prev.NotifyCanExecuteChanged();
            next.NotifyCanExecuteChanged();
        }

        private void SetSearchText(ref string field, string display, string propertyName, bool isPartyA)
        {
            if (string.Equals(field, display, StringComparison.Ordinal)) { return; }
            if (propertyName == nameof(PropertySearch)) { _suppressPropertySearch = true; }
            else if (isPartyA) { _suppressOwnerASearch = true; }
            else { _suppressOwnerBSearch = true; }
            try
            {
                field = display;
                OnPropertyChanged(propertyName);
            }
            finally
            {
                if (propertyName == nameof(PropertySearch)) { _suppressPropertySearch = false; }
                else if (isPartyA) { _suppressOwnerASearch = false; }
                else { _suppressOwnerBSearch = false; }
            }
        }

        private void ApplyPartyOwner(bool isPartyA)
        {
            int? ownerId = isPartyA ? _partyAOwnerId : _partyBOwnerId;
            var opt = isPartyA ? _selectedOwnerAOption : _selectedOwnerBOption;
            if (opt != null && opt.OwnerId.HasValue)
            {
                // 引用业主：带出姓名与电话（只读展示）
                if (isPartyA) { PartyAName = opt.Name; PartyAPhone = opt.Phone; }
                else { PartyBName = opt.Name; PartyBPhone = opt.Phone; }
            }
            else
            {
                // 外部登记：清空待手填
                if (isPartyA) { PartyAName = string.Empty; PartyAPhone = string.Empty; }
                else { PartyBName = string.Empty; PartyBPhone = string.Empty; }
            }
        }

        private void SelectMediator(DisputeMediatorOption option)
        {
            if (option == null) return;
            SelectedMediatorId = option.EmployeeId;
            foreach (var m in Mediators) m.IsSelected = m == option;
        }

        /// <summary>调解员智能推荐（按类型 + 房产；BR-DIS-03），类型切换时刷新。</summary>
        private async Task LoadRecommendationsAsync()
        {
            try
            {
                int? typeId = _typeId > 0 ? _typeId : (int?)null;
                var list = await Api.GetMediatorRecommendationsAsync(typeId, FormPropertyId);
                string typeLabel = null;
                foreach (var t in Types) { if (t.Id == _typeId) { typeLabel = t.Name; break; } }

                Mediators.Clear();
                int index = 0;
                foreach (var m in list)
                {
                    var parts = new[] { m.Name, m.PositionName, m.DeptName }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    Mediators.Add(new DisputeMediatorOption
                    {
                        Dto = m,
                        // 原型卡片：头像 +「姓名 · 职位 · 部门」+「推荐」标签 + 理由行；字段缺省时兜底避免空卡片
                        Display = parts.Count > 0 ? string.Join(" · ", parts) : ("调解员 #" + m.EmployeeId),
                        AvatarText = string.IsNullOrWhiteSpace(m.Name) ? "调" : m.Name.Substring(0, 1),
                        ReasonText = m.CaseCount > 0
                            ? (typeLabel ?? "同类") + "类成功率 " + Math.Round(m.SuccessRate) + "%"
                            : "暂无同类型结案记录",
                        IsRecommended = index == 0,
                        IsSelected = m.EmployeeId == _selectedMediatorId
                    });
                    index++;
                }
                _mediatorsLoadedOnce = true;
                _selectedMediatorOption = Mediators.FirstOrDefault(x => x.EmployeeId == _selectedMediatorId);
                OnPropertyChanged(nameof(HasMediators));
                OnPropertyChanged(nameof(NoMediator));
                OnPropertyChanged(nameof(MediatorEmptyText));
                OnPropertyChanged(nameof(SelectedMediatorOption));
            }
            catch (Exception ex)
            {
                ErrorText = "调解员推荐加载失败：" + ex.Message;
            }
        }

        public bool HasMediators { get { return Mediators.Count > 0; } }
        public bool NoMediator { get { return Mediators.Count == 0; } }
        public string MediatorEmptyText
        {
            get { return _mediatorsLoadedOnce ? "该类型暂无推荐调解员，可不分配提交" : "推荐加载中…"; }
        }

        private async Task SubmitAsync()
        {
            int typeId = await ResolveTypeIdAsync();
            if (typeId <= 0) { if (string.IsNullOrEmpty(ErrorText)) ErrorText = "请选择或输入纠纷类型"; return; }
            if (string.IsNullOrWhiteSpace(_partyAName)) { ErrorText = "请填写当事人甲方（报修方）姓名"; return; }
            if (string.IsNullOrWhiteSpace(_partyBName)) { ErrorText = "请填写当事人乙方（责任方）姓名"; return; }

            string caseNo = null;
            string warning = null;
            await RunAsync(async () =>
            {
                var dto = await Api.CreateDisputeAsync(new DisputeCaseCreateRequest
                {
                    TypeId = typeId,
                    Level = Level,
                    OccurTime = DateTime.Now,
                    Location = Location,
                    Detail = Detail,
                    MediatorId = _selectedMediatorId,
                    PropertyId = FormPropertyId,
                    ExpectedAt = ExpectedAt,
                    Parties = new List<DisputePartyDto>
                    {
                        new DisputePartyDto { PartyType = "0", OwnerId = _partyAOwnerId, Name = PartyAName, Phone = PartyAPhone },
                        new DisputePartyDto { PartyType = "1", OwnerId = _partyBOwnerId, Name = PartyBName, Phone = PartyBPhone }
                    }
                });
                caseNo = dto == null ? null : dto.CaseNo;
                warning = dto == null ? null : dto.WarningText;
            }, "纠纷已登记");

            if (caseNo != null)
            {
                // 成功文案只报登记事实（编号），不谎报“已通知”（通知基建未建）
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "纠纷已登记（" + caseNo + "）";
                if (!string.IsNullOrEmpty(warning))
                {
                    MessageBox.Show("该房产 90 天内已有同类纠纷：" + warning + "，建议升级处理。",
                        "同类纠纷提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                Reset();
            }
        }

        /// <summary>根据下拉文本解析类型 Id：命中已有类型返回其 Id，否则新建自定义类型。</summary>
        private async Task<int> ResolveTypeIdAsync()
        {
            string text = (FormTypeText ?? string.Empty).Trim();
            if (text.Length == 0) return _typeId > 0 ? _typeId : -1;
            var existing = Types.FirstOrDefault(t => string.Equals(t.Name, text, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing.Id;
            try
            {
                var created = await Api.CreateDisputeTypeAsync(new DisputeTypeRequest { Name = text });
                if (created != null) { Types.Add(created); return created.Id; }
            }
            catch (ApiClientException ex) { ErrorText = ex.Message; return -1; }
            catch (Exception ex) { ErrorText = "创建类型失败：" + ex.Message; return -1; }
            return -1;
        }

        /// <summary>打开新增自定义类型内联行。</summary>
        private void OpenNewType()
        {
            NewTypeName = string.Empty;
            ErrorText = string.Empty;
            IsTypeAdd = true;
        }

        /// <summary>保存自定义类型：新建并选中。</summary>
        private async Task ConfirmNewTypeAsync()
        {
            string name = NewTypeName.Trim();
            if (name.Length == 0) { ErrorText = "请输入类型名称"; return; }
            if (Types.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))) { ErrorText = "类型已存在：" + name; return; }
            await RunAsync(async () =>
            {
                var created = await Api.CreateDisputeTypeAsync(new DisputeTypeRequest { Name = name });
                Types.Add(created);
                IsTypeAdd = false;
                NewTypeName = string.Empty;
                FormTypeText = created.Name;
                TypeId = created.Id;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "纠纷类型「" + created.Name + "」已新增";
            }, "类型已新增");
        }

        /// <summary>请求删除当前类型：打开二次确认浮层。</summary>
        private void RequestDeleteType()
        {
            string current = (FormTypeText ?? string.Empty).Trim();
            var type = Types.FirstOrDefault(t => string.Equals(t.Name, current, StringComparison.OrdinalIgnoreCase))
                       ?? Types.FirstOrDefault(t => t.Id == TypeId);
            if (type == null) { ErrorText = "请先选择需要删除的类型"; return; }
            _deleteTypeId = type.Id;
            DeleteTypeName = type.Name;
            IsTypeDeleteVisible = true;
        }

        /// <summary>确认删除类型（软删），重新选中第一个类型。</summary>
        private async Task ConfirmDeleteTypeAsync()
        {
            int id = _deleteTypeId;
            string name = DeleteTypeName;
            await RunAsync(async () =>
            {
                await Api.DeleteDisputeTypeAsync(id);
                var removed = Types.FirstOrDefault(t => t.Id == id);
                if (removed != null) Types.Remove(removed);
                IsTypeDeleteVisible = false;
                if (Types.Count > 0) { TypeId = Types[0].Id; FormTypeText = Types[0].Name; }
                else { TypeId = 0; FormTypeText = string.Empty; }
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "纠纷类型「" + name + "」已删除";
            }, "类型已删除");
        }

        private void Reset()
        {
            FormPropertyId = null;
            PartyAOwnerId = null;
            PartyBOwnerId = null;
            _selectedPropertyOption = PropertyOptions.FirstOrDefault(o => !o.PropertyId.HasValue);
            OnPropertyChanged(nameof(SelectedPropertyOption));
            _selectedOwnerAOption = OwnerAOptions.FirstOrDefault(o => !o.OwnerId.HasValue);
            OnPropertyChanged(nameof(SelectedOwnerAOption));
            _selectedOwnerBOption = OwnerBOptions.FirstOrDefault(o => !o.OwnerId.HasValue);
            OnPropertyChanged(nameof(SelectedOwnerBOption));
            PartyAName = string.Empty;
            PartyAPhone = string.Empty;
            PartyBName = string.Empty;
            PartyBPhone = string.Empty;
            Level = 0;
            ExpectedAt = null;
            Location = string.Empty;
            Detail = string.Empty;
            SelectedMediatorId = null;
            foreach (var m in Mediators) m.IsSelected = false;
            PropertySearch = string.Empty;
            PartyASearch = string.Empty;
            PartyBSearch = string.Empty;
            IsTypeAdd = false;
            NewTypeName = string.Empty;
            IsTypeDeleteVisible = false;
            _ = SearchPropertiesCoreAsync(string.Empty, 1);
            _ = SearchOwnersCoreAsync(true, string.Empty, 1);
            _ = SearchOwnersCoreAsync(false, string.Empty, 1);
        }
    }
}
