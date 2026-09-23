using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
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
    /// <summary>业主-房产关系行（PG-INF-03）。</summary>
    public class OwnerRelationRow : ObservableObject
    {
        public OwnerPropertyRelationDto Dto { get; set; }
        public int Id { get { return Dto.Id; } }
        public string PropertyNo { get { return string.IsNullOrEmpty(Dto.PropertyUnitPath) ? Dto.PropertyRoomNo : Dto.PropertyUnitPath; } }
        public string PropertyCode
        {
            get
            {
                string path = string.IsNullOrEmpty(Dto.PropertyUnitPath) ? Dto.PropertyRoomNo : Dto.PropertyUnitPath;
                var parts = (path ?? string.Empty).Split('-');
                if (parts.Length >= 3)
                {
                    string b = new string(parts[0].Where(char.IsDigit).ToArray()).PadLeft(2, '0');
                    string u = new string(parts[1].Where(char.IsDigit).ToArray()).PadLeft(2, '0');
                    return "FC-" + b + u + "-" + parts[parts.Length - 1];
                }
                return "FC-" + Dto.PropertyId.ToString("D4");
            }
        }
        public string OwnerName { get { return string.IsNullOrEmpty(Dto.OwnerName) ? "—" : Dto.OwnerName; } }
        public string RelTypeText { get { return RelTypeName(Dto.RelType); } }
        public decimal Share { get { return Dto.Share; } }
        public string ShareText { get { return Dto.RelType == OwnerRelType.RentRecord ? "—" : Dto.Share.ToString("0") + "%"; } }
        public string StartText { get { return Dto.EffectiveAt == default ? "—" : Dto.EffectiveAt.ToString("yyyy-MM-dd"); } }
        public string EndText { get { return Dto.ExpireAt?.ToString("yyyy-MM-dd") ?? "长期"; } }
        public string PeriodText { get { return StartText + " ~ " + EndText; } }
        public string StatusText { get { return Dto.StatusText ?? string.Empty; } }
        public Brush StatusBg { get { return Sw(Dto.Status); } }
        public Brush StatusBrush { get { return SwFg(Dto.Status); } }
        private static Brush Sw(OwnerRelStatus s)
        {
            switch (s) { case OwnerRelStatus.Expiring: return Br("#FEF0C7"); case OwnerRelStatus.Released: return Br("#F1F3F7"); default: return Br("#E8F7F1"); }
        }
        private static Brush SwFg(OwnerRelStatus s)
        {
            switch (s) { case OwnerRelStatus.Expiring: return Br("#B54708"); case OwnerRelStatus.Released: return Br("#667085"); default: return Br("#12805C"); }
        }
        private static Brush Br(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        private static string RelTypeName(OwnerRelType t)
        {
            switch (t) { case OwnerRelType.Owner: return "业主"; case OwnerRelType.CoOwner: return "共有人"; default: return "租户备案"; }
        }
    }

    /// <summary>业主-房产关系页（PG-INF-03，UC-INF-004，BR-INF-02）。</summary>
    public class OwnerRelationViewModel : BaseInfoPageViewModel
    {
        private string _roomKeyword = string.Empty;
        private string _propertySearchText = string.Empty;
        private string _ownerSearchText = string.Empty;
        /// <summary>下拉候选分页大小（CHG-v1.3.1-01：负责人 2026-09-23 口径 = 与列表页一致，每页 20 条）。</summary>
        private const int PickerPageSize = 20;
        private int _propOptionPage = 1;
        private int _propOptionTotal;
        private int _ownerOptionPage = 1;
        private int _ownerOptionTotal;
        /// <summary>选中后回填展示名时不触发新一轮检索（避免用"楼栋+单元+房号"整串去搜出 0 条）。</summary>
        private bool _suppressOptionSearch;
        /// <summary>服务端检索的竞态保护：只认最后一次请求的结果。</summary>
        private int _propOptionSeq;
        private int _ownerOptionSeq;
        private int _pageIndex = 1;
        private int _pageSize = 20;
        private int _total;
        private int _relTypeFilter;
        private int _statusFilter;
        private bool _isFormVisible;
        private int? _formPropertyId;
        private int? _formOwnerId;
        private OwnerRelType _formRelType = OwnerRelType.Owner;
        private decimal _formShare;
        private DateTime _formStart = DateTime.Today;
        private DateTime? _formEnd;
        private OwnerRelationRow _releaseRow;
        private string _releaseReason = string.Empty;
        private bool _isBatchVisible;
        /// <summary>批量解除的检索关键字（v1.2.0 第 3 轮：原来的「房号 / 业主」两个搜索框合并为一个）。</summary>
        private string _batchSearchText = string.Empty;
        private bool _isBatchSelectAll;
        private List<OwnerPropertyRelationDto> _batchSource = new List<OwnerPropertyRelationDto>();
        private readonly DispatcherTimer _searchDebounce;
        private readonly DispatcherTimer _propertyOptionDebounce;
        private readonly DispatcherTimer _ownerOptionDebounce;

        public OwnerRelationViewModel(IApiClient api) : base(api)
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounce.Tick += async (s, e) =>
            {
                _searchDebounce.Stop();
                _pageIndex = 1;
                await LoadAsync();
            };
            // CHG-v1.3.1-01：手动绑定表单的房产 / 业主候选改为**服务端检索**（输入防抖 300ms），
            // 修掉「只取前 100 条 + 本地过滤 → 数据一多就搜什么都搜不到」。
            _propertyOptionDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _propertyOptionDebounce.Tick += async (s, e) =>
            {
                _propertyOptionDebounce.Stop();
                await RunAsync(() => LoadPropertyOptionsAsync(1), null);
            };
            _ownerOptionDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _ownerOptionDebounce.Tick += async (s, e) =>
            {
                _ownerOptionDebounce.Stop();
                await RunAsync(() => LoadOwnerOptionsAsync(1), null);
            };
            QueryCommand = new AsyncRelayCommand(LoadAsync);
            PropertyPrevPageCommand = new AsyncRelayCommand(() => LoadPropertyOptionsAsync(_propOptionPage - 1), () => CanPropertyPrev);
            PropertyNextPageCommand = new AsyncRelayCommand(() => LoadPropertyOptionsAsync(_propOptionPage + 1), () => CanPropertyNext);
            OwnerPrevPageCommand = new AsyncRelayCommand(() => LoadOwnerOptionsAsync(_ownerOptionPage - 1), () => CanOwnerPrev);
            OwnerNextPageCommand = new AsyncRelayCommand(() => LoadOwnerOptionsAsync(_ownerOptionPage + 1), () => CanOwnerNext);
            // v1.3.0：业主-房产关系列表补齐翻页与记录条数（与「房产列表」同口径）。
            PrevPageCommand = new RelayCommand(() => { if (_pageIndex > 1) { _pageIndex--; _ = LoadAsync(); } });
            NextPageCommand = new RelayCommand(() => { if (_pageIndex * _pageSize < _total) { _pageIndex++; _ = LoadAsync(); } });
            BindCommand = new RelayCommand(StartBind);
            ReleaseCommand = new RelayCommand<OwnerRelationRow>(r => { ReleaseRow = r; });
            ExportCommand = new AsyncRelayCommand(ExportAsync);
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(() => IsFormVisible = false);
            CancelReleaseCommand = new RelayCommand(() => { ReleaseRow = null; });
            ConfirmReleaseCommand = new AsyncRelayCommand(ConfirmReleaseAsync);
            OpenBatchCommand = new RelayCommand(() =>
            {
                BatchSearchText = string.Empty;
                _isBatchSelectAll = false;
                OnPropertyChanged(nameof(IsBatchSelectAll));
                IsBatchVisible = true;
                _ = LoadBatchAsync();
            });
            CancelBatchCommand = new RelayCommand(() => { IsBatchVisible = false; BatchRelations.Clear(); });
            ConfirmBatchCommand = new AsyncRelayCommand(ConfirmBatchAsync);
            _ = LoadAsync();
        }

        public ObservableCollection<OwnerRelationRow> Items { get; } = new ObservableCollection<OwnerRelationRow>();
        /// <summary>手动绑定：房产候选（当前页，服务端检索结果）。</summary>
        public ObservableCollection<PropertyDto> PropertyOptions { get; } = new ObservableCollection<PropertyDto>();
        /// <summary>手动绑定：业主候选（当前页，服务端检索结果）。</summary>
        public ObservableCollection<OwnerRow> OwnerOptions { get; } = new ObservableCollection<OwnerRow>();

        /// <summary>记录条数与翻页（v1.3.0：与「房产列表」同口径的「共 N 条记录 · 第 X/Y 页」）。</summary>
        public int Total
        {
            get { return _total; }
            private set
            {
                if (SetProperty(ref _total, value))
                {
                    OnPropertyChanged(nameof(PageInfo));
                    OnPropertyChanged(nameof(TotalText));
                    OnPropertyChanged(nameof(CanPrev));
                    OnPropertyChanged(nameof(CanNext));
                }
            }
        }
        public string PageInfo
        {
            get
            {
                return _total == 0
                    ? "共 0 条记录 · 第 0/1 页"
                    : ("共 " + _total + " 条记录 · 第 " + _pageIndex + "/" + ((_total + _pageSize - 1) / _pageSize) + " 页");
            }
        }
        public string TotalText { get { return PageInfo; } }
        public bool CanPrev { get { return _pageIndex > 1; } }
        public bool CanNext { get { return _pageIndex * _pageSize < _total; } }
        /// <summary>房产候选页脚（下拉浮层内部翻页行）：共 N 条 · 第 X/Y 页。</summary>
        public string PropertyPageText { get { return PageTextOf(_propOptionTotal, _propOptionPage); } }
        /// <summary>业主候选页脚（同上）。</summary>
        public string OwnerPageText { get { return PageTextOf(_ownerOptionTotal, _ownerOptionPage); } }
        public bool CanPropertyPrev { get { return _propOptionPage > 1; } }
        public bool CanPropertyNext { get { return _propOptionPage * PickerPageSize < _propOptionTotal; } }
        public bool CanOwnerPrev { get { return _ownerOptionPage > 1; } }
        public bool CanOwnerNext { get { return _ownerOptionPage * PickerPageSize < _ownerOptionTotal; } }

        private static string PageTextOf(int total, int page)
        {
            return "共 " + total + " 条 · 第 " + page + "/" + LastPageOf(total) + " 页";
        }

        private static int LastPageOf(int total)
        {
            return total <= 0 ? 1 : ((total + PickerPageSize - 1) / PickerPageSize);
        }

        public string RoomKeyword
        {
            get { return _roomKeyword; }
            set { if (SetProperty(ref _roomKeyword, value)) { _searchDebounce.Stop(); _searchDebounce.Start(); } }
        }
        /// <summary>房产下拉的检索文字：变化后防抖 300ms 走**服务端检索**（CHG-v1.3.1-01）。</summary>
        public string PropertySearchText
        {
            get { return _propertySearchText; }
            set
            {
                if (!SetProperty(ref _propertySearchText, value)) { return; }
                if (_suppressOptionSearch) { return; }
                _propertyOptionDebounce.Stop();
                _propertyOptionDebounce.Start();
            }
        }
        /// <summary>业主下拉的检索文字：同上。</summary>
        public string OwnerSearchText
        {
            get { return _ownerSearchText; }
            set
            {
                if (!SetProperty(ref _ownerSearchText, value)) { return; }
                if (_suppressOptionSearch) { return; }
                _ownerOptionDebounce.Stop();
                _ownerOptionDebounce.Start();
            }
        }
        public int RelTypeFilter { get { return _relTypeFilter; } set { if (SetProperty(ref _relTypeFilter, value)) { _ = LoadAsync(); } } }
        public int StatusFilter { get { return _statusFilter; } set { if (SetProperty(ref _statusFilter, value)) { _ = LoadAsync(); } } }
        public bool IsFormVisible { get { return _isFormVisible; } private set { SetProperty(ref _isFormVisible, value); } }
        public int? FormPropertyId { get { return _formPropertyId; } set { SetProperty(ref _formPropertyId, value); } }
        public int? FormOwnerId { get { return _formOwnerId; } set { SetProperty(ref _formOwnerId, value); } }

        /// <summary>
        /// 房产下拉选中项（对齐「纠纷登记」单框内检索模式：一个可编辑下拉同时承担检索与选择）。
        /// 输入过程中的瞬时 null 忽略，避免过滤刷新把已选值清空（FormPropertyId 才是保存口径）。
        /// </summary>
        public PropertyDto SelectedProperty
        {
            get { return _selectedProperty; }
            set
            {
                if (value == null) return;
                if (SetProperty(ref _selectedProperty, value)) FormPropertyId = value.Id;
                // 选中后把输入框文本替换为所选对象的展示文本，避免残留检索关键字（现场反馈）；
                // CHG-v1.3.1-01：这是"程序化回填"，不再触发新一轮服务端检索。
                _propertyOptionDebounce.Stop();
                string display = string.IsNullOrEmpty(value.UnitPath) ? (value.RoomNo ?? string.Empty) : value.UnitPath;
                if (!string.Equals(_propertySearchText, display, StringComparison.Ordinal))
                {
                    SetOptionSearchText(true, display);
                }
            }
        }
        private PropertyDto _selectedProperty;

        /// <summary>业主下拉选中项（同上）。</summary>
        public OwnerRow SelectedOwner
        {
            get { return _selectedOwner; }
            set
            {
                if (value == null) return;
                if (SetProperty(ref _selectedOwner, value)) FormOwnerId = value.Id;
                _ownerOptionDebounce.Stop();
                string display = value.OwnerDisplayName ?? value.Name ?? string.Empty;
                if (!string.Equals(_ownerSearchText, display, StringComparison.Ordinal))
                {
                    SetOptionSearchText(false, display);
                }
            }
        }
        private OwnerRow _selectedOwner;

        /// <summary>程序化回填检索文字（选中后展示名）—— 不触发新一轮检索。</summary>
        private void SetOptionSearchText(bool property, string text)
        {
            _suppressOptionSearch = true;
            try
            {
                if (property) { PropertySearchText = text; }
                else { OwnerSearchText = text; }
            }
            finally
            {
                _suppressOptionSearch = false;
            }
        }
        public OwnerRelType FormRelType
        {
            get { return _formRelType; }
            set
            {
                if (SetProperty(ref _formRelType, value))
                {
                    ApplyShareDefault();
                    OnPropertyChanged(nameof(IsShareVisible));
                }
            }
        }
        public bool IsShareVisible { get { return _formRelType != OwnerRelType.RentRecord; } }
        public decimal FormShare { get { return _formShare; } set { SetProperty(ref _formShare, value); } }
        public DateTime FormStart { get { return _formStart; } set { SetProperty(ref _formStart, value); } }
        public DateTime? FormEnd { get { return _formEnd; } set { SetProperty(ref _formEnd, value); } }
        public OwnerRelationRow ReleaseRow { get { return _releaseRow; } set { SetProperty(ref _releaseRow, value); OnPropertyChanged(nameof(IsReleaseVisible)); } }
        public bool IsReleaseVisible { get { return _releaseRow != null; } }
        public string ReleaseReason { get { return _releaseReason; } set { SetProperty(ref _releaseReason, value); } }
        public bool IsBatchVisible { get { return _isBatchVisible; } set { SetProperty(ref _isBatchVisible, value); } }
        /// <summary>
        /// 批量解除检索框（v1.2.0 第 3 轮）：一个框同时匹配 房号/房产编号 与 业主姓名/电话，
        /// 替代原先「输入房号 / 输入姓名」两个搜索框。
        /// </summary>
        public string BatchSearchText { get { return _batchSearchText; } set { if (SetProperty(ref _batchSearchText, value)) { ApplyBatchFilter(); } } }
        /// <summary>批量解除「全选」：勾选/取消勾选全部批次行。</summary>
        public bool IsBatchSelectAll
        {
            get { return _isBatchSelectAll; }
            set
            {
                if (SetProperty(ref _isBatchSelectAll, value))
                {
                    foreach (var r in BatchRelations) { r.IsChecked = value; }
                }
            }
        }
        public ObservableCollection<BatchRelationRow> BatchRelations { get; } = new ObservableCollection<BatchRelationRow>();

        public IAsyncRelayCommand QueryCommand { get; }
        public IRelayCommand PrevPageCommand { get; }
        public IRelayCommand NextPageCommand { get; }
        /// <summary>下拉浮层内的翻页（CHG-v1.3.1-01：作用域 = 当前检索关键字）。</summary>
        public IAsyncRelayCommand PropertyPrevPageCommand { get; }
        public IAsyncRelayCommand PropertyNextPageCommand { get; }
        public IAsyncRelayCommand OwnerPrevPageCommand { get; }
        public IAsyncRelayCommand OwnerNextPageCommand { get; }
        public IRelayCommand BindCommand { get; }
        public IAsyncRelayCommand ExportCommand { get; }
        public IRelayCommand OpenBatchCommand { get; }
        public IRelayCommand CancelBatchCommand { get; }
        public IAsyncRelayCommand ConfirmBatchCommand { get; }
        public IRelayCommand<OwnerRelationRow> ReleaseCommand { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand CancelReleaseCommand { get; }
        public IAsyncRelayCommand ConfirmReleaseCommand { get; }

        /// <summary>
        /// 房产候选：按当前关键字走**服务端检索**，只渲染当页（CHG-v1.3.1-01）。
        /// v1.3.0 及以前是「取前 100 条 + 本地过滤」—— 房产多于 100 套时，第 101 条之后的房产永远搜不到，
        /// 负责人现场实测（349 套房产）即为此因。
        /// </summary>
        private async Task LoadPropertyOptionsAsync(int pageIndex)
        {
            string keyword = string.IsNullOrWhiteSpace(_propertySearchText) ? null : _propertySearchText.Trim();
            int target = pageIndex < 1 ? 1 : pageIndex;
            int seq = ++_propOptionSeq;

            PageResult<PropertyDto> page = await Api.QueryPropertiesAsync(new BaseInfoQueryRequest
            {
                PageIndex = target,
                PageSize = PickerPageSize,
                Keyword = keyword
            });
            if (seq != _propOptionSeq) { return; }

            // 关键字收窄后页码可能越界（例如停在第 5 页时又输入了更严的关键字）→ 回到最后一页重取
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
                if (seq != _propOptionSeq) { return; }
            }

            _propOptionTotal = page.Total;
            _propOptionPage = target;
            PropertyOptions.Clear();
            foreach (var item in page.Items ?? new List<PropertyDto>()) { PropertyOptions.Add(item); }
            RaisePropertyOptionState();
        }

        /// <summary>业主候选：与房产同口径（服务端检索 + 每页 20 条）。</summary>
        private async Task LoadOwnerOptionsAsync(int pageIndex)
        {
            string keyword = string.IsNullOrWhiteSpace(_ownerSearchText) ? null : _ownerSearchText.Trim();
            int target = pageIndex < 1 ? 1 : pageIndex;
            int seq = ++_ownerOptionSeq;

            PageResult<OwnerDto> page = await Api.QueryOwnersAsync(new BaseInfoQueryRequest
            {
                PageIndex = target,
                PageSize = PickerPageSize,
                Keyword = keyword
            });
            if (seq != _ownerOptionSeq) { return; }

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
                if (seq != _ownerOptionSeq) { return; }
            }

            _ownerOptionTotal = page.Total;
            _ownerOptionPage = target;
            OwnerOptions.Clear();
            foreach (var dto in page.Items ?? new List<OwnerDto>()) { OwnerOptions.Add(new OwnerRow { Dto = dto }); }
            RaiseOwnerOptionState();
        }

        private void RaisePropertyOptionState()
        {
            OnPropertyChanged(nameof(PropertyPageText));
            OnPropertyChanged(nameof(CanPropertyPrev));
            OnPropertyChanged(nameof(CanPropertyNext));
            PropertyPrevPageCommand.NotifyCanExecuteChanged();
            PropertyNextPageCommand.NotifyCanExecuteChanged();
        }

        private void RaiseOwnerOptionState()
        {
            OnPropertyChanged(nameof(OwnerPageText));
            OnPropertyChanged(nameof(CanOwnerPrev));
            OnPropertyChanged(nameof(CanOwnerNext));
            OwnerPrevPageCommand.NotifyCanExecuteChanged();
            OwnerNextPageCommand.NotifyCanExecuteChanged();
        }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var query = new BaseInfoQueryRequest
                {
                    PageIndex = _pageIndex,
                    PageSize = _pageSize,
                    Keyword = RoomKeyword
                };
                if (_relTypeFilter != 0) query.RelType = _relTypeFilter == 1 ? OwnerRelType.Owner : (_relTypeFilter == 2 ? OwnerRelType.CoOwner : OwnerRelType.RentRecord);
                if (_statusFilter != 0) query.RelStatus = _statusFilter == 1 ? OwnerRelStatus.Active : (_statusFilter == 2 ? OwnerRelStatus.Expiring : OwnerRelStatus.Released);
                var page = await Api.QueryRelationsAsync(query);
                Total = page.Total;
                // 页码越界保护（如筛选后总页数变少）：回到最后一页重新取数
                int lastPage = _total == 0 ? 1 : ((_total + _pageSize - 1) / _pageSize);
                if (_pageIndex > lastPage)
                {
                    _pageIndex = lastPage;
                    query.PageIndex = _pageIndex;
                    page = await Api.QueryRelationsAsync(query);
                }
                Items.Clear();
                foreach (var dto in page.Items) Items.Add(new OwnerRelationRow { Dto = dto });
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(TotalText));
                OnPropertyChanged(nameof(CanPrev));
                OnPropertyChanged(nameof(CanNext));
            }, "关系已加载");
        }

        private async void StartBind()
        {
            await RunAsync(async () =>
            {
                _propertyOptionDebounce.Stop();
                _ownerOptionDebounce.Stop();
                SetOptionSearchText(true, string.Empty);
                SetOptionSearchText(false, string.Empty);
                FormPropertyId = null;
                FormOwnerId = null;
                _selectedProperty = null;
                _selectedOwner = null;
                OnPropertyChanged(nameof(SelectedProperty));
                OnPropertyChanged(nameof(SelectedOwner));
                await LoadPropertyOptionsAsync(1);
                await LoadOwnerOptionsAsync(1);
                FormRelType = OwnerRelType.Owner;
                FormShare = 100;
                FormStart = DateTime.Today;
                FormEnd = null;
                IsFormVisible = true;
            }, null);

            // CHG-v1.3.1-01：v1.3.0 及以前把「正在加载可绑定对象…」写在**成功文案**上，
            // 于是加载完成后状态栏反而永久停在这句话（现场看起来像卡死）。这里改为加载结果的实数汇报。
            if (string.IsNullOrEmpty(ErrorText))
            {
                StatusText = DateTime.Now.ToString("HH:mm:ss ") +
                    "可绑定对象已加载（房产 " + _propOptionTotal + " · 业主 " + _ownerOptionTotal + "）";
            }
        }

        /// <summary>切换关系类型时同步份额默认值：业主100%、共有人50%、租户备案不适用(0)。</summary>
        private void ApplyShareDefault()
        {
            if (_formRelType == OwnerRelType.Owner) FormShare = 100;
            else if (_formRelType == OwnerRelType.CoOwner) FormShare = 50;
            else FormShare = 0;
        }

        private async Task SaveAsync()
        {
            // F-04：手动键入唯一可匹配的房号/姓名/电话时自动落选；否则给出明确指引
            // CHG-v1.3.1-01：精确匹配改为**服务端校验**（不再只在本页候选里找）。
            if (!FormPropertyId.HasValue) { FormPropertyId = await ResolvePropertyIdFromTextAsync(); }
            if (!FormPropertyId.HasValue) { ErrorText = "请从下拉列表选择房产（或输入可唯一匹配的房号）"; return; }
            if (!FormOwnerId.HasValue) { FormOwnerId = await ResolveOwnerIdFromTextAsync(); }
            if (!FormOwnerId.HasValue) { ErrorText = "请从下拉列表选择业主（或输入可唯一匹配的姓名/电话）"; return; }
            var request = new OwnerPropertyRelationRequest
            {
                PropertyId = FormPropertyId.Value,
                OwnerId = FormOwnerId.Value,
                RelType = FormRelType,
                Share = FormShare,
                EffectiveAt = FormStart,
                ExpireAt = FormEnd,
                Status = ResolveStatus(FormEnd)
            };
            await RunAsync(async () =>
            {
                await Api.CreateRelationAsync(request);
                IsFormVisible = false;
                await LoadAsync();
            }, "关系已绑定");
        }

        /// <summary>
        /// F-04：把搜索框文本解析为唯一房产（完整路径或房号精确匹配）。
        /// CHG-v1.3.1-01：先看当页候选，命中不到再按关键字走服务端精确校验 —— 覆盖"目标不在当前页"的场景。
        /// </summary>
        private async Task<int?> ResolvePropertyIdFromTextAsync()
        {
            string q = (_propertySearchText ?? string.Empty).Trim();
            if (q.Length == 0) { return null; }
            var hits = PropertyOptions.Where(p => PropertyTextMatches(p, q)).ToList();
            if (hits.Count == 1) { return hits[0].Id; }
            PageResult<PropertyDto> page = await Api.QueryPropertiesAsync(new BaseInfoQueryRequest
            {
                PageIndex = 1,
                PageSize = PickerPageSize,
                Keyword = q
            });
            hits = (page.Items ?? new List<PropertyDto>()).Where(p => PropertyTextMatches(p, q)).ToList();
            return hits.Count == 1 ? (int?)hits[0].Id : null;
        }

        private static bool PropertyTextMatches(PropertyDto p, string q)
        {
            return string.Equals((p.UnitPath ?? string.Empty).Trim(), q, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals((p.RoomNo ?? string.Empty).Trim(), q, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>F-04：把搜索框文本解析为唯一业主（姓名 / 电话 / 证件号 / 展示名精确匹配）。</summary>
        private async Task<int?> ResolveOwnerIdFromTextAsync()
        {
            string q = (_ownerSearchText ?? string.Empty).Trim();
            if (q.Length == 0) { return null; }
            var hits = OwnerOptions.Where(o => OwnerTextMatches(o, q)).ToList();
            if (hits.Count == 1) { return hits[0].Id; }
            PageResult<OwnerDto> page = await Api.QueryOwnersAsync(new BaseInfoQueryRequest
            {
                PageIndex = 1,
                PageSize = PickerPageSize,
                Keyword = q
            });
            hits = (page.Items ?? new List<OwnerDto>()).Select(dto => new OwnerRow { Dto = dto })
                .Where(o => OwnerTextMatches(o, q)).ToList();
            return hits.Count == 1 ? (int?)hits[0].Id : null;
        }

        private static bool OwnerTextMatches(OwnerRow o, string q)
        {
            return string.Equals((o.Name ?? string.Empty).Trim(), q, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals((o.Phone ?? string.Empty).Trim(), q, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals((o.IdCard ?? string.Empty).Trim(), q, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals((o.OwnerDisplayName ?? string.Empty).Trim(), q, StringComparison.OrdinalIgnoreCase);
        }

        private async Task ConfirmReleaseAsync()
        {
            var row = ReleaseRow;
            if (row == null) return;
            await RunAsync(async () =>
            {
                await Api.ReleaseRelationAsync(row.Id, ReleaseReason);
                ReleaseRow = null;
                ReleaseReason = string.Empty;
                await LoadAsync();
            }, "关系已解除（已留痕）");
        }

        private async Task LoadBatchAsync()
        {
            await RunAsync(async () =>
            {
                var page = await Api.QueryRelationsAsync(new BaseInfoQueryRequest { PageIndex = 1, PageSize = 1000, RelStatus = OwnerRelStatus.Active });
                _batchSource = page.Items;
                ApplyBatchFilter();
                OnPropertyChanged(nameof(BatchSearchText));
            }, "批量解除列表已加载");
        }

        private void ApplyBatchFilter()
        {
            // v1.2.0 第 3 轮：单框检索 —— 一个关键字同时匹配 房号/房产编号 与 业主姓名/电话
            string q = string.IsNullOrWhiteSpace(_batchSearchText) ? string.Empty : _batchSearchText.Trim();
            BatchRelations.Clear();
            _isBatchSelectAll = false;
            OnPropertyChanged(nameof(IsBatchSelectAll));
            foreach (var dto in _batchSource)
            {
                string path = string.IsNullOrEmpty(dto.PropertyUnitPath) ? dto.PropertyRoomNo : dto.PropertyUnitPath;
                bool hit = q.Length == 0
                    || (path ?? string.Empty).Contains(q)
                    || (dto.PropertyRoomNo ?? string.Empty).Contains(q)
                    || (dto.OwnerName ?? string.Empty).Contains(q)
                    || (dto.OwnerPhone ?? string.Empty).Contains(q);
                if (hit)
                {
                    var row = new BatchRelationRow { Dto = dto };
                    row.PropertyChanged += OnBatchRowPropertyChanged;
                    BatchRelations.Add(row);
                }
            }
        }

        private async Task ConfirmBatchAsync()
        {
            var selected = BatchRelations.Where(x => x.IsChecked).Select(x => x.Dto.Id).ToList();
            if (selected.Count == 0) { ErrorText = "请先勾选要解除的关系"; return; }
            await RunAsync(async () =>
            {
                foreach (var id in selected) await Api.ReleaseRelationAsync(id, "批量解除");
                IsBatchVisible = false;
                BatchRelations.Clear();
                _isBatchSelectAll = false;
                OnPropertyChanged(nameof(IsBatchSelectAll));
                await LoadAsync();
            }, "已批量解除 " + selected.Count + " 条关系");
        }

        /// <summary>导出业主-房产关系（UC-INF-007，参考房产列表/业主档案导出实现）。</summary>
        private async Task ExportAsync()
        {
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                var filter = new BaseInfoQueryRequest
                {
                    PageIndex = 1,
                    PageSize = 100000,
                    Keyword = RoomKeyword
                };
                if (_relTypeFilter != 0)
                    filter.RelType = _relTypeFilter == 1 ? OwnerRelType.Owner : (_relTypeFilter == 2 ? OwnerRelType.CoOwner : OwnerRelType.RentRecord);
                if (_statusFilter != 0)
                    filter.RelStatus = _statusFilter == 1 ? OwnerRelStatus.Active : (_statusFilter == 2 ? OwnerRelStatus.Expiring : OwnerRelStatus.Released);

                var log = await Api.ExportAsync(new BaseInfoExportRequest
                {
                    ExportType = "relation",
                    Format = ExportFormat.Excel,
                    Filter = filter
                });
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "业主房产关系_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xlsx");
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

        /// <summary>全选状态与行勾选同步：任一/全部勾选变化时刷新表头全选框。</summary>
        private void OnBatchRowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(BatchRelationRow.IsChecked), StringComparison.Ordinal)) { return; }
            bool all = BatchRelations.Count > 0 && BatchRelations.All(x => x.IsChecked);
            SetProperty(ref _isBatchSelectAll, all, nameof(IsBatchSelectAll));
        }

        private static OwnerRelStatus ResolveStatus(DateTime? end)
        {
            if (!end.HasValue) return OwnerRelStatus.Active;
            if (end.Value < DateTime.Today) return OwnerRelStatus.Released;
            if (end.Value <= DateTime.Today.AddDays(30)) return OwnerRelStatus.Expiring;
            return OwnerRelStatus.Active;
        }
    }

    public class BatchRelationRow : ObservableObject
    {
        private bool _isChecked;
        public OwnerPropertyRelationDto Dto { get; set; }
        public int Id { get { return Dto.Id; } }
        public string PropertyNo { get { return string.IsNullOrEmpty(Dto.PropertyUnitPath) ? Dto.PropertyRoomNo : Dto.PropertyUnitPath; } }
        public string OwnerName { get { return string.IsNullOrEmpty(Dto.OwnerName) ? "—" : Dto.OwnerName; } }
        public string RelTypeText { get { return Dto.RelType == OwnerRelType.Owner ? "业主" : (Dto.RelType == OwnerRelType.CoOwner ? "共有人" : "租户备案"); } }
        public string StatusText { get { return Dto.StatusText ?? string.Empty; } }
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }
    }
}
