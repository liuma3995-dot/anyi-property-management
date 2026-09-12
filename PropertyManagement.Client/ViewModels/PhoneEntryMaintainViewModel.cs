using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.PhoneBook;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>分页页码项（PG-TEL-02 分页条："上一页 · 1 2 · 下一页"）。</summary>
    public class PhonePageItemVm : ObservableObject
    {
        private bool _isCurrent;
        public int Number { get; set; }
        public bool IsCurrent { get { return _isCurrent; } set { SetProperty(ref _isCurrent, value); } }
    }

    /// <summary>电话条目行（PG-TEL-02）。</summary>
    public class PhoneEntryRow : ObservableObject
    {
        private bool _isSelected;
        public PhoneEntryDto Dto { get; set; }
        public string Category { get { return Dto.CategoryName ?? string.Empty; } }
        public string Name { get { return Dto.Name ?? string.Empty; } }
        public string Phone { get { return Dto.Phone ?? string.Empty; } }
        /// <summary>批量停用勾选态。</summary>
        public bool IsSelected { get { return _isSelected; } set { SetProperty(ref _isSelected, value); } }

        /// <summary>来源（0 手工 / 1 员工通讯录；SourceText 为空时按 Source/EntryType 兜底）。</summary>
        public string Source
        {
            get
            {
                if (!string.IsNullOrEmpty(Dto.SourceText)) return Dto.SourceText;
                return (Dto.Source == 1 || Dto.EntryType == PhoneEntryType.Employee) ? "员工通讯录" : "手工";
            }
        }

        public bool IsTop { get { return Dto.IsTop; } }
        public string TopMark { get { return Dto.IsTop ? "★" : "—"; } }

        /// <summary>三态状态（启用/停用/自动停用；StatusText 为空时按 Status+DisableSource 兜底）。</summary>
        public string StatusText
        {
            get
            {
                if (!string.IsNullOrEmpty(Dto.StatusText)) return Dto.StatusText;
                if (Dto.Status == PhoneEntryStatus.Enabled) return "启用";
                return Dto.DisableSource == 1 ? "自动停用" : "停用";
            }
        }

        public string UpdatedAt { get { return Dto.UpdatedAt.HasValue ? Dto.UpdatedAt.Value.ToString("yyyy-MM-dd") : string.Empty; } }

        /// <summary>紧急条目判定（与后端 PhoneBookService.IsEmergencyEntry 同口径，BR-TEL-03）。</summary>
        public bool IsEmergency
        {
            get
            {
                if (Dto.EntryType == PhoneEntryType.Emergency) return true;
                string digits = (Dto.Phone ?? string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).Trim();
                return digits == "119" || digits == "120" || digits == "110";
            }
        }

        /// <summary>启用行才可编辑（停用/自动停用行隐藏"编辑"，避免误把停用条目重新启用展示）。</summary>
        public bool CanEdit { get { return Dto.Status == PhoneEntryStatus.Enabled; } }

        /// <summary>紧急条目不可停用（BR-TEL-03 后端 409），隐藏"停用"防报错。</summary>
        public bool CanDeactivate { get { return Dto.Status == PhoneEntryStatus.Enabled && !IsEmergency; } }
        /// <summary>批量停用勾选：启用行均可勾选（含紧急条目，由操作员决定是否停用）。</summary>
        public bool CanSelect { get { return Dto.Status == PhoneEntryStatus.Enabled; } }

        /// <summary>停用/自动停用行显示"恢复"。</summary>
        public bool CanActivate { get { return Dto.Status != PhoneEntryStatus.Enabled; } }
    }

    /// <summary>电话条目维护页（PG-TEL-02，UC-TEL-002/005/006，BR-TEL-01/03/04）。</summary>
    public class PhoneEntryMaintainViewModel : BaseInfoPageViewModel
    {
        private const int PageSize = 30;
        private string _keyword = string.Empty;
        private int _categoryFilter;
        private int _sourceFilter;
        private int _statusFilter = 1;   // 原型默认"状态：启用"
        private int _pageIndex = 1;
        private int _total;
        private bool _categoriesLoaded;
        private bool _isFormVisible;
        private bool _isBatchMode;
        private bool _isCategoryAdd;
        private bool _isCategoryDeleteVisible;
        private bool _isTypeAdd;
        private bool _isTypeDeleteVisible;
        private string _newCategoryName = string.Empty;
        private string _deleteCategoryName = string.Empty;
        private int _deleteCategoryId;
        private string _newTypeName = string.Empty;
        private string _deleteTypeName = string.Empty;
        private int _deleteTypeId;
        private string _formTitle = "新增电话条目";
        private bool _isEdit;
        private int? _editId;
        private int _formCategoryId;
        private string _formCategoryName = string.Empty;
        private int _formEntryType;
        private int _formTypeId;
        private string _formTypeText = string.Empty;
        private string _formName = string.Empty;
        private string _formPhone = string.Empty;
        private string _formNote = string.Empty;
        private bool _formIsTop;

        public PhoneEntryMaintainViewModel(IApiClient api) : base(api)
        {
            QueryCommand = new AsyncRelayCommand(() => LoadAsync());
            NewCommand = new RelayCommand(StartNew);
            EditCommand = new RelayCommand<PhoneEntryRow>(r => StartEdit(r));
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            CancelCommand = new RelayCommand(() => IsFormVisible = false);
            DeactivateCommand = new AsyncRelayCommand<PhoneEntryRow>(r => SetStatusAsync(r, PhoneEntryStatus.Disabled));
            ActivateCommand = new AsyncRelayCommand<PhoneEntryRow>(r => SetStatusAsync(r, PhoneEntryStatus.Enabled));
            SyncCommand = new AsyncRelayCommand(SyncAsync);
            EnterBatchModeCommand = new RelayCommand(EnterBatchMode);
            BatchDisableCommand = new AsyncRelayCommand(BatchDisableAsync);
            CancelBatchCommand = new RelayCommand(CancelBatch);
            OpenNewCategoryCommand = new RelayCommand(OpenNewCategory);
            ConfirmNewCategoryCommand = new AsyncRelayCommand(ConfirmNewCategoryAsync);
            CancelNewCategoryCommand = new RelayCommand(() => { IsCategoryAdd = false; NewCategoryName = string.Empty; ErrorText = string.Empty; });
            RequestDeleteCategoryCommand = new RelayCommand(RequestDeleteCategory);
            ConfirmDeleteCategoryCommand = new AsyncRelayCommand(ConfirmDeleteCategoryAsync);
            CancelDeleteCategoryCommand = new RelayCommand(() => IsCategoryDeleteVisible = false);
            OpenNewTypeCommand = new RelayCommand(OpenNewType);
            ConfirmNewTypeCommand = new AsyncRelayCommand(ConfirmNewTypeAsync);
            CancelNewTypeCommand = new RelayCommand(() => { IsTypeAdd = false; NewTypeName = string.Empty; ErrorText = string.Empty; });
            RequestDeleteTypeCommand = new RelayCommand(RequestDeleteType);
            ConfirmDeleteTypeCommand = new AsyncRelayCommand(ConfirmDeleteTypeAsync);
            CancelDeleteTypeCommand = new RelayCommand(() => IsTypeDeleteVisible = false);
            PrevPageCommand = new RelayCommand(PrevPage);
            NextPageCommand = new RelayCommand(NextPage);
            GotoPageCommand = new RelayCommand<object>(GotoPage);
            _ = LoadAsync();
        }

        public ObservableCollection<PhoneEntryRow> Items { get; } = new ObservableCollection<PhoneEntryRow>();
        public ObservableCollection<PhoneCategoryDto> Categories { get; } = new ObservableCollection<PhoneCategoryDto>();
        /// <summary>类型下拉选项（t_phone_type，支持自定义新增/删除）。</summary>
        public ObservableCollection<PhoneTypeDto> TypeOptions { get; } = new ObservableCollection<PhoneTypeDto>();
        /// <summary>筛选下拉：全部 + 五分类（「全部」=0，不传 CategoryId）。</summary>
        public ObservableCollection<PhoneCategoryDto> FilterCategories { get; } = new ObservableCollection<PhoneCategoryDto>();
        public ObservableCollection<PhonePageItemVm> PageNumbers { get; } = new ObservableCollection<PhonePageItemVm>();

        public string Keyword { get { return _keyword; } set { if (SetProperty(ref _keyword, value)) { PageIndex = 1; _ = LoadAsync(); } } }
        public int CategoryFilter { get { return _categoryFilter; } set { if (SetProperty(ref _categoryFilter, value)) { PageIndex = 1; _ = LoadAsync(); } } }
        /// <summary>来源筛选：0 全部 / 1 手工 / 2 员工通讯录（映射 QueryRequest.Source 0/1）。</summary>
        public int SourceFilter { get { return _sourceFilter; } set { if (SetProperty(ref _sourceFilter, value)) { PageIndex = 1; _ = LoadAsync(); } } }
        public int StatusFilter { get { return _statusFilter; } set { if (SetProperty(ref _statusFilter, value)) { PageIndex = 1; _ = LoadAsync(); } } }
        public int PageIndex { get { return _pageIndex; } private set { if (SetProperty(ref _pageIndex, value)) OnPropertyChanged(nameof(TotalText)); } }
        public int Total { get { return _total; } private set { if (SetProperty(ref _total, value)) OnPropertyChanged(nameof(TotalText)); } }
        private int PageCount { get { return _total <= 0 ? 1 : (_total + PageSize - 1) / PageSize; } }
        public string TotalText { get { return "共 " + _total + " 条记录 · 第 " + _pageIndex + "/" + PageCount + " 页"; } }

        public bool IsFormVisible { get { return _isFormVisible; } set { SetProperty(ref _isFormVisible, value); } }
        /// <summary>批量停用模式：激活后才显示勾选框列与确认/取消按钮。</summary>
        public bool IsBatchMode
        {
            get { return _isBatchMode; }
            set { if (SetProperty(ref _isBatchMode, value)) { OnPropertyChanged(nameof(ShowBatchStart)); } }
        }
        /// <summary>新增自定义分类：显示内联输入行。</summary>
        public bool IsCategoryAdd { get { return _isCategoryAdd; } set { SetProperty(ref _isCategoryAdd, value); } }
        public string NewCategoryName { get { return _newCategoryName; } set { SetProperty(ref _newCategoryName, value); } }
        /// <summary>删除分类二次确认浮层。</summary>
        public bool IsCategoryDeleteVisible { get { return _isCategoryDeleteVisible; } set { SetProperty(ref _isCategoryDeleteVisible, value); } }
        public string DeleteCategoryName { get { return _deleteCategoryName; } set { SetProperty(ref _deleteCategoryName, value); } }
        /// <summary>新增自定义类型：显示内联输入行。</summary>
        public bool IsTypeAdd { get { return _isTypeAdd; } set { SetProperty(ref _isTypeAdd, value); } }
        public string NewTypeName { get { return _newTypeName; } set { SetProperty(ref _newTypeName, value); } }
        /// <summary>删除类型二次确认浮层。</summary>
        public bool IsTypeDeleteVisible { get { return _isTypeDeleteVisible; } set { SetProperty(ref _isTypeDeleteVisible, value); } }
        public string DeleteTypeName { get { return _deleteTypeName; } set { SetProperty(ref _deleteTypeName, value); } }
        /// <summary>工具栏「批量停用」按钮是否显示（非批量模式时显示）。</summary>
        public bool ShowBatchStart { get { return !_isBatchMode; } }
        /// <summary>是否有行被勾选（控制「确认停用」可用态）。</summary>
        public bool HasChecked { get { return Items.Any(x => x.IsSelected); } }
        /// <summary>全选/取消全选（仅作用于可停用行）。</summary>
        public bool IsAllChecked
        {
            get { var sel = Items.Where(x => x.CanSelect).ToList(); return sel.Count > 0 && sel.All(x => x.IsSelected); }
            set
            {
                foreach (var row in Items) row.IsSelected = value && row.CanSelect;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasChecked));
                OnPropertyChanged(nameof(CheckedCountText));
            }
        }
        /// <summary>勾选计数文本。</summary>
        public string CheckedCountText { get { return "已选 " + Items.Count(x => x.IsSelected) + " / " + Items.Count(x => x.CanSelect) + " 项"; } }
        public string FormTitle { get { return _formTitle; } private set { SetProperty(ref _formTitle, value); } }
        public int FormCategoryId { get { return _formCategoryId; } set { SetProperty(ref _formCategoryId, value); } }
        /// <summary>分类下拉框文本（可自定义输入；保存时未匹配则自动新建分类）。</summary>
        public string FormCategoryName { get { return _formCategoryName; } set { SetProperty(ref _formCategoryName, value); } }
        /// <summary>0 紧急 / 1 普通；编辑员工条目（2）时表单类型区显示"员工通讯录（同步生成）"只读文本。</summary>
        public int FormEntryType
        {
            get { return _formEntryType; }
            set { if (SetProperty(ref _formEntryType, value)) { OnPropertyChanged(nameof(ShowTypeButtons)); OnPropertyChanged(nameof(ShowTypeSelector)); OnPropertyChanged(nameof(ShowEmployeeTypeHint)); } }
        }
        /// <summary>类型下拉框选中 id（与分类下拉框保持一致）。</summary>
        public int FormTypeId { get { return _formTypeId; } set { SetProperty(ref _formTypeId, value); } }
        /// <summary>类型新增/删除按钮是否显示（编辑员工条目时隐藏）。</summary>
        public bool ShowTypeButtons { get { return _formEntryType != (int)PhoneEntryType.Employee; } }
        /// <summary>类型下拉框是否显示（编辑员工条目时隐藏，改由只读文本替代）。</summary>
        public bool ShowTypeSelector { get { return _formEntryType != (int)PhoneEntryType.Employee; } }
        /// <summary>编辑员工条目时显示"员工通讯录（同步生成）"只读文本。</summary>
        public bool ShowEmployeeTypeHint { get { return _formEntryType == (int)PhoneEntryType.Employee; } }
        /// <summary>类型下拉框文本（可自定义输入；保存时须映射为 紧急/普通）。</summary>
        public string FormTypeText { get { return _formTypeText; } set { SetProperty(ref _formTypeText, value); } }
        public string FormName { get { return _formName; } set { SetProperty(ref _formName, value); } }
        public string FormPhone { get { return _formPhone; } set { SetProperty(ref _formPhone, value); } }
        public string FormNote { get { return _formNote; } set { SetProperty(ref _formNote, value); } }
        public bool FormIsTop { get { return _formIsTop; } set { SetProperty(ref _formIsTop, value); } }

        public IAsyncRelayCommand QueryCommand { get; }
        public IRelayCommand NewCommand { get; }
        public IRelayCommand<PhoneEntryRow> EditCommand { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IAsyncRelayCommand<PhoneEntryRow> DeactivateCommand { get; }
        public IAsyncRelayCommand<PhoneEntryRow> ActivateCommand { get; }
        public IAsyncRelayCommand SyncCommand { get; }
        public IRelayCommand EnterBatchModeCommand { get; }
        public IAsyncRelayCommand BatchDisableCommand { get; }
        public IRelayCommand CancelBatchCommand { get; }
        public IRelayCommand OpenNewCategoryCommand { get; }
        public IAsyncRelayCommand ConfirmNewCategoryCommand { get; }
        public IRelayCommand CancelNewCategoryCommand { get; }
        public IRelayCommand RequestDeleteCategoryCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteCategoryCommand { get; }
        public IRelayCommand CancelDeleteCategoryCommand { get; }
        public IRelayCommand OpenNewTypeCommand { get; }
        public IAsyncRelayCommand ConfirmNewTypeCommand { get; }
        public IRelayCommand CancelNewTypeCommand { get; }
        public IRelayCommand RequestDeleteTypeCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteTypeCommand { get; }
        public IRelayCommand CancelDeleteTypeCommand { get; }
        public IRelayCommand PrevPageCommand { get; }
        public IRelayCommand NextPageCommand { get; }
        public IRelayCommand<object> GotoPageCommand { get; }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var query = new PhoneEntryQueryRequest { PageIndex = PageIndex, PageSize = PageSize, Keyword = Keyword };
                if (_categoryFilter != 0) query.CategoryId = _categoryFilter;
                if (_sourceFilter != 0) query.Source = _sourceFilter - 1;   // 1→0 手工，2→1 员工通讯录
                if (_statusFilter != 0) query.Status = _statusFilter == 1 ? PhoneEntryStatus.Enabled : PhoneEntryStatus.Disabled;
                var page = await Api.QueryPhoneEntriesAsync(query);
                Items.Clear();
                foreach (var dto in page.Items) Items.Add(WrapRow(dto));
                Total = page.Total;
                RebuildPageNumbers();
                if (!_categoriesLoaded)
                {
                    FilterCategories.Add(new PhoneCategoryDto { Id = 0, Name = "全部" });
                    foreach (var c in await Api.GetPhoneCategoriesAsync())
                    {
                        Categories.Add(c);
                        FilterCategories.Add(c);
                    }
                    foreach (var t in await Api.GetPhoneTypesAsync()) TypeOptions.Add(t);
                    _categoriesLoaded = true;
                }
            }, "条目已加载");
        }

        /// <summary>包装行并订阅 IsSelected 变化，用于刷新全选/已选计数。</summary>
        private PhoneEntryRow WrapRow(PhoneEntryDto dto)
        {
            var row = new PhoneEntryRow { Dto = dto };
            row.PropertyChanged += Row_PropertyChanged;
            return row;
        }

        private void Row_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PhoneEntryRow.IsSelected))
            {
                OnPropertyChanged(nameof(HasChecked));
                OnPropertyChanged(nameof(IsAllChecked));
                OnPropertyChanged(nameof(CheckedCountText));
            }
        }

        private void RebuildPageNumbers()
        {
            if (PageNumbers.Count != PageCount)
            {
                PageNumbers.Clear();
                for (int i = 1; i <= PageCount; i++) PageNumbers.Add(new PhonePageItemVm { Number = i });
            }
            foreach (var p in PageNumbers) p.IsCurrent = p.Number == PageIndex;
        }

        private void PrevPage() { if (PageIndex > 1) { PageIndex--; _ = LoadAsync(); } }
        private void NextPage() { if (PageIndex < PageCount) { PageIndex++; _ = LoadAsync(); } }

        private void GotoPage(object parameter)
        {
            int page;
            if (parameter is int) page = (int)parameter;
            else if (!int.TryParse(Convert.ToString(parameter), out page)) return;
            if (page >= 1 && page <= PageCount && page != PageIndex) { PageIndex = page; _ = LoadAsync(); }
        }

        private void StartNew()
        {
            FormTitle = "新增电话条目";
            _isEdit = false; _editId = null;
            FormCategoryId = Categories.Count > 0 ? Categories[0].Id : 0;
            FormCategoryName = Categories.Count > 0 ? (Categories[0].Name ?? string.Empty) : string.Empty;
            FormEntryType = 1; FormTypeText = "普通";
            FormTypeId = TypeOptions.FirstOrDefault(x => x.Name == "普通")?.Id ?? 0;
            FormName = string.Empty; FormPhone = string.Empty; FormNote = string.Empty; FormIsTop = false;
            ErrorText = string.Empty;
            IsFormVisible = true;
        }

        private void StartEdit(PhoneEntryRow row)
        {
            if (row == null || !row.CanEdit) return;
            FormTitle = "编辑电话条目";
            _isEdit = true; _editId = row.Dto.Id;
            FormCategoryId = row.Dto.CategoryId;
            FormCategoryName = string.IsNullOrEmpty(row.Dto.CategoryName) ? (Categories.FirstOrDefault(c => c.Id == row.Dto.CategoryId)?.Name ?? string.Empty) : row.Dto.CategoryName;
            FormEntryType = (int)row.Dto.EntryType;
            FormTypeText = string.IsNullOrEmpty(row.Dto.TypeName)
                ? (FormEntryType == (int)PhoneEntryType.Emergency ? "紧急" : (FormEntryType == (int)PhoneEntryType.Normal ? "普通" : "员工通讯录"))
                : row.Dto.TypeName;
            FormTypeId = row.Dto.TypeId ?? (TypeOptions.FirstOrDefault(t => t.Name == FormTypeText)?.Id ?? 0);
            FormName = row.Dto.Name; FormPhone = row.Dto.Phone; FormNote = row.Dto.Note ?? string.Empty; FormIsTop = row.Dto.IsTop;
            ErrorText = string.Empty;
            IsFormVisible = true;
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(FormName)) { ErrorText = "名称不能为空"; return; }
            if (string.IsNullOrWhiteSpace(FormPhone)) { ErrorText = "号码不能为空"; return; }
            if (string.IsNullOrWhiteSpace(FormCategoryName)) { ErrorText = "请选择或输入分类"; return; }
            if (!_isEdit && FormEntryType == (int)PhoneEntryType.Employee)
            {
                // BR-TEL-01：员工条目只来自同步，新增表单不提供"员工通讯录"类型
                ErrorText = "员工条目只能通过\"同步员工通讯录\"生成（BR-TEL-01）";
                return;
            }
            string typeName = (FormTypeText ?? string.Empty).Trim();
            if (typeName.Length == 0) { ErrorText = "请选择或输入类型"; return; }
            string catName = FormCategoryName.Trim();
            await RunAsync(async () =>
            {
                int categoryId = await ResolveCategoryAsync(catName);
                int? typeId; PhoneEntryType entryType;
                bool employeeEdit = _isEdit && FormEntryType == (int)PhoneEntryType.Employee;
                if (employeeEdit) { entryType = PhoneEntryType.Employee; typeId = TypeOptions.FirstOrDefault(x => x.Name == "员工通讯录")?.Id ?? null; }
                else { typeId = await ResolveTypeAsync(typeName); entryType = MapTypeName(typeName); }
                var request = new PhoneEntryRequest
                {
                    CategoryId = categoryId, TypeId = typeId, EntryType = entryType,
                    Name = FormName.Trim(), Phone = FormPhone.Trim(), Note = FormNote ?? string.Empty,
                    IsTop = FormIsTop
                };
                if (_isEdit && _editId.HasValue) await Api.UpdatePhoneEntryAsync(_editId.Value, request);
                else await Api.CreatePhoneEntryAsync(request);
                IsFormVisible = false;
                await LoadAsync();
            }, "条目已保存");
        }

        /// <summary>解析自定义类型：命中已有类型返回其 id，否则新建类型。</summary>
        private async Task<int?> ResolveTypeAsync(string typeName)
        {
            var existing = TypeOptions.FirstOrDefault(x => string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing.Id;
            var created = await Api.CreatePhoneTypeAsync(new PhoneTypeRequest { Name = typeName });
            TypeOptions.Add(created);
            return created.Id;
        }

        /// <summary>类型名 → PhoneEntryType 枚举（自定义类型归为普通）。</summary>
        private static PhoneEntryType MapTypeName(string name)
        {
            if (name == "紧急") return PhoneEntryType.Emergency;
            if (name == "员工通讯录") return PhoneEntryType.Employee;
            return PhoneEntryType.Normal;
        }

        /// <summary>解析自定义分类：命中已有分类返回其 id，否则新建分类（UC-TEL-001）。</summary>
        private async Task<int> ResolveCategoryAsync(string catName)
        {
            var existing = Categories.FirstOrDefault(c => string.Equals(c.Name, catName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing.Id;
            var created = await Api.CreatePhoneCategoryAsync(new PhoneCategoryRequest { Name = catName });
            Categories.Add(created);
            return created.Id;
        }

        /// <summary>进入批量停用模式：显示勾选框列，清空已有勾选。</summary>
        private void EnterBatchMode()
        {
            foreach (var row in Items) row.IsSelected = false;
            ErrorText = string.Empty;
            IsBatchMode = true;
            OnPropertyChanged(nameof(HasChecked));
            OnPropertyChanged(nameof(IsAllChecked));
            OnPropertyChanged(nameof(CheckedCountText));
        }

        /// <summary>退出批量停用模式并清空勾选。</summary>
        private void CancelBatch()
        {
            foreach (var row in Items) row.IsSelected = false;
            ErrorText = string.Empty;
            IsBatchMode = false;
            OnPropertyChanged(nameof(HasChecked));
            OnPropertyChanged(nameof(IsAllChecked));
            OnPropertyChanged(nameof(CheckedCountText));
        }

        /// <summary>批量停用（=删除口径）：勾选行置为停用，紧急号码自动跳过（BR-TEL-03），完成后退出批量模式。</summary>
        private async Task BatchDisableAsync()
        {
            var ids = Items.Where(x => x.IsSelected && x.Dto.Status == PhoneEntryStatus.Enabled).Select(x => x.Dto.Id).ToList();
            if (ids.Count == 0) { ErrorText = "请勾选需要停用的条目"; return; }
            PhoneEntryBatchStatusResultDto result = null;
            await RunAsync(async () =>
            {
                result = await Api.BatchDisablePhoneEntriesAsync(new PhoneEntryBatchStatusRequest { Ids = ids });
                foreach (var row in Items) row.IsSelected = false;
                await LoadAsync();
            }, "批量停用完成");
            if (result != null)
            {
                IsBatchMode = false;
                foreach (var row in Items) row.IsSelected = false;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已停用 " + result.Disabled + " 条"
                    + (result.Skipped > 0 ? "，跳过紧急号码 " + result.Skipped + " 条" : string.Empty);
                OnPropertyChanged(nameof(HasChecked));
                OnPropertyChanged(nameof(IsAllChecked));
                OnPropertyChanged(nameof(CheckedCountText));
            }
        }

        private async Task SetStatusAsync(PhoneEntryRow row, PhoneEntryStatus status)
        {
            if (row == null) return;
            if (status == PhoneEntryStatus.Disabled && row.IsEmergency) return;   // BR-TEL-03 双保险防 409
            await RunAsync(async () =>
            {
                await Api.SetPhoneEntryStatusAsync(row.Dto.Id, status);
                await LoadAsync();
            }, status == PhoneEntryStatus.Disabled ? "条目已停用" : "条目已启用");
        }

        /// <summary>打开新增分类内联行。</summary>
        private void OpenNewCategory()
        {
            NewCategoryName = string.Empty;
            ErrorText = string.Empty;
            IsCategoryAdd = true;
        }

        /// <summary>保存自定义分类：新建后选中该分类。</summary>
        private async Task ConfirmNewCategoryAsync()
        {
            string name = NewCategoryName.Trim();
            if (name.Length == 0) { ErrorText = "请输入分类名称"; return; }
            if (Categories.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) { ErrorText = "分类已存在：" + name; return; }
            await RunAsync(async () =>
            {
                var created = await Api.CreatePhoneCategoryAsync(new PhoneCategoryRequest { Name = name });
                Categories.Add(created);
                FilterCategories.Add(created);
                IsCategoryAdd = false;
                NewCategoryName = string.Empty;
                FormCategoryId = created.Id;
                FormCategoryName = created.Name;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "分类「" + created.Name + "」已新增";
            }, "分类已新增");
        }

        /// <summary>请求删除当前分类：打开二次确认浮层。</summary>
        private void RequestDeleteCategory()
        {
            var cat = Categories.FirstOrDefault(c => c.Id == FormCategoryId);
            if (cat == null) { ErrorText = "请先选择需要删除的分类"; return; }
            _deleteCategoryId = cat.Id;
            DeleteCategoryName = cat.Name;
            IsCategoryDeleteVisible = true;
        }

        /// <summary>确认删除分类（软删），并重选第一个分类。</summary>
        private async Task ConfirmDeleteCategoryAsync()
        {
            int id = _deleteCategoryId;
            string name = DeleteCategoryName;
            await RunAsync(async () =>
            {
                await Api.DeletePhoneCategoryAsync(id);
                var removed = Categories.FirstOrDefault(c => c.Id == id);
                if (removed != null) Categories.Remove(removed);
                var rmFilter = FilterCategories.FirstOrDefault(c => c.Id == id);
                if (rmFilter != null) FilterCategories.Remove(rmFilter);
                IsCategoryDeleteVisible = false;
                if (Categories.Count > 0)
                {
                    FormCategoryId = Categories[0].Id;
                    FormCategoryName = Categories[0].Name ?? string.Empty;
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + "分类「" + name + "」已删除";
                }
                else
                {
                    FormCategoryId = 0;
                    FormCategoryName = string.Empty;
                }
            }, "分类已删除");
        }

        /// <summary>打开新增类型内联行。</summary>
        private void OpenNewType()
        {
            NewTypeName = string.Empty;
            ErrorText = string.Empty;
            IsTypeAdd = true;
        }

        /// <summary>保存自定义类型：新建后选中该类型。</summary>
        private async Task ConfirmNewTypeAsync()
        {
            string name = NewTypeName.Trim();
            if (name.Length == 0) { ErrorText = "请输入类型名称"; return; }
            if (TypeOptions.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) { ErrorText = "类型已存在：" + name; return; }
            await RunAsync(async () =>
            {
                var created = await Api.CreatePhoneTypeAsync(new PhoneTypeRequest { Name = name });
                TypeOptions.Add(created);
                IsTypeAdd = false;
                NewTypeName = string.Empty;
                FormTypeText = created.Name;
                FormTypeId = created.Id;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "类型「" + created.Name + "」已新增";
            }, "类型已新增");
        }

        /// <summary>请求删除当前类型：打开二次确认浮层。</summary>
        private void RequestDeleteType()
        {
            var type = TypeOptions.FirstOrDefault(x => x.Name == (FormTypeText ?? string.Empty).Trim());
            if (type == null) { ErrorText = "请先选择需要删除的类型"; return; }
            _deleteTypeId = type.Id;
            DeleteTypeName = type.Name;
            IsTypeDeleteVisible = true;
        }

        /// <summary>确认删除类型（软删），并重选第一个类型。</summary>
        private async Task ConfirmDeleteTypeAsync()
        {
            int id = _deleteTypeId;
            string name = DeleteTypeName;
            await RunAsync(async () =>
            {
                await Api.DeletePhoneTypeAsync(id);
                var removed = TypeOptions.FirstOrDefault(x => x.Id == id);
                if (removed != null) TypeOptions.Remove(removed);
                IsTypeDeleteVisible = false;
                if (TypeOptions.Count > 0) { FormTypeText = TypeOptions[0].Name; FormTypeId = TypeOptions[0].Id; }
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "类型「" + name + "」已删除";
            }, "类型已删除");
        }

        /// <summary>同步员工通讯录：categoryId 传 0（后端显式校验口径：&lt;=0 自动落到"物业服务中心"或首个分类）。</summary>
        private async Task SyncAsync()
        {
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                var result = await Api.SyncEmployeePhoneEntriesAsync(0);
                await LoadAsync();
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "员工通讯录已同步：新建 " + result.Synced
                    + " · 更新 " + result.Updated + " · 停用 " + result.Disabled + " · 跳过 " + result.Skipped;
            }
            catch (ApiClientException ex) { ErrorText = ex.Message; }
            catch (Exception ex) { ErrorText = "操作失败：" + ex.Message; }
            finally { IsBusy = false; }
        }
    }
}
