using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.PhoneBook;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>分类 tab 行（PG-TEL-01 分类筛选："全部"+五分类，选中态高亮）。</summary>
    public class CategoryTabVm : ObservableObject
    {
        private bool _isSelected;
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsSelected { get { return _isSelected; } set { SetProperty(ref _isSelected, value); } }
    }

    /// <summary>电话查询行（PG-TEL-01）。</summary>
    public class PhoneQueryRow : ObservableObject
    {
        public PhoneEntryDto Dto { get; set; }
        public string Name { get { return Dto.Name ?? string.Empty; } }
        public string Phone { get { return Dto.Phone ?? string.Empty; } }
        public string Category { get { return Dto.CategoryName ?? string.Empty; } }
        public string Note { get { return Dto.Note ?? string.Empty; } }
        public bool IsTop { get { return Dto.IsTop; } }
        public string TopMark { get { return Dto.IsTop ? "★" : string.Empty; } }
        /// <summary>详情卡备注标签是否显示（备注为空时隐藏"24 小时"样式标签）。</summary>
        public bool HasNote { get { return !string.IsNullOrEmpty(Dto.Note); } }

        /// <summary>
        /// 紧急条目判定（与后端 PhoneBookService.IsEmergencyEntry 同口径，BR-TEL-03）：
        /// EntryType=紧急，或号码去"-"与空格后为 119/120/110。
        /// </summary>
        public bool IsEmergency
        {
            get
            {
                if (Dto.EntryType == PhoneEntryType.Emergency) return true;
                string digits = (Dto.Phone ?? string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).Trim();
                return digits == "119" || digits == "120" || digits == "110";
            }
        }

        /// <summary>置顶按钮可用性：紧急条目禁用（防后端 409"紧急电话默认置顶，不可取消"）。</summary>
        public bool CanTop { get { return !IsEmergency; } }
    }

    /// <summary>电话查询页（PG-TEL-01，UC-TEL-003/004，BR-TEL-02）。左列查询结果 + 右列详情卡（大号码/复制/置顶）。</summary>
    public class PhoneQueryViewModel : BaseInfoPageViewModel
    {
        private const int PageSize = 100;
        private string _keyword = string.Empty;
        private int _selectedCategoryId;
        private PhoneQueryRow _selectedRow;
        private int _total;
        private bool _categoriesLoaded;

        public PhoneQueryViewModel(IApiClient api) : base(api)
        {
            SelectCategoryCommand = new RelayCommand<object>(SelectCategory);
            CopyCommand = new RelayCommand<PhoneQueryRow>(Copy);
            TopCommand = new AsyncRelayCommand<PhoneQueryRow>(SetTopAsync);
            CategoryTabs.Add(new CategoryTabVm { Id = 0, Name = "全部", IsSelected = true });
            _ = LoadAsync();
        }

        public ObservableCollection<PhoneQueryRow> Items { get; } = new ObservableCollection<PhoneQueryRow>();
        public ObservableCollection<PhoneCategoryDto> Categories { get; } = new ObservableCollection<PhoneCategoryDto>();
        public ObservableCollection<CategoryTabVm> CategoryTabs { get; } = new ObservableCollection<CategoryTabVm>();

        /// <summary>输入即自动检索（参考电话条目维护模块，逐键触发查询并重置页码）。</summary>
        public string Keyword { get { return _keyword; } set { if (SetProperty(ref _keyword, value)) { _ = LoadAsync(); } } }

        public int SelectedCategoryId { get { return _selectedCategoryId; } }

        public PhoneQueryRow SelectedRow
        {
            get { return _selectedRow; }
            set
            {
                if (SetProperty(ref _selectedRow, value))
                {
                    OnPropertyChanged(nameof(HasSelection));
                }
            }
        }

        public bool HasSelection { get { return _selectedRow != null; } }

        public int Total { get { return _total; } private set { if (SetProperty(ref _total, value)) OnPropertyChanged(nameof(TotalText)); } }
        public string TotalText { get { return "共 " + _total + " 条"; } }

        public IRelayCommand<object> SelectCategoryCommand { get; }
        public IRelayCommand<PhoneQueryRow> CopyCommand { get; }
        public IAsyncRelayCommand<PhoneQueryRow> TopCommand { get; }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var query = new PhoneEntryQueryRequest
                {
                    PageIndex = 1, PageSize = PageSize,
                    Keyword = Keyword, Status = PhoneEntryStatus.Enabled   // BR-TEL-02：查询页不泄漏停用条目
                };
                if (_selectedCategoryId != 0) query.CategoryId = _selectedCategoryId;
                var page = await Api.QueryPhoneEntriesAsync(query);
                Items.Clear();
                foreach (var dto in page.Items) Items.Add(new PhoneQueryRow { Dto = dto });
                Total = page.Total;
                if (!_categoriesLoaded)
                {
                    Categories.Clear();
                    foreach (var c in await Api.GetPhoneCategoriesAsync()) Categories.Add(c);
                    RebuildCategoryTabs();
                    _categoriesLoaded = true;
                }
                // 刷新后按 Id 恢复选中行，右列详情跟随最新数据（置顶后 ★ 与按钮态即时更新）
                if (_selectedRow != null)
                {
                    var fresh = Items.FirstOrDefault(x => x.Dto.Id == _selectedRow.Dto.Id);
                    SelectedRow = fresh;
                }
            }, "电话已加载");
        }

        /// <summary>分类 tab 点击：全部=0，其余=分类 Id；选中态高亮后重查。</summary>
        private void SelectCategory(object parameter)
        {
            int id;
            if (parameter is int) id = (int)parameter;
            else if (!int.TryParse(Convert.ToString(parameter), out id)) return;
            if (id == _selectedCategoryId) return;
            _selectedCategoryId = id;
            OnPropertyChanged(nameof(SelectedCategoryId));
            foreach (var tab in CategoryTabs) tab.IsSelected = tab.Id == id;
            _ = LoadAsync();
        }

        private void RebuildCategoryTabs()
        {
            CategoryTabs.Clear();
            CategoryTabs.Add(new CategoryTabVm { Id = 0, Name = "全部", IsSelected = _selectedCategoryId == 0 });
            foreach (var c in Categories)
                CategoryTabs.Add(new CategoryTabVm { Id = c.Id, Name = c.Name, IsSelected = c.Id == _selectedCategoryId });
        }

        private void Copy(PhoneQueryRow row)
        {
            if (row == null) return;
            try { System.Windows.Clipboard.SetText(row.Phone); StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已复制 " + row.Phone; }
            catch (Exception ex) { ErrorText = "复制失败：" + ex.Message; }
        }

        private async Task SetTopAsync(PhoneQueryRow row)
        {
            if (row == null || row.IsEmergency) return;   // 紧急条目禁取消置顶（BR-TEL-03），按钮已禁用，此处双保险防 409
            await RunAsync(async () =>
            {
                await Api.SetPhoneEntryTopAsync(row.Dto.Id, !row.IsTop);
                await LoadAsync();
            }, "置顶状态已更新");
        }
    }
}
