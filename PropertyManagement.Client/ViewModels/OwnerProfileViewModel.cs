using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>业主档案页（PG-INF-02，UC-INF-003，BR-INF-04）。</summary>
    public class OwnerProfileViewModel : BaseInfoPageViewModel
    {
        private string _searchText = string.Empty;
        private OwnerDto _selected;
        private int? _selectedOwnerId;
        /// <summary>下拉当前选中行（CHG-v1.3.1-01：下拉改为服务端检索 + 下拉内翻页）。</summary>
        private OwnerRow _selectedOwnerItem;
        /// <summary>下拉候选分页（负责人 2026-09-23 口径：与列表页一致，每页 20 条）。</summary>
        private const int PickerPageSize = 20;
        private int _ownerPage = 1;
        private int _ownerTotal;
        private int _ownerSearchSeq;
        private bool _isFormVisible;
        private int _editingId;
        private string _formName = string.Empty;
        private OwnerIdCardType _formIdCardType = OwnerIdCardType.IdCard;
        private string _formIdCard = string.Empty;
        private string _formPhone = string.Empty;
        private string _formAddress = string.Empty;
        private string _formEmergencyName = string.Empty;
        private string _formEmergencyPhone = string.Empty;
        private DateTime? _formCheckIn;
        private OwnerStatus _formStatus = OwnerStatus.Living;
        private OwnerPropertyRelationRow _viewRelationRow;
        private bool _isDeleteConfirmVisible;
        private string _deleteConfirmOwnerName = string.Empty;
        private bool _isBatchDialogVisible;
        private bool _isBatchSelectAll;
        private readonly DispatcherTimer _searchDebounce;

        public OwnerProfileViewModel(IApiClient api) : base(api)
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounce.Tick += async (s, e) =>
            {
                _searchDebounce.Stop();
                await SearchOwnersAsync();
            };
            SearchCommand = new AsyncRelayCommand(LoadAsync);
            // CHG-v1.3.1-01：下拉浮层内翻页（作用域 = 当前检索关键字）
            OwnerPrevPageCommand = new AsyncRelayCommand(() => SearchOwnersCoreAsync(CurrentOwnerKeyword(), _ownerPage - 1), () => CanOwnerPrev);
            OwnerNextPageCommand = new AsyncRelayCommand(() => SearchOwnersCoreAsync(CurrentOwnerKeyword(), _ownerPage + 1), () => CanOwnerNext);
            SelectCommand = new AsyncRelayCommand<OwnerRow>(SelectOwner);
            EditCommand = new RelayCommand(StartEdit);
            NewCommand = new RelayCommand(StartNew);
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(() => IsFormVisible = false);
            DeleteCommand = new RelayCommand(RequestDelete);
            ConfirmDeleteCommand = new AsyncRelayCommand(ConfirmDeleteAsync);
            CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirmVisible = false);
            BatchDeleteCommand = new RelayCommand(OpenBatchDelete);
            ConfirmBatchDeleteCommand = new AsyncRelayCommand(ConfirmBatchDeleteAsync);
            CancelBatchDeleteCommand = new RelayCommand(CloseBatchDelete);
            ExportCommand = new AsyncRelayCommand(ExportAsync);
            ExportProfilePdfCommand = new AsyncRelayCommand(ExportProfilePdfAsync);
            ExportAllProfilesPdfCommand = new AsyncRelayCommand(ExportAllProfilesPdfAsync);
            ViewRelationCommand = new RelayCommand<OwnerPropertyRelationRow>(row => ViewRelationRow = row);
            CloseViewCommand = new RelayCommand(() => ViewRelationRow = null);
            _ = RefreshAsync();
        }

        public ObservableCollection<OwnerRow> Owners { get; } = new ObservableCollection<OwnerRow>();
        public ObservableCollection<OwnerRow> FilteredOwners { get; } = new ObservableCollection<OwnerRow>();
        public ObservableCollection<OwnerRow> BatchOwners { get; } = new ObservableCollection<OwnerRow>();
        public ObservableCollection<OwnerPropertyRelationRow> Relations { get; } = new ObservableCollection<OwnerPropertyRelationRow>();
        public ObservableCollection<ChangeLogRow> ChangeLogs { get; } = new ObservableCollection<ChangeLogRow>();

        /// <summary>业主搜索框文本：输入后防抖走后端 keyword 检索（对齐房产列表搜索框方法）。</summary>
        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _searchDebounce.Stop();
                    _searchDebounce.Start();
                }
            }
        }
        public OwnerDto Selected
        {
            get { return _selected; }
            set { SetProperty(ref _selected, value); OnPropertyChanged(nameof(HasSelection)); }
        }
        public bool HasSelection { get { return _selected != null; } }
        public bool IsDeleteConfirmVisible { get { return _isDeleteConfirmVisible; } private set { SetProperty(ref _isDeleteConfirmVisible, value); } }
        public string DeleteConfirmOwnerName { get { return _deleteConfirmOwnerName; } private set { SetProperty(ref _deleteConfirmOwnerName, value); } }
        public bool IsBatchDialogVisible { get { return _isBatchDialogVisible; } private set { SetProperty(ref _isBatchDialogVisible, value); } }
        /// <summary>批量删除「全选」：勾选/取消勾选批量对话框内全部业主。</summary>
        public bool IsBatchSelectAll
        {
            get { return _isBatchSelectAll; }
            set
            {
                if (SetProperty(ref _isBatchSelectAll, value))
                {
                    foreach (var r in BatchOwners) { r.IsChecked = value; }
                }
            }
        }
        public OwnerPropertyRelationRow ViewRelationRow
        {
            get { return _viewRelationRow; }
            set { SetProperty(ref _viewRelationRow, value); OnPropertyChanged(nameof(IsViewVisible)); }
        }
        public bool IsViewVisible { get { return _viewRelationRow != null; } }

        public string OwnerNoText { get { return _selected == null ? string.Empty : "YZ-" + _selected.Id.ToString("D3"); } }
        public string OwnerName { get { return _selected == null ? string.Empty : _selected.Name; } }
        public string OwnerAvatar { get { return string.IsNullOrEmpty(OwnerName) ? "?" : OwnerName.Substring(0, 1); } }
        public int? SelectedOwnerId
        {
            get { return _selectedOwnerId; }
            set { if (SetProperty(ref _selectedOwnerId, value)) { _ = SelectOwnerById(value); } }
        }
        /// <summary>
        /// 下拉选中行（CHG-v1.3.1-01）：null（集合重建导致的复位）一律忽略，
        /// 真正的"未选"由 SelectedOwnerId 表达，避免下拉刷新把当前业主清空。
        /// </summary>
        public OwnerRow SelectedOwnerItem
        {
            get { return _selectedOwnerItem; }
            set
            {
                if (value == null) { return; }
                if (SetProperty(ref _selectedOwnerItem, value)) { _ = SelectOwner(value); }
            }
        }
        /// <summary>下拉页脚（共 N 条 · 第 X/Y 页）。</summary>
        public string OwnerPageText { get { return "共 " + _ownerTotal + " 条 · 第 " + _ownerPage + "/" + LastOwnerPage + " 页"; } }
        public int LastOwnerPage { get { return _ownerTotal <= 0 ? 1 : ((_ownerTotal + PickerPageSize - 1) / PickerPageSize); } }
        public bool CanOwnerPrev { get { return _ownerPage > 1; } }
        public bool CanOwnerNext { get { return _ownerPage * PickerPageSize < _ownerTotal; } }
        public string OwnerStatusText { get { return _selected == null ? string.Empty : (_selected.Status == OwnerStatus.Living ? "在住" : "搬离"); } }
        public string OwnerPhone { get { return _selected == null ? string.Empty : _selected.Phone; } }
        public string OwnerIdCardTypeText { get { return IdCardTypeName(_selected?.IdCardType); } }
        public string OwnerIdCard { get { return _selected == null ? string.Empty : _selected.IdCard; } }
        public string OwnerCheckInText { get { return _selected?.CheckInDate?.ToString("yyyy-MM-dd") ?? "—"; } }
        public string OwnerAddress { get { return _selected == null ? string.Empty : _selected.ResidentAddress; } }
        public string OwnerEmergency { get { return _selected == null ? string.Empty : ((_selected.EmergencyContactName ?? string.Empty) + " " + (_selected.EmergencyContactPhone ?? string.Empty)).Trim(); } }
        public decimal YearReceivable { get { return _selected?.YearReceivable ?? 0; } }
        public decimal YearPaid { get { return _selected?.YearPaid ?? 0; } }
        public decimal CurrentArrear { get { return _selected?.CurrentArrear ?? 0; } }
        public decimal CollectionRate { get { return YearReceivable == 0 ? 0 : Math.Round(YearPaid / YearReceivable * 100, 1); } }

        public bool IsFormVisible { get { return _isFormVisible; } private set { SetProperty(ref _isFormVisible, value); } }
        public bool IsEditing { get { return _editingId > 0; } }
        public string FormTitle { get { return _editingId > 0 ? "编辑业主" : "新增业主"; } }
        public string FormName { get { return _formName; } set { SetProperty(ref _formName, value); } }
        public OwnerIdCardType FormIdCardType { get { return _formIdCardType; } set { SetProperty(ref _formIdCardType, value); } }
        public string FormIdCard { get { return _formIdCard; } set { SetProperty(ref _formIdCard, value); } }
        public string FormPhone { get { return _formPhone; } set { SetProperty(ref _formPhone, value); } }
        public string FormAddress { get { return _formAddress; } set { SetProperty(ref _formAddress, value); } }
        public string FormEmergencyName { get { return _formEmergencyName; } set { SetProperty(ref _formEmergencyName, value); } }
        public string FormEmergencyPhone { get { return _formEmergencyPhone; } set { SetProperty(ref _formEmergencyPhone, value); } }
        public DateTime? FormCheckIn { get { return _formCheckIn; } set { SetProperty(ref _formCheckIn, value); } }
        public OwnerStatus FormStatus { get { return _formStatus; } set { SetProperty(ref _formStatus, value); } }

        public IAsyncRelayCommand SearchCommand { get; }
        public IAsyncRelayCommand OwnerPrevPageCommand { get; }
        public IAsyncRelayCommand OwnerNextPageCommand { get; }
        public IRelayCommand<OwnerRow> SelectCommand { get; }
        public IRelayCommand EditCommand { get; }
        public IRelayCommand NewCommand { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand DeleteCommand { get; }
        public IRelayCommand BatchDeleteCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteCommand { get; }
        public IRelayCommand CancelDeleteCommand { get; }
        public IAsyncRelayCommand ConfirmBatchDeleteCommand { get; }
        public IRelayCommand CancelBatchDeleteCommand { get; }
        public IAsyncRelayCommand ExportCommand { get; }

        /// <summary>CHG-v1.2.0-13：导出业主本年度缴费概况与缴费明细 PDF。</summary>
        public IAsyncRelayCommand ExportProfilePdfCommand { get; }

        /// <summary>CHG-v1.2.0-17：导出**全部业主**本年度缴费概况与缴费明细 PDF（汇总 + 逐户）。</summary>
        public IAsyncRelayCommand ExportAllProfilesPdfCommand { get; }
        public IRelayCommand<OwnerPropertyRelationRow> ViewRelationCommand { get; }
        public IRelayCommand CloseViewCommand { get; }

        /// <summary>业主搜索：按当前 SearchText 走后端 keyword 检索（空关键字=全量），结果同时驱动下拉选择器。</summary>
        private async Task SearchOwnersAsync()
        {
            string keyword = string.IsNullOrWhiteSpace(_searchText) ? string.Empty : _searchText.Trim();
            await RunAsync(() => SearchOwnersCoreAsync(keyword), string.Empty);
        }

        private async Task SearchOwnersCoreAsync(string keyword)
        {
            await SearchOwnersCoreAsync(keyword, 1);
        }

        /// <summary>
        /// 业主候选：按关键字走**服务端检索**，只渲染当页（CHG-v1.3.1-01）。
        /// v1.3.0 及以前固定取前 100 条 —— 业主多于 100 位时下拉框"只显示一部分"（负责人现场反馈）。
        /// </summary>
        private async Task SearchOwnersCoreAsync(string keyword, int pageIndex)
        {
            int target = pageIndex < 1 ? 1 : pageIndex;
            int seq = ++_ownerSearchSeq;

            var page = await Api.QueryOwnersAsync(new BaseInfoQueryRequest
            {
                PageIndex = target,
                PageSize = PickerPageSize,
                Keyword = keyword
            });
            if (seq != _ownerSearchSeq) { return; }

            int last = OwnerLastPageOf(page.Total);
            if (target > last)
            {
                target = last;
                page = await Api.QueryOwnersAsync(new BaseInfoQueryRequest
                {
                    PageIndex = target,
                    PageSize = PickerPageSize,
                    Keyword = keyword
                });
                if (seq != _ownerSearchSeq) { return; }
            }

            _ownerTotal = page.Total;
            _ownerPage = target;
            Owners.Clear();
            foreach (var dto in page.Items) Owners.Add(new OwnerRow { Dto = dto });
            FilteredOwners.Clear();
            foreach (var dto in page.Items) FilteredOwners.Add(new OwnerRow { Dto = dto });
            RaiseOwnerPageState();
        }

        private string CurrentOwnerKeyword()
        {
            return string.IsNullOrWhiteSpace(_searchText) ? string.Empty : _searchText.Trim();
        }

        private static int OwnerLastPageOf(int total)
        {
            return total <= 0 ? 1 : ((total + PickerPageSize - 1) / PickerPageSize);
        }

        private void RaiseOwnerPageState()
        {
            OnPropertyChanged(nameof(OwnerPageText));
            OnPropertyChanged(nameof(LastOwnerPage));
            OnPropertyChanged(nameof(CanOwnerPrev));
            OnPropertyChanged(nameof(CanOwnerNext));
            OwnerPrevPageCommand.NotifyCanExecuteChanged();
            OwnerNextPageCommand.NotifyCanExecuteChanged();
        }

        public async Task SelectOwnerById(int? id)
        {
            if (!id.HasValue) return;
            var row = Owners.FirstOrDefault(x => x.Id == id.Value) ?? FilteredOwners.FirstOrDefault(x => x.Id == id.Value);
            if (row != null) await SelectOwner(row);
        }

        private static string IdCardTypeName(OwnerIdCardType? t)
        {
            if (!t.HasValue) return string.Empty;
            switch (t.Value)
            {
                case OwnerIdCardType.Passport: return "护照";
                case OwnerIdCardType.Hukou: return "户口簿";
                case OwnerIdCardType.Other: return "其他";
                default: return "身份证";
            }
        }

        private async Task RefreshAsync()
        {
            await RunAsync(async () =>
            {
                await SearchOwnersCoreAsync(string.Empty);
                if (FilteredOwners.Count > 0) await SelectOwner(FilteredOwners[0]);
            }, "业主已加载");
        }

        public async Task LoadAsync()
        {
            await RunAsync(() => SearchOwnersCoreAsync(string.IsNullOrWhiteSpace(_searchText) ? string.Empty : _searchText.Trim()), "业主已加载");
        }

        public async Task SelectOwner(OwnerRow row)
        {
            if (row == null) return;
            // CHG-v1.3.1-01：下拉选中行与业主档案保持同源（回填下拉高亮，不触发二次加载）
            if (!ReferenceEquals(_selectedOwnerItem, row))
            {
                _selectedOwnerItem = row;
                OnPropertyChanged(nameof(SelectedOwnerItem));
            }
            if (_selectedOwnerId != row.Id) { _selectedOwnerId = row.Id; OnPropertyChanged(nameof(SelectedOwnerId)); }
            var owner = await Api.GetOwnerAsync(row.Id);
            Selected = owner;
            OnPropertyChanged(nameof(OwnerNoText));
            OnPropertyChanged(nameof(OwnerName));
            OnPropertyChanged(nameof(OwnerStatusText));
            OnPropertyChanged(nameof(OwnerPhone));
            OnPropertyChanged(nameof(OwnerAvatar));
            OnPropertyChanged(nameof(OwnerIdCardTypeText));
            OnPropertyChanged(nameof(OwnerIdCard));
            OnPropertyChanged(nameof(OwnerCheckInText));
            OnPropertyChanged(nameof(OwnerAddress));
            OnPropertyChanged(nameof(OwnerEmergency));
            OnPropertyChanged(nameof(YearReceivable));
            OnPropertyChanged(nameof(YearPaid));
            OnPropertyChanged(nameof(CurrentArrear));
            OnPropertyChanged(nameof(CollectionRate));
            var rels = await Api.GetOwnerRelationsAsync(row.Id);
            Relations.Clear();
            foreach (var r in rels) Relations.Add(new OwnerPropertyRelationRow { Dto = r });
            var logs = await Api.GetOwnerChangeLogsAsync(row.Id);
            ChangeLogs.Clear();
            foreach (var l in logs) ChangeLogs.Add(new ChangeLogRow { Dto = l });
        }

        private void StartEdit()
        {
            if (_selected == null) return;
            _editingId = _selected.Id;
            OnPropertyChanged(nameof(FormTitle));
            OnPropertyChanged(nameof(IsEditing));
            FormName = _selected.Name;
            FormIdCardType = _selected.IdCardType;
            FormIdCard = _selected.IdCard;
            FormPhone = _selected.Phone;
            FormAddress = _selected.ResidentAddress;
            FormEmergencyName = _selected.EmergencyContactName;
            FormEmergencyPhone = _selected.EmergencyContactPhone;
            FormCheckIn = _selected.CheckInDate;
            FormStatus = _selected.Status;
            IsFormVisible = true;
        }

        private void StartNew()
        {
            _editingId = 0;
            OnPropertyChanged(nameof(FormTitle));
            OnPropertyChanged(nameof(IsEditing));
            FormName = string.Empty;
            FormIdCardType = OwnerIdCardType.IdCard;
            FormIdCard = string.Empty;
            FormPhone = string.Empty;
            FormAddress = string.Empty;
            FormEmergencyName = string.Empty;
            FormEmergencyPhone = string.Empty;
            FormCheckIn = DateTime.Today;
            FormStatus = OwnerStatus.Living;
            IsFormVisible = true;
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(FormName)) { ErrorText = "姓名不能为空"; return; }
            // v1.1.0 F-07：联系电话放开为选填（与导入模板/接口口径统一）；同名业主请填证件号或电话
            var request = new OwnerRequest
            {
                Name = FormName.Trim(),
                IdCardType = FormIdCardType,
                IdCard = (FormIdCard ?? string.Empty).Trim(),
                Phone = (FormPhone ?? string.Empty).Trim(),
                ResidentAddress = FormAddress,
                EmergencyContactName = FormEmergencyName,
                EmergencyContactPhone = FormEmergencyPhone,
                CheckInDate = FormCheckIn,
                Status = FormStatus
            };
            await RunAsync(async () =>
            {
                int ownerId;
                if (_editingId > 0) { await Api.UpdateOwnerAsync(_editingId, request); ownerId = _editingId; }
                else { var created = await Api.CreateOwnerAsync(request); ownerId = created.Id; }
                IsFormVisible = false;
                await LoadAsync();
                await SelectOwnerById(ownerId);
            }, _editingId > 0 ? "业主已更新（变更已留痕）" : "业主已新增");
        }

        private void RequestDelete()
        {
            if (_selected == null) { ErrorText = "请先选择要删除的业主"; return; }
            DeleteConfirmOwnerName = _selected.Name;
            IsDeleteConfirmVisible = true;
        }

        private async Task ConfirmDeleteAsync()
        {
            if (_selected == null) { IsDeleteConfirmVisible = false; return; }
            await RunAsync(async () =>
            {
                int id = _selected.Id;
                await Api.DeleteOwnerAsync(id);
                IsDeleteConfirmVisible = false;
                _selected = null;
                _selectedOwnerId = null;
                OnPropertyChanged(nameof(Selected));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedOwnerId));
                await SearchOwnersCoreAsync(string.IsNullOrWhiteSpace(_searchText) ? string.Empty : _searchText.Trim());
                if (FilteredOwners.Count > 0) await SelectOwner(FilteredOwners[0]);
            }, "业主已删除（软删除）");
        }

        /// <summary>打开批量删除对话框：以当前搜索结果为源，勾选后逐个软删。</summary>
        private void OpenBatchDelete()
        {
            if (FilteredOwners.Count == 0) { ErrorText = "当前没有可删除的业主"; return; }
            BatchOwners.Clear();
            _isBatchSelectAll = false;
            OnPropertyChanged(nameof(IsBatchSelectAll));
            foreach (var row in FilteredOwners)
            {
                row.IsChecked = false;
                BatchOwners.Add(row);
            }
            IsBatchDialogVisible = true;
        }

        private void CloseBatchDelete()
        {
            IsBatchDialogVisible = false;
            foreach (var row in BatchOwners) { row.IsChecked = false; }
            BatchOwners.Clear();
            _isBatchSelectAll = false;
            OnPropertyChanged(nameof(IsBatchSelectAll));
        }

        private async Task ConfirmBatchDeleteAsync()
        {
            var rows = BatchOwners.Where(x => x.IsChecked).ToList();
            if (rows.Count == 0) { ErrorText = "请先勾选要删除的业主"; return; }
            await RunAsync(async () =>
            {
                foreach (var r in rows) { await Api.DeleteOwnerAsync(r.Id); }
                IsBatchDialogVisible = false;
                BatchOwners.Clear();
                _isBatchSelectAll = false;
                OnPropertyChanged(nameof(IsBatchSelectAll));
                await SearchOwnersCoreAsync(string.IsNullOrWhiteSpace(_searchText) ? string.Empty : _searchText.Trim());
                if (FilteredOwners.Count > 0) await SelectOwner(FilteredOwners[0]);
            }, "已批量删除 " + rows.Count + " 户业主（软删除）");
        }

        private async Task ExportAsync()
        {
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                var log = await Api.ExportAsync(new BaseInfoExportRequest
                {
                    ExportType = "owner",
                    Format = ExportFormat.Excel,
                    Filter = new BaseInfoQueryRequest { PageIndex = 1, PageSize = 100000, Keyword = string.IsNullOrWhiteSpace(_searchText) ? string.Empty : _searchText.Trim() }
                });
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "业主档案_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xlsx");
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

        /// <summary>
        /// CHG-v1.2.0-13：导出该业主「本年度缴费概况 + 缴费明细记录」PDF。
        /// 与「导出 Excel（业主档案表格）」两条通道并存，互不影响；留空年度 = 当前年度。
        /// </summary>
        private async Task ExportProfilePdfAsync()
        {
            if (_selected == null || _selected.Id <= 0)
            {
                ErrorText = "请先选择要导出的业主";
                return;
            }
            int ownerId = _selected.Id;
            string ownerName = _selected.Name ?? string.Empty;
            string savedPath = null;
            await RunAsync(async () =>
            {
                ReportLogDto log = await Api.ExportOwnerProfilePdfAsync(ownerId, DateTime.Today.Year);
                if (log == null || log.Id <= 0)
                {
                    throw new InvalidOperationException("导出失败：服务端未生成导出记录");
                }
                var dialog = new SaveFileDialog
                {
                    Title = "导出业主缴费概况与缴费明细（PDF）",
                    Filter = "PDF 文件|*.pdf",
                    FileName = "安怡物业-业主缴费概况-" + ownerName + "-" + DateTime.Today.Year + ".pdf"
                };
                if (dialog.ShowDialog() != true) { return; }
                await Api.DownloadReportFileAsync(log.Id, dialog.FileName);
                savedPath = dialog.FileName;
            }, null);
            if (!string.IsNullOrEmpty(savedPath))
            {
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "业主缴费概况 PDF 已导出：" + savedPath;
            }
        }

        /// <summary>
        /// CHG-v1.2.0-17：导出**全部业主**的本年度缴费概况与缴费明细 PDF（汇总表 + 逐户明细）。
        /// 与单业主导出、Excel 导出三条通道并存。
        /// </summary>
        private async Task ExportAllProfilesPdfAsync()
        {
            string savedPath = null;
            await RunAsync(async () =>
            {
                ReportLogDto log = await Api.ExportAllOwnerProfilesPdfAsync(DateTime.Today.Year);
                if (log == null || log.Id <= 0)
                {
                    throw new InvalidOperationException("导出失败：服务端未生成导出记录");
                }
                var dialog = new SaveFileDialog
                {
                    Title = "导出全部业主缴费概况与缴费明细（PDF）",
                    Filter = "PDF 文件|*.pdf",
                    FileName = "安怡物业-全部业主缴费概况-" + DateTime.Today.Year + ".pdf"
                };
                if (dialog.ShowDialog() != true) { return; }
                await Api.DownloadReportFileAsync(log.Id, dialog.FileName);
                savedPath = dialog.FileName;
            }, null);
            if (!string.IsNullOrEmpty(savedPath))
            {
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "全部业主缴费概况 PDF 已导出：" + savedPath;
            }
        }
    }

    public class OwnerRow : ObservableObject
    {
        private bool _isChecked;
        public OwnerDto Dto { get; set; }
        /// <summary>批量选择标记。</summary>
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }
        public int Id { get { return Dto.Id; } }
        public string Name { get { return Dto.Name; } }
        public string Phone { get { return Dto.Phone; } }
        public string IdCard { get { return Dto.IdCard; } }
        public string StatusText { get { return Dto.Status == OwnerStatus.Living ? "在住" : "搬离"; } }
        public string OwnerDisplayName { get { return BuildDisplayName(Dto); } }

        /// <summary>业主展示名：姓名 · 手机尾4（区分同名业主）。</summary>
        public static string BuildDisplayName(OwnerDto dto)
        {
            if (dto == null) return string.Empty;
            string p4 = string.IsNullOrEmpty(dto.Phone) || dto.Phone.Length < 4 ? string.Empty : dto.Phone.Substring(dto.Phone.Length - 4);
            return (dto.Name ?? string.Empty) + " · " + (string.IsNullOrEmpty(p4) ? "—" : ("手机尾" + p4));
        }
    }

    public class OwnerPropertyRelationRow : ObservableObject
    {
        public OwnerPropertyRelationDto Dto { get; set; }
        public string Room { get { return string.IsNullOrEmpty(Dto.PropertyUnitPath) ? Dto.PropertyRoomNo : Dto.PropertyUnitPath; } }
        public string RelTypeText { get { return RelTypeName(Dto.RelType); } }
        public decimal Share { get { return Dto.Share; } }
        public string ShareText { get { return Dto.RelType == OwnerRelType.RentRecord ? "—" : Dto.Share.ToString("0") + "%"; } }
        public string CheckIn { get { return Dto.EffectiveAt == default ? "—" : Dto.EffectiveAt.ToString("yyyy-MM-dd"); } }
        public string StatusText { get { return Dto.StatusText ?? string.Empty; } }
        private static string RelTypeName(OwnerRelType t)
        {
            switch (t) { case OwnerRelType.Owner: return "业主"; case OwnerRelType.CoOwner: return "共有人"; default: return "租户备案"; }
        }
    }

    public class ChangeLogRow : ObservableObject
    {
        public BaseChangeLogDto Dto { get; set; }
        public string TimeText { get { return Dto.ChangedAt == default ? "—" : Dto.ChangedAt.ToString("yyyy-MM-dd HH:mm"); } }
        public string Field { get { return Dto.FieldName ?? string.Empty; } }
        public string OldValue { get { return Dto.OldValue ?? string.Empty; } }
        public string NewValue { get { return Dto.NewValue ?? string.Empty; } }
        public string Operator { get { return Dto.Operator ?? "系统管理员"; } }
        public string Channel { get { return Dto.Channel ?? string.Empty; } }
    }
}
