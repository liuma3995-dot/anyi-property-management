using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>支出登记行（PG-FIN-05，UC-FIN-005；T4F-5-1 收款方/关联对象列）。</summary>
    public class ExpenseRow : ObservableObject
    {
        public ExpenseDto Dto { get; set; }

        public string NoText { get { return "ZC-" + Dto.Id.ToString("D4"); } }

        public string DateText { get { return Dto.ExpenseDate.ToString("yyyy-MM-dd"); } }

        public string CategoryName { get { return Dto.CategoryName ?? "—"; } }

        public string Note { get { return string.IsNullOrEmpty(Dto.Note) ? "—" : Dto.Note; } }

        public string AmountText { get { return "-¥" + Dto.Amount.ToString("N2"); } }

        public string PayeeText { get { return string.IsNullOrEmpty(Dto.Payee) ? "—" : Dto.Payee; } }

        /// <summary>关联对象本期表单可空（M4 简单版），统一展示“—”。</summary>
        public string ObjectText { get { return "—"; } }

        public string StatusText { get { return Dto.Status == 0 ? "已支付" : "已删除"; } }

        public Brush StatusBrush { get { return Dto.Status == 0 ? OkBrush : MutedBrush; } }

        public Brush StatusBg { get { return Dto.Status == 0 ? OkBg : MutedBg; } }

        public bool IsActive { get { return Dto.Status == 0; } }

        private static readonly Brush OkBrush = Br("#12805C");
        private static readonly Brush OkBg = Br("#E8F7F1");
        private static readonly Brush MutedBrush = Br("#98A2B3");
        private static readonly Brush MutedBg = Br("#F1F3F7");

        private static Brush Br(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    /// <summary>支出登记页（PG-FIN-05，UC-FIN-005/006，无审批流=登记+权限控制；T4F-5-1：9 列表格/搜索收款方/payee 落库/查看/删除二次确认）。</summary>
    public class ExpenseViewModel : FinancePageViewModel
    {
        private string _monthTotalText = "¥0";
        private string _monthTotalSub = "较上月 —";
        private string _countText = "0";
        private string _countSub = "未删除记录";
        private string _pendingText = "0";
        private string _budgetText = "—";
        private bool _isFormVisible;
        private bool _isDetailVisible;
        private bool _isConfirmVisible;
        private ExpenseCategoryDto _selectedCategory;
        private ExpenseRow _selectedRow;
        private ExpenseRow _confirmRow;
        private decimal _amount;
        private DateTime _expenseDate = DateTime.Today;
        private string _note = string.Empty;
        private string _payee = string.Empty;
        private int _selectedFilterIndex;
        private int _selectedStatusFilter;
        private string _keyword = string.Empty;
        private bool _loading;

        public ExpenseViewModel(IApiClient api) : base(api)
        {
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            SearchCommand = new AsyncRelayCommand(LoadAsync);
            OpenFormCommand = new RelayCommand(() => IsFormVisible = true);
            CancelCommand = new RelayCommand(() => IsFormVisible = false);
            DeleteCommand = new RelayCommand<ExpenseRow>(RequestDelete);
            ConfirmDeleteCommand = new AsyncRelayCommand(DeleteConfirmedAsync);
            CancelDeleteCommand = new RelayCommand(() => { ConfirmRow = null; IsConfirmVisible = false; });
            ViewCommand = new RelayCommand<ExpenseRow>(row => { if (row != null) { SelectedRow = row; IsDetailVisible = true; } });
            CloseDetailCommand = new RelayCommand(() => IsDetailVisible = false);
            _ = LoadAsync();
        }

        public ObservableCollection<ExpenseCategoryDto> Categories { get; } = new ObservableCollection<ExpenseCategoryDto>();

        /// <summary>筛选用类别选项（首位“全部”）。</summary>
        public ObservableCollection<ExpenseCategoryDto> CategoryOptions { get; } = new ObservableCollection<ExpenseCategoryDto>();

        public ObservableCollection<ExpenseRow> Items { get; } = new ObservableCollection<ExpenseRow>();

        public string MonthTotalText { get { return _monthTotalText; } private set { SetProperty(ref _monthTotalText, value); } }

        public string MonthTotalSub { get { return _monthTotalSub; } private set { SetProperty(ref _monthTotalSub, value); } }

        public string CountText { get { return _countText; } private set { SetProperty(ref _countText, value); } }

        public string CountSub { get { return _countSub; } private set { SetProperty(ref _countSub, value); } }

        public string PendingText { get { return _pendingText; } private set { SetProperty(ref _pendingText, value); } }

        public string BudgetText { get { return _budgetText; } private set { SetProperty(ref _budgetText, value); } }

        public bool IsFormVisible { get { return _isFormVisible; } private set { SetProperty(ref _isFormVisible, value); } }

        public bool IsDetailVisible { get { return _isDetailVisible; } private set { SetProperty(ref _isDetailVisible, value); } }

        public bool IsConfirmVisible { get { return _isConfirmVisible; } private set { SetProperty(ref _isConfirmVisible, value); } }

        public ExpenseCategoryDto SelectedCategory { get { return _selectedCategory; } set { SetProperty(ref _selectedCategory, value); } }

        public ExpenseRow SelectedRow { get { return _selectedRow; } private set { SetProperty(ref _selectedRow, value); } }

        public ExpenseRow ConfirmRow { get { return _confirmRow; } private set { SetProperty(ref _confirmRow, value); } }

        public decimal Amount { get { return _amount; } set { SetProperty(ref _amount, value); } }

        public DateTime ExpenseDate { get { return _expenseDate; } set { SetProperty(ref _expenseDate, value); } }

        public string Note { get { return _note; } set { SetProperty(ref _note, value); } }

        public string Payee { get { return _payee; } set { SetProperty(ref _payee, value); } }

        public string Keyword { get { return _keyword; } set { SetProperty(ref _keyword, value); } }

        public int SelectedFilterIndex
        {
            get { return _selectedFilterIndex; }
            set { SetProperty(ref _selectedFilterIndex, value); }
        }

        /// <summary>状态筛选：0=全部 1=已支付 2=已删除。</summary>
        public int SelectedStatusFilter
        {
            get { return _selectedStatusFilter; }
            set { SetProperty(ref _selectedStatusFilter, value); }
        }

        public IAsyncRelayCommand SaveCommand { get; }

        public IAsyncRelayCommand SearchCommand { get; }

        public IRelayCommand OpenFormCommand { get; }

        public IRelayCommand CancelCommand { get; }

        public IRelayCommand<ExpenseRow> DeleteCommand { get; }

        public IAsyncRelayCommand ConfirmDeleteCommand { get; }

        public IRelayCommand CancelDeleteCommand { get; }

        public IRelayCommand<ExpenseRow> ViewCommand { get; }

        public IRelayCommand CloseDetailCommand { get; }

        public async Task LoadAsync()
        {
            // T4R-5：防重入。下拉 SelectionChanged -> LoadAsync 在加载期间再次触发时直接跳过，
            // 避免全量 Clear() 重建选项导致选中被重置（类别筛选恒为“全部”）与并发双刷。
            if (_loading) { return; }
            _loading = true;
            try
            {
                await LoadCoreAsync();
            }
            finally
            {
                _loading = false;
            }
        }

        private async Task LoadCoreAsync()
        {
            await RunAsync(async () =>
            {
                Categories.Clear();
                var cats = await Api.GetExpenseCategoriesAsync();
                var activeCats = cats.Where(x => x.Status == 0).ToList();
                foreach (var cat in activeCats)
                {
                    Categories.Add(cat);
                }
                if (SelectedCategory == null || !Categories.Any(x => x.Id == SelectedCategory.Id))
                {
                    SelectedCategory = Categories.FirstOrDefault();
                }
                // T4R-5：筛选“类别”下拉就地增删并保留当前选中；
                // 原实现每次 Clear() 重建会把绑定 SelectedIndex 重置为 -1，导致类别筛选永远回退“全部”。
                RefreshCategoryOptions(activeCats);

                Items.Clear();
                var page = await Api.QueryExpensesAsync(new PageRequest { PageIndex = 1, PageSize = 100 });
                var query = page.Items.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(Keyword))
                {
                    string kw = Keyword.Trim();
                    query = query.Where(x =>
                        (x.Note ?? string.Empty).Contains(kw) ||
                        (x.CategoryName ?? string.Empty).Contains(kw) ||
                        (x.Payee ?? string.Empty).Contains(kw) ||
                        ("ZC-" + x.Id.ToString("D4")).Contains(kw.ToUpperInvariant()));
                }
                var selectedFilter = SelectedFilterIndex > 0 && SelectedFilterIndex < CategoryOptions.Count
                    ? CategoryOptions[SelectedFilterIndex].Id
                    : 0;
                if (selectedFilter > 0)
                {
                    query = query.Where(x => x.CategoryId == selectedFilter);
                }
                if (SelectedStatusFilter == 1) { query = query.Where(x => x.Status == 0); }
                else if (SelectedStatusFilter == 2) { query = query.Where(x => x.Status != 0); }
                foreach (var dto in query.OrderByDescending(x => x.ExpenseDate))
                {
                    Items.Add(new ExpenseRow { Dto = dto });
                }

                int year = DateTime.Now.Year;
                int month = DateTime.Now.Month;
                var active = page.Items.Where(x => x.Status == 0).ToList();
                var monthItems = active.Where(x => x.ExpenseDate.Year == year && x.ExpenseDate.Month == month).ToList();
                MonthTotalText = "¥" + monthItems.Sum(x => x.Amount).ToString("N0");
                var lastMonthItems = active.Where(x => x.ExpenseDate.Year == year && x.ExpenseDate.Month == month - 1).ToList();
                MonthTotalSub = "较上月 —";
                if (lastMonthItems.Sum(x => x.Amount) > 0)
                {
                    decimal lastTotal = lastMonthItems.Sum(x => x.Amount);
                    decimal delta = (monthItems.Sum(x => x.Amount) - lastTotal) / lastTotal * 100m;
                    MonthTotalSub = delta >= 0 ? "较上月 +" + delta.ToString("0.0") + "%" : "较上月 " + delta.ToString("0.0") + "%";
                }
                CountText = active.Count.ToString();
                if (monthItems.Count > 0)
                {
                    var top = monthItems.GroupBy(x => x.CategoryName ?? "未分类")
                        .OrderByDescending(g => g.Sum(x => x.Amount)).First();
                    CountSub = top.Key + "类占 " + (top.Sum(x => x.Amount) / monthItems.Sum(x => x.Amount) * 100m).ToString("0") + "%";
                }
                else
                {
                    CountSub = "本月暂无支出";
                }
                PendingText = "0";
                BudgetText = "—";
            }, "支出记录已加载");
        }

        /// <summary>T4R-5：就地同步筛选“类别”下拉（首位“全部”），保留当前选中项不被 Clear() 重置。</summary>
        private void RefreshCategoryOptions(List<ExpenseCategoryDto> activeCats)
        {
            if (CategoryOptions.Count == 0)
            {
                CategoryOptions.Add(new ExpenseCategoryDto { Id = 0, Name = "全部" });
            }
            else if (CategoryOptions[0].Id != 0)
            {
                CategoryOptions.Insert(0, new ExpenseCategoryDto { Id = 0, Name = "全部" });
            }

            for (int i = CategoryOptions.Count - 1; i >= 0; i--)
            {
                if (CategoryOptions[i].Id == 0) { continue; }
                if (!activeCats.Any(c => c.Id == CategoryOptions[i].Id))
                {
                    CategoryOptions.RemoveAt(i);
                }
            }

            int insertPos = 1;
            foreach (var cat in activeCats)
            {
                bool exists = false;
                for (int j = 1; j < CategoryOptions.Count; j++)
                {
                    if (CategoryOptions[j].Id == cat.Id) { exists = true; break; }
                }
                if (!exists)
                {
                    if (insertPos >= CategoryOptions.Count) { CategoryOptions.Add(cat); }
                    else { CategoryOptions.Insert(insertPos, cat); }
                }
                insertPos++;
            }

            if (SelectedFilterIndex < 0 || SelectedFilterIndex >= CategoryOptions.Count)
            {
                SelectedFilterIndex = 0;
            }
        }

        private async Task SaveAsync()
        {
            if (SelectedCategory == null)
            {
                ErrorText = "请选择支出分类（BR-FIN-04）";
                return;
            }
            if (Amount <= 0)
            {
                ErrorText = "支出金额必须大于 0";
                return;
            }

            await RunAsync(async () =>
            {
                await Api.CreateExpenseAsync(new ExpenseCreateRequest
                {
                    CategoryId = SelectedCategory.Id,
                    Amount = Amount,
                    ExpenseDate = ExpenseDate,
                    Note = Note.Trim(),
                    Payee = Payee.Trim(),
                    Objects = null
                });
                IsFormVisible = false;
                Amount = 0m;
                Note = string.Empty;
                Payee = string.Empty;
                await LoadAsync();
            }, "支出已登记（登记即生效，联动流水 PG-FIN-08）");
        }

        private void RequestDelete(ExpenseRow row)
        {
            if (row == null) { return; }
            ConfirmRow = row;
            IsConfirmVisible = true;
        }

        private async Task DeleteConfirmedAsync()
        {
            ExpenseRow row = ConfirmRow;
            if (row == null) { return; }
            await RunAsync(async () =>
            {
                await Api.DeleteExpenseAsync(row.Dto.Id);
                ConfirmRow = null;
                IsConfirmVisible = false;
                await LoadAsync();
            }, "支出已删除（软删除留痕 BR-FIN-10）");
        }
    }
}
