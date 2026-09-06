using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
        private string _batchPropertySearchText = string.Empty;
        private string _batchOwnerSearchText = string.Empty;
        private bool _isBatchSelectAll;
        private List<OwnerPropertyRelationDto> _batchSource = new List<OwnerPropertyRelationDto>();
        private readonly DispatcherTimer _searchDebounce;

        public OwnerRelationViewModel(IApiClient api) : base(api)
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounce.Tick += async (s, e) =>
            {
                _searchDebounce.Stop();
                await LoadAsync();
            };
            QueryCommand = new AsyncRelayCommand(LoadAsync);
            BindCommand = new RelayCommand(StartBind);
            ReleaseCommand = new RelayCommand<OwnerRelationRow>(r => { ReleaseRow = r; });
            ExportCommand = new AsyncRelayCommand(ExportAsync);
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(() => IsFormVisible = false);
            CancelReleaseCommand = new RelayCommand(() => { ReleaseRow = null; });
            ConfirmReleaseCommand = new AsyncRelayCommand(ConfirmReleaseAsync);
            OpenBatchCommand = new RelayCommand(() =>
            {
                BatchPropertySearchText = string.Empty;
                BatchOwnerSearchText = string.Empty;
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
        public ObservableCollection<PropertyDto> Properties { get; } = new ObservableCollection<PropertyDto>();
        public ObservableCollection<PropertyDto> FilteredProperties { get; } = new ObservableCollection<PropertyDto>();
        public ObservableCollection<OwnerDto> Owners { get; } = new ObservableCollection<OwnerDto>();
        public ObservableCollection<OwnerRow> FilteredOwners { get; } = new ObservableCollection<OwnerRow>();
        private ObservableCollection<OwnerRow> _ownerRows = new ObservableCollection<OwnerRow>();

        public string RoomKeyword
        {
            get { return _roomKeyword; }
            set { if (SetProperty(ref _roomKeyword, value)) { _searchDebounce.Stop(); _searchDebounce.Start(); } }
        }
        public string PropertySearchText { get { return _propertySearchText; } set { if (SetProperty(ref _propertySearchText, value)) { ApplyPropertyFilter(); } } }
        public string OwnerSearchText { get { return _ownerSearchText; } set { if (SetProperty(ref _ownerSearchText, value)) { ApplyOwnerFilter(); } } }
        public int RelTypeFilter { get { return _relTypeFilter; } set { if (SetProperty(ref _relTypeFilter, value)) { _ = LoadAsync(); } } }
        public int StatusFilter { get { return _statusFilter; } set { if (SetProperty(ref _statusFilter, value)) { _ = LoadAsync(); } } }
        public bool IsFormVisible { get { return _isFormVisible; } private set { SetProperty(ref _isFormVisible, value); } }
        public int? FormPropertyId { get { return _formPropertyId; } set { SetProperty(ref _formPropertyId, value); } }
        public int? FormOwnerId { get { return _formOwnerId; } set { SetProperty(ref _formOwnerId, value); } }
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
        public string BatchPropertySearchText { get { return _batchPropertySearchText; } set { if (SetProperty(ref _batchPropertySearchText, value)) { ApplyBatchFilter(); } } }
        public string BatchOwnerSearchText { get { return _batchOwnerSearchText; } set { if (SetProperty(ref _batchOwnerSearchText, value)) { ApplyBatchFilter(); } } }
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

        private void ApplyPropertyFilter()
        {
            string q = string.IsNullOrWhiteSpace(_propertySearchText) ? string.Empty : _propertySearchText.Trim();
            int? sel = _formPropertyId;
            FilteredProperties.Clear();
            foreach (var p in Properties)
            {
                bool match = q.Length == 0
                    || (p.UnitPath ?? string.Empty).Contains(q)
                    || (p.RoomNo ?? string.Empty).Contains(q)
                    || (p.OwnerName ?? string.Empty).Contains(q);
                // 固定已选房产：选中后 Text 变化会触发本过滤，若不固定会把已选项移出下拉、导致 WPF 丢失 FormPropertyId。
                if (match || (sel.HasValue && p.Id == sel.Value))
                {
                    FilteredProperties.Add(p);
                }
            }
        }

        private void ApplyOwnerFilter()
        {
            string q = string.IsNullOrWhiteSpace(_ownerSearchText) ? string.Empty : _ownerSearchText.Trim();
            int? sel = _formOwnerId;
            FilteredOwners.Clear();
            foreach (var o in _ownerRows)
            {
                bool match = q.Length == 0
                    || (o.Name ?? string.Empty).Contains(q)
                    || (o.Phone ?? string.Empty).Contains(q)
                    || (o.IdCard ?? string.Empty).Contains(q);
                if (match || (sel.HasValue && o.Id == sel.Value))
                {
                    FilteredOwners.Add(o);
                }
            }
        }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var query = new BaseInfoQueryRequest
                {
                    PageIndex = 1,
                    PageSize = 100,
                    Keyword = RoomKeyword
                };
                if (_relTypeFilter != 0) query.RelType = _relTypeFilter == 1 ? OwnerRelType.Owner : (_relTypeFilter == 2 ? OwnerRelType.CoOwner : OwnerRelType.RentRecord);
                if (_statusFilter != 0) query.RelStatus = _statusFilter == 1 ? OwnerRelStatus.Active : (_statusFilter == 2 ? OwnerRelStatus.Expiring : OwnerRelStatus.Released);
                var page = await Api.QueryRelationsAsync(query);
                Items.Clear();
                foreach (var dto in page.Items) Items.Add(new OwnerRelationRow { Dto = dto });
            }, "关系已加载");
        }

        private async void StartBind()
        {
            await RunAsync(async () =>
            {
                var props = await Api.QueryPropertiesAsync(new BaseInfoQueryRequest { PageIndex = 1, PageSize = 100 });
                Properties.Clear();
                FilteredProperties.Clear();
                foreach (var p in props.Items) Properties.Add(p);
                ApplyPropertyFilter();
                var owners = await Api.QueryOwnersAsync(new BaseInfoQueryRequest { PageIndex = 1, PageSize = 100 });
                Owners.Clear();
                _ownerRows.Clear();
                foreach (var o in owners.Items) { Owners.Add(o); _ownerRows.Add(new OwnerRow { Dto = o }); }
                ApplyOwnerFilter();
                PropertySearchText = string.Empty;
                OwnerSearchText = string.Empty;
                FormPropertyId = null;
                FormOwnerId = null;
                FormRelType = OwnerRelType.Owner;
                FormShare = 100;
                FormStart = DateTime.Today;
                FormEnd = null;
                IsFormVisible = true;
            }, "正在加载可绑定对象…");
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
            if (!FormPropertyId.HasValue) { ErrorText = "请选择房产"; return; }
            if (!FormOwnerId.HasValue) { ErrorText = "请选择业主"; return; }
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
                OnPropertyChanged(nameof(BatchPropertySearchText));
                OnPropertyChanged(nameof(BatchOwnerSearchText));
            }, "批量解除列表已加载");
        }

        private void ApplyBatchFilter()
        {
            string qp = string.IsNullOrWhiteSpace(_batchPropertySearchText) ? string.Empty : _batchPropertySearchText.Trim();
            string qo = string.IsNullOrWhiteSpace(_batchOwnerSearchText) ? string.Empty : _batchOwnerSearchText.Trim();
            BatchRelations.Clear();
            _isBatchSelectAll = false;
            OnPropertyChanged(nameof(IsBatchSelectAll));
            foreach (var dto in _batchSource)
            {
                string path = string.IsNullOrEmpty(dto.PropertyUnitPath) ? dto.PropertyRoomNo : dto.PropertyUnitPath;
                bool op = qp.Length == 0 || (path ?? string.Empty).Contains(qp) || (dto.PropertyRoomNo ?? string.Empty).Contains(qp);
                bool oo = qo.Length == 0 || (dto.OwnerName ?? string.Empty).Contains(qo) || (dto.OwnerPhone ?? string.Empty).Contains(qo);
                if (op && oo)
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
