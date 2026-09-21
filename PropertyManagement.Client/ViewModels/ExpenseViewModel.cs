using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>支出登记行（PG-FIN-05，UC-FIN-005；T4F-5-1 收款方列；「关联对象」列已于 v1.1.0 第 8 轮下线）。</summary>
    public class ExpenseRow : ObservableObject
    {
        public ExpenseDto Dto { get; set; }

        public string NoText { get { return "ZC-" + Dto.Id.ToString("D4"); } }

        public string DateText { get { return Dto.ExpenseDate.ToString("yyyy-MM-dd"); } }

        public string CategoryName { get { return Dto.CategoryName ?? "—"; } }

        public string Note { get { return string.IsNullOrEmpty(Dto.Note) ? "—" : Dto.Note; } }

        public string AmountText { get { return "-¥" + Dto.Amount.ToString("N2"); } }

        public string PayeeText { get { return string.IsNullOrEmpty(Dto.Payee) ? "—" : Dto.Payee; } }

        public string StatusText { get { return Dto.Status == 0 ? "已支付" : "已删除"; } }

        public Brush StatusBrush { get { return Dto.Status == 0 ? OkBrush : MutedBrush; } }

        public Brush StatusBg { get { return Dto.Status == 0 ? OkBg : MutedBg; } }

        public bool IsActive { get { return Dto.Status == 0; } }

        private bool _isSelected;

        /// <summary>v1.1.0-⑤：批量删除勾选状态（仅选择模式且未删除行可勾选）。</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

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
        // v1.1.0 第 7 轮：本月预算（系统参数 finance.budget.monthly）
        private const string BudgetParamKey = "finance.budget.monthly";
        private decimal _monthBudget;
        private string _budgetSub = "可在卡片内设置月度预算";
        private bool _isBudgetFormVisible;
        private string _budgetInput = string.Empty;
        // v1.1.0 第 7 轮：支出分类新增/删除（登记支出表单内）
        private string _newCategoryName = string.Empty;
        private string _categoryHint = string.Empty;
        private bool _isSelectionMode;              // v1.1.0-⑤：点击「批量删除记录」后才显示勾选框列
        private bool _isBatchConfirmVisible;
        private string _batchConfirmText = string.Empty;

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
            BatchDeleteRecordsCommand = new RelayCommand(BatchDeleteRecords);
            CancelSelectionCommand = new RelayCommand(ExitSelectionMode);
            ConfirmBatchDeleteCommand = new AsyncRelayCommand(ConfirmBatchDeleteAsync);
            CancelBatchDeleteConfirmCommand = new RelayCommand(() => IsBatchConfirmVisible = false);
            // v1.1.0 第 7 轮：预算设置 + 支出分类新增/删除
            OpenBudgetCommand = new RelayCommand(OpenBudgetForm);
            SaveBudgetCommand = new AsyncRelayCommand(SaveBudgetAsync);
            CancelBudgetCommand = new RelayCommand(() => IsBudgetFormVisible = false);
            AddCategoryCommand = new AsyncRelayCommand(AddCategoryAsync);
            DeleteCategoryCommand = new AsyncRelayCommand(DeleteCategoryAsync);
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

        /// <summary>本月预算卡片副文案：已用进度 / 未设置提示（v1.1.0 第 7 轮）。</summary>
        public string BudgetSubText { get { return _budgetSub; } private set { SetProperty(ref _budgetSub, value); } }

        public bool IsBudgetFormVisible
        {
            get { return _isBudgetFormVisible; }
            private set { SetProperty(ref _isBudgetFormVisible, value); }
        }

        public string BudgetInput { get { return _budgetInput; } set { SetProperty(ref _budgetInput, value); } }

        /// <summary>登记支出表单内的分类维护提示（新增/删除反馈，v1.1.0 第 7 轮）。</summary>
        public string CategoryHintText { get { return _categoryHint; } private set { SetProperty(ref _categoryHint, value); } }

        public string NewCategoryName { get { return _newCategoryName; } set { SetProperty(ref _newCategoryName, value); } }

        public IRelayCommand OpenBudgetCommand { get; }

        public IAsyncRelayCommand SaveBudgetCommand { get; }

        public IRelayCommand CancelBudgetCommand { get; }

        public IAsyncRelayCommand AddCategoryCommand { get; }

        public IAsyncRelayCommand DeleteCategoryCommand { get; }

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

        // ---------- v1.1.0-⑤：支出记录批量删除（软删留痕 BR-FIN-10） ----------

        public IRelayCommand BatchDeleteRecordsCommand { get; }

        public IRelayCommand CancelSelectionCommand { get; }

        public IAsyncRelayCommand ConfirmBatchDeleteCommand { get; }

        public IRelayCommand CancelBatchDeleteConfirmCommand { get; }

        /// <summary>
        /// 选择模式：默认 false（表格不显示勾选框列）；点击「批量删除记录」后进入选择模式，
        /// 勾选框列显示且按钮文案变为「删除所选」；取消或删除完成后退出并清空勾选。
        /// </summary>
        public bool IsSelectionMode
        {
            get { return _isSelectionMode; }
            private set
            {
                if (SetProperty(ref _isSelectionMode, value))
                {
                    OnPropertyChanged(nameof(BatchDeleteButtonText));
                }
            }
        }

        public string BatchDeleteButtonText
        {
            get { return IsSelectionMode ? "删除所选" : "批量删除记录"; }
        }

        /// <summary>批量删除二次确认浮层（与单条删除弹层相互独立，避免相互干扰）。</summary>
        public bool IsBatchConfirmVisible
        {
            get { return _isBatchConfirmVisible; }
            set { SetProperty(ref _isBatchConfirmVisible, value); }
        }

        public string BatchConfirmText
        {
            get { return _batchConfirmText; }
            private set { SetProperty(ref _batchConfirmText, value); }
        }

        /// <summary>可勾选行（仅未删除记录）：已删除记录无可删除内容，其留痕由「一键清理残余数据」处理。</summary>
        private List<ExpenseRow> SelectableRows
        {
            get { return Items.Where(x => x.IsActive).ToList(); }
        }

        /// <summary>记录全选/取消全选（表头勾选框双向绑定，仅作用于未删除记录）。</summary>
        public bool IsAllRecordsSelected
        {
            get
            {
                List<ExpenseRow> rows = SelectableRows;
                return rows.Count > 0 && rows.All(r => r.IsSelected);
            }
            set
            {
                foreach (ExpenseRow row in SelectableRows) { row.IsSelected = value; }
                OnPropertyChanged(nameof(IsAllRecordsSelected));
            }
        }

        /// <summary>刷新全选态（勾选/取消单行后由视图调用）。</summary>
        public void RefreshSelectAllState()
        {
            OnPropertyChanged(nameof(IsAllRecordsSelected));
        }

        /// <summary>批量删除入口：第一次点击进入选择模式，已在选择模式时校验勾选并弹出二次确认。</summary>
        private void BatchDeleteRecords()
        {
            if (!IsSelectionMode)
            {
                EnterSelectionMode();
                return;
            }

            List<ExpenseRow> selected = Items.Where(x => x.IsSelected && x.IsActive).ToList();
            if (selected.Count == 0)
            {
                ErrorText = "请先勾选要删除的支出记录（可勾选单条，也可勾选表头全选），或点击【取消】退出批量删除";
                return;
            }
            decimal amount = selected.Sum(x => x.Dto.Amount);
            BatchConfirmText = "将删除所选 " + selected.Count + " 笔支出（合计 ¥" + amount.ToString("N2") +
                               "）。删除为软删留痕，历史记录保留，可在「备份与恢复」页一键清理。确认删除？";
            IsBatchConfirmVisible = true;
        }

        private async Task ConfirmBatchDeleteAsync()
        {
            IsBatchConfirmVisible = false;
            var ids = Items.Where(x => x.IsSelected && x.IsActive).Select(x => x.Dto.Id).Distinct().ToList();
            if (ids.Count == 0) { return; }

            string message = null;
            await RunAsync(async () =>
            {
                RecordBatchDeleteResultDto result = await Api.BatchDeleteExpensesAsync(
                    new RecordBatchDeleteRequest { Ids = ids });
                message = "已删除 " + (result == null ? 0 : result.Deleted) + " 笔支出（软删留痕，可在「备份与恢复」页一键清理）";
                await LoadAsync();
                ExitSelectionMode();
            }, null);
            StatusText = string.IsNullOrEmpty(message) ? StatusText : message;
        }

        /// <summary>进入选择模式：显示勾选框列并清空历史勾选。</summary>
        private void EnterSelectionMode()
        {
            foreach (ExpenseRow row in Items) { row.IsSelected = false; }
            ErrorText = string.Empty;
            IsSelectionMode = true;
            OnPropertyChanged(nameof(IsAllRecordsSelected));
        }

        /// <summary>退出选择模式：隐藏勾选框列并清空勾选。</summary>
        private void ExitSelectionMode()
        {
            foreach (ExpenseRow row in Items) { row.IsSelected = false; }
            IsSelectionMode = false;
            IsBatchConfirmVisible = false;
            OnPropertyChanged(nameof(IsAllRecordsSelected));
        }

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
                // v1.1.0 第 7 轮：本月预算来自系统参数（finance.budget.monthly），并显示已用进度
                await LoadBudgetAsync();
                ApplyBudget(monthItems.Sum(x => x.Amount));
            }, "支出记录已加载");
        }

        /// <summary>读取系统参数中的月度预算（0/空 = 未设置）。</summary>
        private async Task LoadBudgetAsync()
        {
            try
            {
                List<ParamDto> ps = await Api.GetSystemParamsAsync();
                ParamDto row = ps == null ? null : ps.FirstOrDefault(x => x.ParamKey == BudgetParamKey);
                decimal value;
                _monthBudget = row != null && decimal.TryParse(row.ParamValue, out value) && value > 0 ? value : 0m;
            }
            catch
            {
                _monthBudget = 0m;   // 参数接口不可用时不阻断支出页加载
            }
        }

        /// <summary>刷新预算卡片：金额 + 已用进度/剩余（未设置时给出设置入口提示）。</summary>
        private void ApplyBudget(decimal monthUsed)
        {
            if (_monthBudget <= 0)
            {
                BudgetText = "未设置";
                BudgetSubText = "点右侧【设置预算】填写月度预算";
                return;
            }
            decimal rate = monthUsed / _monthBudget * 100m;
            decimal left = _monthBudget - monthUsed;
            BudgetText = "¥" + _monthBudget.ToString("N0");
            BudgetSubText = "已用 " + rate.ToString("0.#") + "%（¥" + monthUsed.ToString("N0") + "），" +
                            (left >= 0 ? "剩余 ¥" + left.ToString("N0") : "超支 ¥" + (-left).ToString("N0"));
        }

        private void OpenBudgetForm()
        {
            BudgetInput = _monthBudget > 0 ? _monthBudget.ToString("0.##") : string.Empty;
            IsBudgetFormVisible = true;
        }

        private async Task SaveBudgetAsync()
        {
            decimal value;
            if (!decimal.TryParse((BudgetInput ?? string.Empty).Trim(), out value) || value < 0)
            {
                ErrorText = "月度预算必须是不小于 0 的数字（0 = 清除预算）";
                return;
            }

            await RunAsync(async () =>
            {
                await Api.SetSystemParamAsync(BudgetParamKey, value.ToString("0.##"));
                _monthBudget = value > 0 ? value : 0m;
                IsBudgetFormVisible = false;
                await LoadAsync();
            }, value > 0 ? "本月预算已保存" : "已清除本月预算");
        }

        /// <summary>新增支出分类（登记支出表单内）：新增后自动选中，保证「分类为空」不再阻断登记。</summary>
        private async Task AddCategoryAsync()
        {
            string name = (NewCategoryName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                CategoryHintText = "请输入新分类名称";
                return;
            }
            if (Categories.Any(x => string.Equals(x.Name, name, StringComparison.Ordinal)))
            {
                CategoryHintText = "分类「" + name + "」已存在";
                return;
            }

            await RunAsync(async () =>
            {
                ExpenseCategoryDto created = await Api.CreateExpenseCategoryAsync(new ExpenseCategoryRequest { Name = name });
                NewCategoryName = string.Empty;
                await LoadCoreAsync();
                if (created != null)
                {
                    SelectedCategory = Categories.FirstOrDefault(x => x.Id == created.Id) ?? Categories.FirstOrDefault();
                }
                CategoryHintText = "分类「" + name + "」已新增并选中";
            }, null);
        }

        /// <summary>删除当前选中分类：被支出记录引用时服务端拒绝（提示改用停用），前端给出中文反馈。</summary>
        private async Task DeleteCategoryAsync()
        {
            if (SelectedCategory == null)
            {
                CategoryHintText = "请先选择要删除的分类";
                return;
            }
            string name = SelectedCategory.Name;
            if (MessageBox.Show("确认删除支出分类「" + name + "」？\n被支出记录引用的分类不能删除（可停用）。",
                    "删除支出分类", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }

            await RunAsync(async () =>
            {
                await Api.DeleteExpenseCategoryAsync(SelectedCategory.Id);
                SelectedCategory = null;
                await LoadCoreAsync();
                CategoryHintText = "分类「" + name + "」已删除";
            }, null);
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
                ErrorText = "请选择支出分类";
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
            }, "支出已登记（登记即生效，已联动收支明细流水）");
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
            }, "支出已删除（软删除留痕）");
        }
    }
}
