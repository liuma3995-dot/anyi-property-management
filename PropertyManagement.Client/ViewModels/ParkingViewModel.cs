using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    /// <summary>车位维护行（PG-INF-04）。</summary>
    public class ParkingRow : ObservableObject
    {
        private bool _isChecked;
        public ParkingSpaceDto Dto { get; set; }
        /// <summary>批量选择标记。</summary>
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }
        public int Id { get { return Dto.Id; } }
        public string SpaceNo { get { return Dto.SpaceNo; } }
        public string Area { get { return Dto.Area ?? string.Empty; } }
        public string TypeText { get { return Dto.SpaceTypeText ?? string.Empty; } }
        public string StatusText { get { return Dto.StatusText ?? string.Empty; } }
        public Brush StatusBg { get { return Sw(Dto.Status); } }
        public Brush StatusBrush { get { return SwFg(Dto.Status); } }
        private static Brush Sw(ParkingSpaceStatus s)
        {
            switch (s) { case ParkingSpaceStatus.Owned: return Br("#E9F0EE"); case ParkingSpaceStatus.Rented: return Br("#E8F7F1"); case ParkingSpaceStatus.Repairing: return Br("#F1F3F7"); default: return Br("#FFF5DC"); }
        }
        private static Brush SwFg(ParkingSpaceStatus s)
        {
            switch (s) { case ParkingSpaceStatus.Owned: return Br("#1F4B43"); case ParkingSpaceStatus.Rented: return Br("#12805C"); case ParkingSpaceStatus.Repairing: return Br("#667085"); default: return Br("#B76E00"); }
        }
        private static Brush Br(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        public string BindProperty { get { return string.IsNullOrEmpty(Dto.BindingProperty) ? "未绑定" : Dto.BindingProperty; } }
        public string OwnerName { get { return string.IsNullOrEmpty(Dto.OwnerName) ? "—" : Dto.OwnerName; } }
        public decimal? MonthlyRent { get { return Dto.MonthlyRent; } }
        public string RentText { get { return Dto.MonthlyRent.HasValue ? ("¥" + Dto.MonthlyRent.Value.ToString("0.00") + "/" + (Dto.RentMode == RentMode.Yearly ? "年" : "月")) : "—"; } }
        public string RentTo { get { return Dto.RentTo?.ToString("yyyy-MM-dd") ?? "—"; } }
        public string RentPeriodText { get { return RentText + " / " + RentTo; } }
    }

    /// <summary>
    /// 车位表单「绑定业主」候选项（CHG-v1.1.0-11）。
    /// 说明：可编辑下拉框的显示文本必须与回写文本**完全一致**，否则 WPF 会在
    /// 「选中项 → 回写 Text → 重新同步显示文本」之间来回触发，实测导致栈溢出崩溃。
    /// 因此这里用 DisplayText 作为唯一显示口径，SelectedOwner 回写同一字符串。
    /// </summary>
    public class OwnerPickItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string DisplayText
        {
            get { return string.IsNullOrEmpty(Phone) ? (Name ?? string.Empty) : (Name ?? string.Empty) + "（" + Phone + "）"; }
        }
    }

    /// <summary>车位维护页（PG-INF-04，UC-INF-005，BR-INF-03）。</summary>
    public class ParkingViewModel : BaseInfoPageViewModel
    {
        private string _spaceNoKeyword = string.Empty;
        private int _typeFilter;
        private int _statusFilter;
        private bool _isFormVisible;
        private string _formSpaceNo = string.Empty;
        private string _formArea = string.Empty;
        private ParkingSpaceType _formType = ParkingSpaceType.PropertyRight;
        private ParkingSpaceStatus _formStatus = ParkingSpaceStatus.Vacant;
        private RentMode _formRentMode = RentMode.Monthly;
        private decimal? _formRent;
        private DateTime? _formRentTo;
        // CHG-v1.1.0-11：绑定业主（车位维护表单内直接维护，替代已下线的租金/租期至）
        private int? _formOwnerId;
        private string _ownerSearchText = string.Empty;
        private OwnerPickItem _selectedOwner;
        private string _cardUnderNote = "地下 B1/B2 两层";
        private int _total;
        private int _ownedCount;
        private int _rentedCount;
        private int _vacantCount;
        private int _globalRentedOwnerCount;
        private int _globalVacantDefense;
        private ParkingRow _viewRow;
        private ParkingRow _deleteRow;
        private int _editingId;
        private bool _isSelectAll;
        private bool _isBatchConfirmVisible;
        private string _batchConfirmMessage = string.Empty;
        private List<ParkingRow> _batchRows;
        private readonly DispatcherTimer _searchDebounce;

        public ParkingViewModel(IApiClient api) : base(api)
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounce.Tick += async (s, e) =>
            {
                _searchDebounce.Stop();
                await LoadAsync();
            };
            QueryCommand = new AsyncRelayCommand(LoadAsync);
            NewCommand = new RelayCommand(StartNew);
            ViewCommand = new RelayCommand<ParkingRow>(r => ViewRow = r);
            EditCommand = new RelayCommand<ParkingRow>(StartEdit);
            DeleteCommand = new RelayCommand<ParkingRow>(r => DeleteRow = r);
            BatchDeleteCommand = new RelayCommand(RequestBatchDelete);
            ExportCommand = new AsyncRelayCommand(ExportAsync);
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(() => IsFormVisible = false);
            CloseViewCommand = new RelayCommand(() => ViewRow = null);
            ConfirmDeleteCommand = new AsyncRelayCommand(ConfirmDeleteAsync);
            CancelDeleteCommand = new RelayCommand(() => DeleteRow = null);
            ConfirmBatchDeleteCommand = new AsyncRelayCommand(ConfirmBatchDeleteAsync);
            CancelBatchDeleteCommand = new RelayCommand(() => { IsBatchConfirmVisible = false; _batchRows = null; });
            SaveUnderNoteCommand = new AsyncRelayCommand(SaveUnderNoteAsync);
            _ = LoadAsync();
        }

        public ObservableCollection<ParkingRow> Items { get; } = new ObservableCollection<ParkingRow>();
        public ObservableCollection<OwnerPickItem> Owners { get; } = new ObservableCollection<OwnerPickItem>();

        public string SpaceNoKeyword
        {
            get { return _spaceNoKeyword; }
            set { if (SetProperty(ref _spaceNoKeyword, value)) { _searchDebounce.Stop(); _searchDebounce.Start(); } }
        }
        public int TypeFilter { get { return _typeFilter; } set { if (SetProperty(ref _typeFilter, value)) { _ = LoadAsync(); } } }
        public int StatusFilter { get { return _statusFilter; } set { if (SetProperty(ref _statusFilter, value)) { _ = LoadAsync(); } } }
        public int Total { get { return _total; } private set { SetProperty(ref _total, value); } }
        public int OwnedCount { get { return _ownedCount; } private set { SetProperty(ref _ownedCount, value); } }
        public int RentedCount { get { return _rentedCount; } private set { SetProperty(ref _rentedCount, value); } }
        public int VacantCount { get { return _vacantCount; } private set { SetProperty(ref _vacantCount, value); } }
        public string SoldRatioText { get { return _total == 0 ? "占比 0%" : ("占比 " + Math.Round((double)_ownedCount / _total * 100, 1) + "%"); } }
        /// <summary>
        /// CHG-v1.1.0-12：租金已下线（定价统一归收费项目），「已出租」卡片副文案由「月租金收入」改为「含绑定业主 N 户」。
        /// </summary>
        public string RentedOwnerHintText { get { return "含绑定业主 " + _globalRentedOwnerCount + " 户"; } }
        public string VacantHintText { get { return "含普通车位 " + _globalVacantDefense + " 个"; } }
        public bool IsFormVisible { get { return _isFormVisible; } private set { SetProperty(ref _isFormVisible, value); } }
        public bool IsEdit { get { return _editingId > 0; } }
        public string FormTitle { get { return _editingId > 0 ? "编辑车位" : "新增车位"; } }
        public ParkingRow ViewRow { get { return _viewRow; } set { SetProperty(ref _viewRow, value); OnPropertyChanged(nameof(IsViewVisible)); } }
        public bool IsViewVisible { get { return _viewRow != null; } }
        public ParkingRow DeleteRow { get { return _deleteRow; } set { SetProperty(ref _deleteRow, value); OnPropertyChanged(nameof(IsDeleteVisible)); } }
        public bool IsDeleteVisible { get { return _deleteRow != null; } }
        public bool IsBatchConfirmVisible { get { return _isBatchConfirmVisible; } private set { SetProperty(ref _isBatchConfirmVisible, value); } }
        public string BatchConfirmMessage { get { return _batchConfirmMessage; } private set { SetProperty(ref _batchConfirmMessage, value); } }
        /// <summary>全选：勾选/取消勾选当前列表全部行。</summary>
        public bool IsSelectAll
        {
            get { return _isSelectAll; }
            set
            {
                if (SetProperty(ref _isSelectAll, value))
                {
                    foreach (var r in Items) { r.IsChecked = value; }
                }
            }
        }
        public string FormSpaceNo { get { return _formSpaceNo; } set { SetProperty(ref _formSpaceNo, value); } }
        public string FormArea { get { return _formArea; } set { SetProperty(ref _formArea, value); } }
        public ParkingSpaceType FormType { get { return _formType; } set { SetProperty(ref _formType, value); } }
        public ParkingSpaceStatus FormStatus { get { return _formStatus; } set { SetProperty(ref _formStatus, value); } }
        public RentMode FormRentMode { get { return _formRentMode; } set { SetProperty(ref _formRentMode, value); } }
        public decimal? FormRent { get { return _formRent; } set { SetProperty(ref _formRent, value); } }
        public DateTime? FormRentTo { get { return _formRentTo; } set { SetProperty(ref _formRentTo, value); } }
        public string CardUnderNote { get { return _cardUnderNote; } set { SetProperty(ref _cardUnderNote, value); } }

        public IAsyncRelayCommand QueryCommand { get; }
        public IRelayCommand NewCommand { get; }
        public IRelayCommand<ParkingRow> ViewCommand { get; }
        public IRelayCommand<ParkingRow> EditCommand { get; }
        public IRelayCommand<ParkingRow> DeleteCommand { get; }
        public IRelayCommand BatchDeleteCommand { get; }
        public IAsyncRelayCommand ExportCommand { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand CloseViewCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteCommand { get; }
        public IRelayCommand CancelDeleteCommand { get; }
        public IAsyncRelayCommand ConfirmBatchDeleteCommand { get; }
        public IRelayCommand CancelBatchDeleteCommand { get; }
        public IAsyncRelayCommand SaveUnderNoteCommand { get; }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                // 表格：按当前筛选加载
                var query = new BaseInfoQueryRequest { PageIndex = 1, PageSize = 100000, SpaceNo = SpaceNoKeyword };
                if (_typeFilter != 0) query.SpaceType = _typeFilter == 1 ? ParkingSpaceType.PropertyRight : (_typeFilter == 2 ? ParkingSpaceType.CivilDefense : ParkingSpaceType.Temporary);
                if (_statusFilter != 0) query.SpaceStatus = _statusFilter == 1 ? ParkingSpaceStatus.Owned : (_statusFilter == 2 ? ParkingSpaceStatus.Rented : (_statusFilter == 3 ? ParkingSpaceStatus.Vacant : ParkingSpaceStatus.Repairing));
                var page = await Api.QueryParkingsAsync(query);
                Items.Clear();
                foreach (var dto in page.Items) Items.Add(new ParkingRow { Dto = dto });
                _isSelectAll = false;
                OnPropertyChanged(nameof(IsSelectAll));

                // 统计卡：全量口径，不受筛选/搜索影响（车位总数/已售/已租/空置/月租收入/普通类型空置）
                var stats = await Api.QueryParkingsAsync(new BaseInfoQueryRequest { PageIndex = 1, PageSize = 100000 });
                Total = stats.Total;
                OwnedCount = stats.Items.Count(x => x.Status == ParkingSpaceStatus.Owned);
                RentedCount = stats.Items.Count(x => x.Status == ParkingSpaceStatus.Rented);
                VacantCount = stats.Items.Count(x => x.Status == ParkingSpaceStatus.Vacant);
                // CHG-v1.1.0-12：租金口径下线 → 已出租卡片改为统计「已出租且已绑定业主」的车位数
                _globalRentedOwnerCount = stats.Items.Count(x => x.Status == ParkingSpaceStatus.Rented && x.OwnerId.HasValue);
                _globalVacantDefense = stats.Items.Count(x => x.Status == ParkingSpaceStatus.Vacant && x.SpaceType == ParkingSpaceType.CivilDefense);
                OnPropertyChanged(nameof(SoldRatioText));
                OnPropertyChanged(nameof(RentedOwnerHintText));
                OnPropertyChanged(nameof(VacantHintText));
                var note = await Api.GetParamAsync("parking_card_under_note");
                if (!string.IsNullOrWhiteSpace(note)) CardUnderNote = note;
            }, "车位已加载");
        }

        private async void StartNew()
        {
            await RunAsync(async () =>
            {
                await LoadOwnersAsync();
                OwnerSearchText = string.Empty;
                _editingId = 0;
                OnPropertyChanged(nameof(FormTitle));
                OnPropertyChanged(nameof(IsEdit));
                FormSpaceNo = string.Empty;
                FormArea = string.Empty;
                FormType = ParkingSpaceType.PropertyRight;
                FormStatus = ParkingSpaceStatus.Vacant;
                FormRentMode = RentMode.Monthly;
                // CHG-v1.1.0-12：绑定房产下线（保存时由服务端按「绑定业主」自动引用）
                FormOwnerId = null;
                _selectedOwner = null;
                OnPropertyChanged(nameof(SelectedOwner));
                FormRent = null;
                FormRentTo = null;
                IsFormVisible = true;
            }, "正在加载可绑定房产…");
        }

        private async void StartEdit(ParkingRow row)
        {
            if (row == null) return;
            await RunAsync(async () =>
            {
                await LoadOwnersAsync();
                OwnerSearchText = string.Empty;
                _editingId = row.Id;
                OnPropertyChanged(nameof(FormTitle));
                OnPropertyChanged(nameof(IsEdit));
                FormSpaceNo = row.Dto.SpaceNo;
                FormArea = row.Dto.Area ?? string.Empty;
                FormType = row.Dto.SpaceType;
                FormStatus = row.Dto.Status;
                FormRentMode = row.Dto.RentMode;
                FormOwnerId = row.Dto.OwnerId;
                _selectedOwner = Owners.FirstOrDefault(o => o.Id == row.Dto.OwnerId);
                OnPropertyChanged(nameof(SelectedOwner));
                OwnerSearchText = _selectedOwner == null ? string.Empty : _selectedOwner.DisplayText;
                FormRent = row.Dto.MonthlyRent;
                FormRentTo = row.Dto.RentTo;
                IsFormVisible = true;
            }, "正在加载车位信息…");
        }

        /// <summary>CHG-v1.1.0-11：加载绑定业主候选（一次性拉取，前端按姓名/手机过滤）。</summary>
        private async Task LoadOwnersAsync()
        {
            var owners = await Api.QueryOwnersAsync(new BaseInfoQueryRequest { PageIndex = 1, PageSize = 200 });
            Owners.Clear();
            foreach (var o in owners.Items)
            {
                Owners.Add(new OwnerPickItem { Id = o.Id, Name = o.Name, Phone = o.Phone });
            }
            OwnersView.Refresh();
        }

        /// <summary>CHG-v1.1.0-11：绑定业主候选视图（对齐「绑定房产」单框内检索：输入姓名/手机筛选）。</summary>
        public ICollectionView OwnersView
        {
            get { return _ownersView ?? (_ownersView = BuildOwnersView()); }
        }
        private ICollectionView _ownersView;

        private ICollectionView BuildOwnersView()
        {
            var view = CollectionViewSource.GetDefaultView(Owners);
            view.Filter = o =>
            {
                var owner = o as OwnerPickItem;
                if (owner == null) return true;
                if (_formOwnerId.HasValue && owner.Id == _formOwnerId.Value) return true;   // 已选中项恒保留
                string q = (_ownerSearchText ?? string.Empty).Trim();
                if (q.Length == 0) return true;
                return (owner.Name ?? string.Empty).Contains(q)
                    || (owner.Phone ?? string.Empty).Contains(q);
            };
            return view;
        }

        /// <summary>绑定业主输入框文本（输入检索关键字）。</summary>
        public string OwnerSearchText
        {
            get { return _ownerSearchText; }
            set { if (SetProperty(ref _ownerSearchText, value)) OwnersView.Refresh(); }
        }

        /// <summary>绑定业主选中项（保存口径为 FormOwnerId；输入过程中的瞬时 null 忽略）。</summary>
        public OwnerPickItem SelectedOwner
        {
            get { return _selectedOwner; }
            set
            {
                if (value == null) return;
                if (SetProperty(ref _selectedOwner, value)) FormOwnerId = value.Id;
                string display = value.DisplayText;   // 与 DisplayMemberPath 完全一致，避免 Text 回写震荡
                if (!string.Equals(_ownerSearchText, display, StringComparison.Ordinal)) OwnerSearchText = display;
            }
        }

        public int? FormOwnerId { get { return _formOwnerId; } set { SetProperty(ref _formOwnerId, value); } }

        private async Task SaveUnderNoteAsync()
        {
            await RunAsync(async () =>
            {
                await Api.SetParamAsync("parking_card_under_note", CardUnderNote);
            }, "统计卡说明已保存");
            if (string.IsNullOrEmpty(ErrorText))
                MessageBox.Show("车位总数提示文字已保存", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(FormSpaceNo)) { ErrorText = "车位编号不能为空"; return; }
            if (FormType == ParkingSpaceType.CivilDefense && FormStatus == ParkingSpaceStatus.Owned) { ErrorText = "普通车位不可标为出售"; return; }
            // CHG-v1.1.0-12：产权车位售出改为校验「绑定业主」（房产权属由服务端按业主自动引用）
            if (FormType == ParkingSpaceType.PropertyRight && FormStatus == ParkingSpaceStatus.Owned && !FormOwnerId.HasValue)
            { ErrorText = "产权车位标记为已售需绑定业主（房产权属将按其名下房产自动引用）"; return; }
            var request = new ParkingSpaceRequest
            {
                SpaceNo = FormSpaceNo.Trim(),
                Area = FormArea,
                SpaceType = FormType,
                Status = FormStatus,
                // CHG-v1.1.0-12：PropertyId 不再由表单提交，服务端按绑定业主自动引用房产
                PropertyId = null,
                // CHG-v1.1.0-11：表单内直接维护绑定业主；租金/租期至已从界面下线，
                // 但编辑时仍原样带回既有值，避免覆盖历史数据（存量值保留在库，不再作为计费依据）。
                OwnerId = FormOwnerId,
                MonthlyRent = FormRent,
                RentMode = FormRentMode,
                RentTo = FormRentTo
            };
            await RunAsync(async () =>
            {
                if (_editingId > 0) await Api.UpdateParkingAsync(_editingId, request);
                else await Api.CreateParkingAsync(request);
                IsFormVisible = false;
                await LoadAsync();
            }, _editingId > 0 ? "车位已更新" : "车位已新增");
        }

        private async Task ConfirmDeleteAsync()
        {
            var row = DeleteRow;
            if (row == null) return;
            await RunAsync(async () =>
            {
                await Api.DeleteParkingAsync(row.Id);
                DeleteRow = null;
                await LoadAsync();
            }, "车位已删除");
        }

        private void RequestBatchDelete()
        {
            var rows = Items.Where(x => x.IsChecked).ToList();
            if (rows.Count == 0) { ErrorText = "请先勾选要删除的车位"; return; }
            string desc = string.Join("、", rows.Take(3).Select(r => r.SpaceNo));
            if (rows.Count > 3) { desc += " 等 " + rows.Count + " 个"; }
            _batchRows = rows;
            BatchConfirmMessage = "将删除 " + rows.Count + " 个车位（软删除留痕）：\n" + desc;
            IsBatchConfirmVisible = true;
        }

        private async Task ConfirmBatchDeleteAsync()
        {
            var rows = _batchRows;
            IsBatchConfirmVisible = false;
            if (rows == null || rows.Count == 0) { return; }
            await RunAsync(async () =>
            {
                foreach (var r in rows) { await Api.DeleteParkingAsync(r.Id); }
                _batchRows = null;
                await LoadAsync();
            }, "已批量删除 " + rows.Count + " 个车位");
        }

        private async Task ExportAsync()
        {
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                var filter = new BaseInfoQueryRequest { PageIndex = 1, PageSize = 100000, SpaceNo = SpaceNoKeyword };
                if (_typeFilter != 0)
                    filter.SpaceType = _typeFilter == 1 ? ParkingSpaceType.PropertyRight : (_typeFilter == 2 ? ParkingSpaceType.CivilDefense : ParkingSpaceType.Temporary);
                if (_statusFilter != 0)
                    filter.SpaceStatus = _statusFilter == 1 ? ParkingSpaceStatus.Owned : (_statusFilter == 2 ? ParkingSpaceStatus.Rented : (_statusFilter == 3 ? ParkingSpaceStatus.Vacant : ParkingSpaceStatus.Repairing));

                var log = await Api.ExportAsync(new BaseInfoExportRequest
                {
                    ExportType = "parking",
                    Format = ExportFormat.Excel,
                    Filter = filter
                });
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "车位_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xlsx");
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
