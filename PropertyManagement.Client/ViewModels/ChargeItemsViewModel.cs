using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>收费项目维护页行（PG-FIN-01，UC-FIN-001，BR-FIN-03；T4F-1-3 新字段展示）。</summary>
    public class ChargeItemRow : ObservableObject
    {
        private bool _isChecked;
        public ChargeItemDto Dto { get; set; }
        /// <summary>批量选择标记。</summary>
        public bool IsChecked { get { return _isChecked; } set { SetProperty(ref _isChecked, value); } }

        public int Id { get { return Dto.Id; } }

        public string NoText { get { return "XM-" + Dto.Id.ToString("D3"); } }

        public string Name { get { return Dto.Name; } }

        /// <summary>CHG-v1.1.2-26：收费标准名（价目表重构后列表以「收费标准 + 规格」呈现价格口径）。</summary>
        public string StandardText { get { return string.IsNullOrWhiteSpace(Dto.StandardName) ? "—" : Dto.StandardName; } }

        /// <summary>CHG-v1.1.2-26：规格条数（同项目多规格 = 差异化收费不再多建项目）。</summary>
        public string SpecText { get { return Dto.SpecCount > 0 ? Dto.SpecCount + " 条规格" : "统一价"; } }

        /// <summary>
        /// CHG-v1.1.2-26 / FIX-v1.1.2-03：默认单价列**只展示一条规格**（多条时以「起」标注），
        /// 避免同一单元格塞进多档价格造成拥挤与视觉遮挡；完整价目表见「价目表」页签。
        /// </summary>
        public string SpecPriceText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Dto.SpecPriceText))
                {
                    return PriceText;
                }
                var parts = Dto.SpecPriceText.Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x =>
                    {
                        int idx = x.LastIndexOf(' ');
                        return idx > 0 ? x.Substring(0, idx) + " ¥" + x.Substring(idx + 1) : x;
                    })
                    .ToList();
                if (parts.Count == 0) { return PriceText; }
                string first = parts[0];
                return Dto.SpecCount > 1 ? first + " 起" : first;
            }
        }

        public string CategoryText { get { return string.IsNullOrEmpty(Dto.Category) ? "未分类" : Dto.Category; } }

        public string MethodText
        {
            get
            {
                if (!string.IsNullOrEmpty(Dto.MethodName)) { return Dto.MethodName; }
                switch (Dto.PayMode)
                {
                    case ChargePayMode.Yearly: return "按年";
                    case ChargePayMode.Monthly: return "按月";
                    default: return "临时";
                }
            }
        }

        public string PriceText
        {
            get
            {
                string unit = string.IsNullOrEmpty(Dto.PriceUnit) ? string.Empty : Dto.PriceUnit;
                string baseText = "¥" + Dto.UnitPrice.ToString("0.00") + "/" + unit;
                string suffix = CycleSuffix();
                string text = string.IsNullOrEmpty(suffix) ? baseText : baseText + "·" + suffix;
                // CHG-v1.1.2-06：配置了自定义公式的项目，列表直接标注公式，避免「看不到实际计费口径」
                return string.IsNullOrWhiteSpace(Dto.Formula) ? text : text + " · 公式：" + Dto.Formula;
            }
        }

        public string CycleText
        {
            get
            {
                switch (Dto.CycleType)
                {
                    case BillingCycleType.Monthly: return "每月";
                    case BillingCycleType.Yearly: return "每年";
                    case BillingCycleType.OneTime: return "一次性";
                    case BillingCycleType.Custom: return string.IsNullOrEmpty(Dto.CycleName) ? "自定义" : Dto.CycleName;
                    default: return "每月";
                }
            }
        }

        /// <summary>单价列周期后缀（月/年/空/每季→季、每半年→半年）。</summary>
        private string CycleSuffix()
        {
            switch (Dto.CycleType)
            {
                case BillingCycleType.Monthly: return "月";
                case BillingCycleType.Yearly: return "年";
                case BillingCycleType.OneTime: return string.Empty;
                case BillingCycleType.Custom:
                    string name = Dto.CycleName ?? string.Empty;
                    return name.StartsWith("每") ? name.Substring(1) : name;
                default: return "月";
            }
        }

        public string StatusText { get { return Dto.Status == 0 ? "启用" : "停用"; } }

        /// <summary>
        /// CHG-v1.1.0-16/17：缴费对象展示（房产/车位/业主/自定义项名称）。
        /// 名称以 charge_object 字典为准，字典项缺失时按对象类型回落。
        /// </summary>
        public string ObjectTypeText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Dto.ObjectName)) { return Dto.ObjectName.Trim(); }
                switch (Dto.ObjectType)
                {
                    case ChargeObjectType.Parking: return "车位";
                    case ChargeObjectType.Owner: return "业主";
                    case ChargeObjectType.Custom: return "自定义";
                    default: return "房产";
                }
            }
        }

        public string ToggleText { get { return IsEnabled ? "停用" : "启用"; } }

        public Brush StatusBrush { get { return Dto.Status == 0 ? StatusOkBrush : StatusOffBrush; } }

        public Brush StatusBg { get { return Dto.Status == 0 ? StatusOkBg : StatusOffBg; } }

        public bool IsEnabled { get { return Dto.Status == 0; } }

        private static readonly Brush StatusOkBrush = Br("#12805C");
        private static readonly Brush StatusOkBg = Br("#E8F7F1");
        private static readonly Brush StatusOffBrush = Br("#667085");
        private static readonly Brush StatusOffBg = Br("#F1F3F7");

        private static Brush Br(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    /// <summary>收费项目维护页（PG-FIN-01，T4F-1-1~1-4：搜索占位/类别字典/表格对齐/表单重建+业务联动）。</summary>
    public partial class ChargeItemsViewModel : FinancePageViewModel
    {
        private static readonly DictItemDto AllFilter = new DictItemDto { Id = 0, ItemName = "全部" };

        private string _keyword = string.Empty;
        private DictItemDto _categoryFilter;
        private int _statusFilter;
        private bool _isLoadingDicts;
        private bool _inited;

        private bool _isFormVisible;
        private int _editingId;
        private string _formName = string.Empty;
        private DictItemDto _formCategory;
        private DictItemDto _formMethod;
        private decimal _formUnitPrice;
        private DictItemDto _formObject;
        private DictItemDto _formCycle;
        private bool _formEnabled = true;
        private string _formFormula = string.Empty;
        private ChargeFormulaTemplate _formFormulaTemplate;

        /// <summary>CHG-v1.1.2-06：计价公式模板（选中即回填公式文本，可继续手改）。</summary>
        public class ChargeFormulaTemplate
        {
            public string Name { get; set; }
            public string Formula { get; set; }
            public string Description { get; set; }
        }

        private bool _isCustomDialogVisible;
        private string _customDialogTitle = string.Empty;
        private string _customTypeCode = string.Empty;
        private bool _customShowRemark;
        private string _customInputName = string.Empty;
        /// <summary>CHG-v1.1.0-19：自定义弹窗名称输入框的语境化提示（类别/计价方式/计费周期/缴费对象各不相同）。</summary>
        private string _customInputPlaceholder = "请输入名称";
        private readonly DispatcherTimer _searchDebounce;
        private bool _isConfirmVisible;
        private ChargeItemRow _confirmRow;
        private bool _isSelectAll;
        /// <summary>FIX-v1.1.2-01：并发加载保护（序号令牌 + 写入锁）。</summary>
        private int _itemsLoadSeq;
        private readonly object _itemsLoadSync = new object();
        private string _customInputRemark = string.Empty;

        public ChargeItemsViewModel(IApiClient api) : base(api)
        {
            NewCommand = new RelayCommand(StartNew);
            EditCommand = new RelayCommand<ChargeItemRow>(StartEdit);
            SearchCommand = new AsyncRelayCommand(LoadAsync);
            CancelCommand = new RelayCommand(CancelForm);
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            ToggleCommand = new AsyncRelayCommand<ChargeItemRow>(ToggleAsync);
            OpenCustomCategoryCommand = new RelayCommand(() => OpenCustomDialog("charge_category", "自定义类别", false));
            OpenCustomMethodCommand = new RelayCommand(() => OpenCustomDialog("charge_method", "自定义计价方式", true));
            OpenCustomCycleCommand = new RelayCommand(() => OpenCustomDialog("charge_cycle", "自定义计费周期", false));
            OpenCustomObjectCommand = new RelayCommand(() => OpenCustomDialog("charge_object", "自定义缴费对象", false));
            ConfirmCustomCommand = new AsyncRelayCommand(ConfirmCustomAsync);
            CancelCustomCommand = new RelayCommand(CancelCustom);
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounce.Tick += async (s, e) =>
            {
                _searchDebounce.Stop();
                await LoadAsync();
            };
            DeleteCommand = new RelayCommand<ChargeItemRow>(RequestDelete);
            ConfirmDeleteCommand = new AsyncRelayCommand(ConfirmDeleteAsync);
            CancelDeleteCommand = new RelayCommand(() => { ConfirmRow = null; IsConfirmVisible = false; });
            // CHG-v1.1.2-26：价目表 / 规格 / 计量变量的命令装配
            InitPriceList();
            _ = InitAsync();
        }

        public ObservableCollection<DictItemDto> FilterCategories { get; } = new ObservableCollection<DictItemDto>();
        public ObservableCollection<DictItemDto> FormCategories { get; } = new ObservableCollection<DictItemDto>();
        public ObservableCollection<DictItemDto> Methods { get; } = new ObservableCollection<DictItemDto>();
        public ObservableCollection<DictItemDto> Cycles { get; } = new ObservableCollection<DictItemDto>();
        /// <summary>CHG-v1.1.0-17：缴费对象选项（charge_object 字典：房产/车位/业主 + 自定义项）。</summary>
        public ObservableCollection<DictItemDto> ChargeObjects { get; } = new ObservableCollection<DictItemDto>();
        public ObservableCollection<ChargeItemRow> Items { get; } = new ObservableCollection<ChargeItemRow>();

        public string Keyword
        {
            get { return _keyword; }
            set
            {
                if (SetProperty(ref _keyword, value))
                {
                    _searchDebounce?.Stop();
                    _searchDebounce?.Start();
                }
            }
        }

        public DictItemDto CategoryFilter
        {
            get { return _categoryFilter; }
            set
            {
                if (SetProperty(ref _categoryFilter, value) && !_isLoadingDicts && _inited)
                {
                    _ = LoadAsync();
                }
            }
        }

        public int StatusFilter
        {
            get { return _statusFilter; }
            set
            {
                if (SetProperty(ref _statusFilter, value) && _inited)
                {
                    _ = LoadAsync();
                }
            }
        }

        public bool IsFormVisible { get { return _isFormVisible; } private set { SetProperty(ref _isFormVisible, value); } }

        public bool IsEditing { get { return _editingId > 0; } }

        public string FormTitle { get { return _editingId > 0 ? "编辑收费项目" : "新增收费项目"; } }

        public string FormName { get { return _formName; } set { SetProperty(ref _formName, value); } }

        public DictItemDto FormCategory { get { return _formCategory; } set { SetProperty(ref _formCategory, value); } }

        public DictItemDto FormMethod
        {
            get { return _formMethod; }
            set
            {
                if (SetProperty(ref _formMethod, value))
                {
                    OnPropertyChanged(nameof(PriceUnitHint));
                    // CHG-v1.1.0-16/17：计价方式决定缴费对象的默认值（按车位→车位；按卡/一次性→业主；其余→房产）
                    if (value != null)
                    {
                        string code = value.ItemCode ?? string.Empty;
                        string objectCode = string.Equals(code, "parking", StringComparison.OrdinalIgnoreCase) ? "parking"
                            : (string.Equals(code, "card", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(code, "onetime", StringComparison.OrdinalIgnoreCase) ? "owner" : "property");
                        DictItemDto fixedObject = ChargeObjects.FirstOrDefault(x =>
                            string.Equals(x.ItemCode, objectCode, StringComparison.OrdinalIgnoreCase));
                        if (fixedObject != null) { FormObject = fixedObject; }
                    }
                }
            }
        }

        public decimal FormUnitPrice { get { return _formUnitPrice; } set { SetProperty(ref _formUnitPrice, value); } }

        /// <summary>
        /// CHG-v1.1.0-16/17：缴费对象（charge_object 字典项）——房产/车位/业主为系统固定三项，
        /// 其余为自定义项（租户/广告商/外部单位等）。口径与生成账单表单的「缴费对象」一致。
        /// </summary>
        public DictItemDto FormObject
        {
            get { return _formObject; }
            set
            {
                if (SetProperty(ref _formObject, value))
                {
                    OnPropertyChanged(nameof(FormObjectHint));
                    OnPropertyChanged(nameof(IsCustomObjectSelected));
                }
            }
        }

        /// <summary>CHG-v1.1.0-17：自定义缴费对象提示（出账时按基础信息调用，自定义项暂不支持批量出账）。</summary>
        public string FormObjectHint
        {
            get
            {
                if (FormObject == null) { return "缴费对象决定该项目可对哪类对象出账"; }
                string baseHint = IsCustomObjectSelected
                    ? "自定义缴费对象：适用于租户、广告商等无房产/车位/业主档案的缴费方；出账时在账单工作台手工填写缴费对象名称"
                    : "缴费对象决定该项目向哪类对象出账（与生成账单表单一致）";
                // CHG-v1.1.2-06：计价方式与缴费对象的匹配由「硬拦」改为「提示建议」（可自写公式覆盖）
                string advice = ObjectMethodAdvice();
                return string.IsNullOrEmpty(advice) ? baseHint : baseHint + "\n提示：" + advice;
            }
        }

        /// <summary>
        /// CHG-v1.1.2-06：计价方式与缴费对象的搭配建议（不再阻断保存）。
        /// 例：选了「按建筑面积」却把缴费对象选成车位/业主时，按面积取值将为空 —— 提示改用房产或自写公式。
        /// </summary>
        private string ObjectMethodAdvice()
        {
            if (FormObject == null || FormMethod == null) { return null; }
            bool usesArea = string.Equals(FormMethod.ItemCode, "area", StringComparison.OrdinalIgnoreCase) ||
                            (FormFormula != null && FormFormula.Contains("面积"));
            if (!usesArea) { return null; }
            if (string.Equals(FormObject.ItemCode, "property", StringComparison.OrdinalIgnoreCase)) { return null; }
            return "当前口径要用到「建筑面积」，建议把缴费对象改为「房产」；若确实要对车位/业主计费，请改用固定金额或自写公式";
        }

        /// <summary>CHG-v1.1.0-17：当前是否选择了自定义缴费对象。</summary>
        public bool IsCustomObjectSelected
        {
            get { return FormObject != null && !IsFixedObjectCode(FormObject.ItemCode); }
        }

        /// <summary>CHG-v1.1.0-17：系统固定缴费对象编码（property/parking/owner）。</summary>
        internal static bool IsFixedObjectCode(string itemCode)
        {
            return string.Equals(itemCode, "property", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemCode, "parking", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemCode, "owner", StringComparison.OrdinalIgnoreCase);
        }

        public DictItemDto FormCycle { get { return _formCycle; } set { SetProperty(ref _formCycle, value); } }

        public bool FormEnabled { get { return _formEnabled; } set { SetProperty(ref _formEnabled, value); } }

        // ---------- CHG-v1.1.2-06：自定义计价公式 ----------

        /// <summary>计价公式模板（按建筑面积/按面积×月数/按户/按车位/按张/自定义）。</summary>
        public ObservableCollection<ChargeFormulaTemplate> FormulaTemplates { get; } = new ObservableCollection<ChargeFormulaTemplate>
        {
            new ChargeFormulaTemplate { Name = "按建筑面积（月缴）", Formula = "单价 * 面积", Description = "适合按月收取的物业费：单价（元/㎡）× 建筑面积" },
            new ChargeFormulaTemplate { Name = "按建筑面积 × 月数（年缴/季缴）", Formula = "单价 * 面积 * 月数", Description = "适合按年/季预收的物业费：系统按计费周期自动折算月数" },
            new ChargeFormulaTemplate { Name = "按户", Formula = "单价", Description = "每户一笔固定金额" },
            new ChargeFormulaTemplate { Name = "按车位", Formula = "单价", Description = "每个车位一笔固定金额" },
            new ChargeFormulaTemplate { Name = "按张", Formula = "单价 * 数量", Description = "按张/按次计费（数量默认 1）" },
            new ChargeFormulaTemplate { Name = "自定义", Formula = "", Description = "自行书写公式，可用变量：单价、面积、月数、天数、数量" }
        };

        /// <summary>计价公式（选填）。留空 = 按所选计价方式的内置口径计算。</summary>
        public string FormFormula
        {
            get { return _formFormula; }
            set
            {
                if (SetProperty(ref _formFormula, value))
                {
                    OnPropertyChanged(nameof(FormulaHint));
                    OnPropertyChanged(nameof(HasFormulaError));
                    OnPropertyChanged(nameof(FormObjectHint));
                }
            }
        }

        /// <summary>公式模板选择：选中即把模板公式写入公式框（「自定义」清空，交给用户手写）。</summary>
        public ChargeFormulaTemplate FormFormulaTemplate
        {
            get { return _formFormulaTemplate; }
            set
            {
                if (SetProperty(ref _formFormulaTemplate, value) && value != null)
                {
                    FormFormula = value.Formula ?? string.Empty;
                }
            }
        }

        /// <summary>公式实时校验：合法（或留空）显示口径说明，非法显示中文原因。</summary>
        public string FormulaHint
        {
            get
            {
                string error = ValidateFormula(_formFormula);
                if (error != null) { return error; }
                return string.IsNullOrWhiteSpace(_formFormula)
                    ? "留空：按所选计价方式计算（按建筑面积 = 单价 × 面积，其余 = 单价）"
                    : "生效口径：出账时按该公式计算金额。可用变量：单价 / 面积 / 月数 / 天数 / 数量，运算符仅 + - * / ( )";
            }
        }

        public bool HasFormulaError { get { return ValidateFormula(_formFormula) != null; } }

        /// <summary>公式合法性（与画布同口径的轻量校验；服务端仍会二次校验）。</summary>
        private static string ValidateFormula(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula)) { return null; }
            string text = formula.Trim();
            string[] variables = { "单价", "面积", "月数", "天数", "数量" };
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '(') { depth++; continue; }
                if (c == ')') { depth--; if (depth < 0) { return "公式括号不匹配：多了一个右括号"; } continue; }
                if (char.IsDigit(c) || c == '.' || c == '+' || c == '-' || c == '*' || c == '/' || char.IsWhiteSpace(c)) { continue; }
                if (char.IsLetter(c))
                {
                    int start = i;
                    while (i < text.Length && char.IsLetter(text[i])) { i++; }
                    string token = text.Substring(start, i - start);
                    i--;
                    bool known = false;
                    foreach (string v in variables) { if (v == token) { known = true; break; } }
                    if (!known) { return "公式中的变量「" + token + "」无效，可用变量：" + string.Join("、", variables); }
                    continue;
                }
                return "公式中存在无法识别的字符「" + c + "」";
            }
            if (depth != 0) { return "公式括号不匹配：缺少右括号"; }
            return null;
        }

        /// <summary>单价单位提示（随计价方式联动：按建筑面积→元/㎡；按户→元/户…）。</summary>
        public string PriceUnitHint
        {
            get
            {
                if (FormMethod == null) { return "元"; }
                string remark = FormMethod.Remark == null ? string.Empty : FormMethod.Remark.Trim();
                return string.IsNullOrEmpty(remark) ? "元" : "元/" + remark;
            }
        }

        public bool IsConfirmVisible { get { return _isConfirmVisible; } private set { SetProperty(ref _isConfirmVisible, value); } }

        public ChargeItemRow ConfirmRow { get { return _confirmRow; } private set { SetProperty(ref _confirmRow, value); } }
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

        public bool IsCustomDialogVisible { get { return _isCustomDialogVisible; } private set { SetProperty(ref _isCustomDialogVisible, value); } }

        public string CustomDialogTitle { get { return _customDialogTitle; } private set { SetProperty(ref _customDialogTitle, value); } }

        public bool CustomShowRemark { get { return _customShowRemark; } private set { SetProperty(ref _customShowRemark, value); } }

        public string CustomInputName { get { return _customInputName; } set { SetProperty(ref _customInputName, value); } }

        /// <summary>CHG-v1.1.0-19：自定义项名称输入框提示（按字典类型区分语境）。</summary>
        public string CustomInputPlaceholder { get { return _customInputPlaceholder; } private set { SetProperty(ref _customInputPlaceholder, value); } }

        public string CustomInputRemark { get { return _customInputRemark; } set { SetProperty(ref _customInputRemark, value); } }

        public IRelayCommand NewCommand { get; }
        public IRelayCommand<ChargeItemRow> EditCommand { get; }
        public IAsyncRelayCommand SearchCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IAsyncRelayCommand<ChargeItemRow> ToggleCommand { get; }
        public IRelayCommand OpenCustomCategoryCommand { get; }
        public IRelayCommand OpenCustomMethodCommand { get; }
        public IRelayCommand OpenCustomCycleCommand { get; }
        /// <summary>CHG-v1.1.0-17：自定义缴费对象（与类别/计价方式/计费周期的自定义入口对齐）。</summary>
        public IRelayCommand OpenCustomObjectCommand { get; }
        public IAsyncRelayCommand ConfirmCustomCommand { get; }
        public IRelayCommand CancelCustomCommand { get; }
        public IRelayCommand<ChargeItemRow> DeleteCommand { get; }
        public IAsyncRelayCommand ConfirmDeleteCommand { get; }
        public IRelayCommand CancelDeleteCommand { get; }

        private async Task InitAsync()
        {
            await RunAsync(async () =>
            {
                await EnsureDictsAsync();
                _inited = true;
                await LoadItemsCoreAsync();
                // CHG-v1.1.2-26：价目表页签与「新增项目」步骤一共享同一份收费标准缓存
                await LoadStandardsCoreAsync();
            }, "收费项目已加载");
        }

        public async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                await LoadItemsCoreAsync();
                await LoadStandardsCoreAsync();
            }, "收费项目已加载");
        }

        private async Task LoadItemsCoreAsync()
        {
            // FIX-v1.1.2-01（负责人反馈「新增项目会多出一条重复项目」）：
            // 根因＝并发加载：页面存在多处加载入口（搜索防抖、保存/删除后刷新、页签切换），
            // 原实现「先 Items.Clear() 再 await 取数」，两次加载会在清空与追加之间交错，把同一批行追加两遍。
            // 处置＝① 先取数后替换（消除空表中间态）；② 序号令牌＋写入锁，保证只有最后一次加载能写回，
            //        且「清空 + 追加」在同一临界区内原子完成（不依赖调用线程是 UI 线程）。
            int seq = ++_itemsLoadSeq;
            var list = await Api.GetChargeItemsAsync(Keyword, CategoryFilterName);
            IEnumerable<ChargeItemDto> query = list;
            if (StatusFilter == 1) { query = query.Where(x => x.Status == 0); }
            else if (StatusFilter == 2) { query = query.Where(x => x.Status != 0); }
            List<ChargeItemRow> rows = query.OrderBy(x => x.Id).Select(x => new ChargeItemRow { Dto = x }).ToList();
            lock (_itemsLoadSync)
            {
                if (seq != _itemsLoadSeq) { return; }   // 已有更新的加载在途，本次结果作废
                Items.Clear();
                foreach (ChargeItemRow row in rows)
                {
                    Items.Add(row);
                }
            }
            _isSelectAll = false;
            OnPropertyChanged(nameof(IsSelectAll));
        }

        /// <summary>刷新三组字典并尽量保持当前选择（启动/自定义字典项保存后调用）。</summary>
        private async Task EnsureDictsAsync()
        {
            var categories = await Api.GetDictItemsAsync("charge_category");
            var methods = await Api.GetDictItemsAsync("charge_method");
            var cycles = await Api.GetDictItemsAsync("charge_cycle");
            var objects = await Api.GetDictItemsAsync("charge_object");

            string filterName = CategoryFilterName;
            string formCat = _formCategory == null ? null : _formCategory.ItemName;
            string methodCode = _formMethod == null ? null : _formMethod.ItemCode;
            string methodName = _formMethod == null ? null : _formMethod.ItemName;
            string cycleName = _formCycle == null ? null : _formCycle.ItemName;
            string objectCode = _formObject == null ? null : _formObject.ItemCode;

            _isLoadingDicts = true;
            try
            {
                FilterCategories.Clear();
                FilterCategories.Add(AllFilter);
                FormCategories.Clear();
                Methods.Clear();
                Cycles.Clear();
                ChargeObjects.Clear();
                foreach (DictItemDto d in categories)
                {
                    FilterCategories.Add(d);
                    FormCategories.Add(d);
                }
                foreach (DictItemDto d in methods) { Methods.Add(d); }
                foreach (DictItemDto d in cycles) { Cycles.Add(d); }
                foreach (DictItemDto d in objects) { ChargeObjects.Add(d); }
            }
            finally
            {
                _isLoadingDicts = false;
            }

            CategoryFilter = string.IsNullOrEmpty(filterName)
                ? AllFilter
                : (FilterCategories.FirstOrDefault(x => x.ItemName == filterName) ?? AllFilter);
            FormCategory = string.IsNullOrEmpty(formCat) ? null : FormCategories.FirstOrDefault(x => x.ItemName == formCat);
            FormMethod = ResolveMethod(methodCode, methodName);
            FormCycle = ResolveCycle(cycleName);
            FormObject = ResolveObject(objectCode);
            OnPropertyChanged(nameof(PriceUnitHint));
        }

        /// <summary>CHG-v1.1.0-17：按字典编码回填缴费对象（字典项缺失时回落房产）。</summary>
        private DictItemDto ResolveObject(string objectCode)
        {
            if (!string.IsNullOrEmpty(objectCode))
            {
                DictItemDto byCode = ChargeObjects.FirstOrDefault(x => x.ItemCode == objectCode);
                if (byCode != null) { return byCode; }
            }
            return ChargeObjects.FirstOrDefault(x => x.ItemCode == "property") ?? ChargeObjects.FirstOrDefault();
        }

        private string CategoryFilterName
        {
            get { return _categoryFilter == null || _categoryFilter.Id == 0 ? null : _categoryFilter.ItemName; }
        }

        private DictItemDto ResolveMethod(string code, string name)
        {
            if (!string.IsNullOrEmpty(code))
            {
                DictItemDto byCode = Methods.FirstOrDefault(x => x.ItemCode == code);
                if (byCode != null) { return byCode; }
            }
            if (!string.IsNullOrEmpty(name))
            {
                DictItemDto byName = Methods.FirstOrDefault(x => x.ItemName == name);
                if (byName != null) { return byName; }
            }
            return null;
        }

        private DictItemDto ResolveCycle(string cycleName)
        {
            if (!string.IsNullOrEmpty(cycleName))
            {
                DictItemDto item = Cycles.FirstOrDefault(x => x.ItemName == cycleName);
                if (item != null) { return item; }
                // 历史自定义周期名不在字典中：补临时项以便编辑回填
                item = new DictItemDto
                {
                    Id = -1,
                    TypeCode = "charge_cycle",
                    ItemCode = "custom_" + cycleName,
                    ItemName = cycleName,
                    Status = DictItemStatus.Enabled
                };
                Cycles.Add(item);
                return item;
            }
            return Cycles.FirstOrDefault(x => x.ItemCode == "monthly") ?? Cycles.FirstOrDefault();
        }

        private void StartNew()
        {
            _editingId = 0;
            OnPropertyChanged(nameof(FormTitle));
            OnPropertyChanged(nameof(IsEditing));
            FormName = string.Empty;
            // CHG-v1.1.0-19：类别默认「物业费」（原为空，需用户先选类别才能保存）
            FormCategory = FormCategories.FirstOrDefault(x =>
                string.Equals(x.ItemCode, "property_fee", StringComparison.OrdinalIgnoreCase))
                ?? FormCategories.FirstOrDefault(x => x.ItemName == "物业费")
                ?? FormCategories.FirstOrDefault();
            FormMethod = Methods.FirstOrDefault();
            FormUnitPrice = 0m;
            FormCycle = Cycles.FirstOrDefault(x => x.ItemCode == "monthly") ?? Cycles.FirstOrDefault();
            FormEnabled = true;
            FormObject = ResolveObject("property");   // 默认房产，用户可按计价方式调整
            FormFormulaTemplate = FormulaTemplates.FirstOrDefault();   // 默认「按建筑面积（月缴）」
            IsFormVisible = true;
        }

        private void CancelForm()
        {
            IsFormVisible = false;
        }

        public void StartEdit(ChargeItemRow row)
        {
            if (row == null) { return; }
            _editingId = row.Id;
            OnPropertyChanged(nameof(FormTitle));
            OnPropertyChanged(nameof(IsEditing));
            FormName = row.Name;
            FormCategory = string.IsNullOrEmpty(row.Dto.Category) ? null : FormCategories.FirstOrDefault(x => x.ItemName == row.Dto.Category);
            FormMethod = ResolveMethod(row.Dto.MethodCode, row.Dto.MethodName);
            FormUnitPrice = row.Dto.UnitPrice;
            FormCycle = ResolveCycle(row.Dto.CycleType == BillingCycleType.Custom ? row.Dto.CycleName : null);
            FormEnabled = row.Dto.Status == 0;
            FormObject = ResolveObject(ResolveObjectCode(row.Dto));
            // CHG-v1.1.2-06：回显已保存的计价公式（存量项目为空 = 沿用内置口径）
            _formFormulaTemplate = null;
            OnPropertyChanged(nameof(FormFormulaTemplate));
            FormFormula = row.Dto.Formula ?? string.Empty;
            IsFormVisible = true;
        }

        /// <summary>CHG-v1.1.0-17：收费项目缴费对象编码（历史数据无 object_code 时按对象类型回落）。</summary>
        private static string ResolveObjectCode(ChargeItemDto dto)
        {
            if (dto == null) { return "property"; }
            if (!string.IsNullOrWhiteSpace(dto.ObjectCode)) { return dto.ObjectCode.Trim(); }
            switch (dto.ObjectType)
            {
                case ChargeObjectType.Parking: return "parking";
                case ChargeObjectType.Owner: return "owner";
                default: return "property";
            }
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(FormName))
            {
                ErrorText = "项目名称不能为空";
                return;
            }
            if (FormCategory == null)
            {
                ErrorText = "请选择收费项目类别";
                return;
            }
            if (FormMethod == null)
            {
                ErrorText = "请选择计价方式";
                return;
            }
            if (FormUnitPrice <= 0)
            {
                ErrorText = "单价必须大于 0";
                return;
            }
            if (FormCycle == null)
            {
                ErrorText = "请选择计费周期";
                return;
            }
            if (FormObject == null)
            {
                ErrorText = "请选择缴费对象";
                return;
            }
            string formulaError = ValidateFormula(FormFormula);
            if (formulaError != null)
            {
                ErrorText = "计价公式不合法：" + formulaError;
                return;
            }

            BillingCycleType cycleType = ResolveFormCycleType();
            if (cycleType == BillingCycleType.Custom && string.IsNullOrWhiteSpace(FormCycle.ItemName))
            {
                ErrorText = "自定义计费周期必须填写周期名称";
                return;
            }

            await RunAsync(async () =>
            {
                var request = new ChargeItemRequest
                {
                    Name = FormName.Trim(),
                    Category = FormCategory.ItemName,
                    MethodCode = FormMethod.ItemCode,
                    MethodName = FormMethod.ItemName,
                    PriceUnit = string.IsNullOrEmpty(FormMethod.Remark) ? string.Empty : FormMethod.Remark.Trim(),
                    UnitPrice = FormUnitPrice,
                    CycleType = cycleType,
                    CycleName = cycleType == BillingCycleType.Custom ? FormCycle.ItemName
                                 : (cycleType == BillingCycleType.OneTime ? "一次性" : string.Empty),
                    Status = FormEnabled ? 0 : 1,
                    PayMode = MapPayMode(cycleType),
                    // CHG-v1.1.2-06：自定义计价公式（留空则按计价方式内置口径计算）
                    Formula = string.IsNullOrWhiteSpace(FormFormula) ? null : FormFormula.Trim(),
                    // CHG-v1.1.0-16/17：缴费对象由表单显式选择（与生成账单的缴费对象一致；以字典编码为准）
                    ObjectCode = FormObject.ItemCode,
                    ObjectType = IsFixedObjectCode(FormObject.ItemCode)
                        ? (string.Equals(FormObject.ItemCode, "parking", StringComparison.OrdinalIgnoreCase) ? ChargeObjectType.Parking
                            : (string.Equals(FormObject.ItemCode, "owner", StringComparison.OrdinalIgnoreCase) ? ChargeObjectType.Owner
                                : ChargeObjectType.Property))
                        : ChargeObjectType.Custom
                };
                if (_editingId > 0)
                {
                    await Api.UpdateChargeItemAsync(_editingId, request);
                }
                else
                {
                    await Api.CreateChargeItemAsync(request);
                }
                IsFormVisible = false;
                await LoadItemsCoreAsync();
            }, _editingId > 0 ? "收费项目已更新" : "收费项目已新增");
        }

        private BillingCycleType ResolveFormCycleType()
        {
            if (FormCycle == null) { return BillingCycleType.Monthly; }
            switch (FormCycle.ItemCode)
            {
                case "monthly": return BillingCycleType.Monthly;
                case "yearly": return BillingCycleType.Yearly;
                case "onetime": return BillingCycleType.OneTime;
                default: return BillingCycleType.Custom;
            }
        }

        private static ChargePayMode MapPayMode(BillingCycleType cycleType)
        {
            switch (cycleType)
            {
                case BillingCycleType.Yearly: return ChargePayMode.Yearly;
                case BillingCycleType.Monthly: return ChargePayMode.Monthly;
                default: return ChargePayMode.Temporary;
            }
        }

        private async Task ToggleAsync(ChargeItemRow row)
        {
            if (row == null) { return; }
            await RunAsync(async () =>
            {
                await Api.UpdateChargeItemAsync(row.Id, new ChargeItemRequest
                {
                    Name = row.Dto.Name,
                    Category = row.Dto.Category,
                    MethodCode = row.Dto.MethodCode,
                    MethodName = row.Dto.MethodName,
                    PriceUnit = row.Dto.PriceUnit,
                    UnitPrice = row.Dto.UnitPrice,
                    CycleType = row.Dto.CycleType,
                    CycleName = row.Dto.CycleName,
                    ObjectCode = row.Dto.ObjectCode,
                    ObjectType = row.Dto.ObjectType,
                    PayMode = row.Dto.PayMode,
                    Status = row.Dto.Status == 0 ? 1 : 0
                });
                await LoadItemsCoreAsync();
            }, row.IsEnabled ? "收费项目已停用（不影响已出账单）" : "收费项目已启用");
        }

        private void RequestDelete(ChargeItemRow row)
        {
            if (row == null) { return; }
            ConfirmRow = row;
            IsConfirmVisible = true;
        }

        private async Task ConfirmDeleteAsync()
        {
            ChargeItemRow row = ConfirmRow;
            if (row == null) { return; }
            // CHG-v1.1.2-35：已被账单引用的收费项目不能删除 —— 服务端原因用弹窗明确告知
            await RunDeleteAsync(async () =>
            {
                await Api.DeleteChargeItemAsync(row.Id);
                ConfirmRow = null;
                IsConfirmVisible = false;
                await LoadItemsCoreAsync();
                return "收费项目已删除（软删除，历史账单与流水不受影响）";
            }, "收费项目未能删除");
        }

        private void OpenCustomDialog(string typeCode, string title, bool showRemark)
        {
            _customTypeCode = typeCode;
            CustomDialogTitle = title;
            CustomShowRemark = showRemark;
            CustomInputPlaceholder = CustomInputHint(typeCode);
            CustomInputName = string.Empty;
            CustomInputRemark = string.Empty;
            IsCustomDialogVisible = true;
        }

        /// <summary>
        /// CHG-v1.1.0-19：自定义项名称输入框提示语 —— 原统一写死「请输入名称（如：按卡）」，
        /// 在自定义类别/缴费对象场景下语境不符；现按字典类型给出对应示例。
        /// </summary>
        internal static string CustomInputHint(string typeCode)
        {
            switch (typeCode)
            {
                case "charge_object": return "如：1号楼租户 / XX广告公司";
                case "charge_category": return "如：装修管理费 / 电梯维保费";
                case "charge_cycle": return "如：每季 / 每半年";
                case "charge_method": return "如：按卡 / 按人";
                default: return "请输入名称";
            }
        }

        private void CancelCustom()
        {
            IsCustomDialogVisible = false;
        }

        private async Task ConfirmCustomAsync()
        {
            if (string.IsNullOrWhiteSpace(CustomInputName))
            {
                ErrorText = "自定义项名称不能为空";
                return;
            }
            string name = CustomInputName.Trim();
            await RunAsync(async () =>
            {
                DictItemDto created = await Api.CreateDictItemAsync(_customTypeCode, new DictItemRequest
                {
                    TypeCode = _customTypeCode,
                    ItemName = name,
                    Remark = CustomShowRemark ? (CustomInputRemark ?? string.Empty).Trim() : string.Empty
                });
                IsCustomDialogVisible = false;
                await EnsureDictsAsync();
                SelectCustomItem(_customTypeCode, created.ItemName);
            }, "自定义项已保存并同步到下拉");
        }

        private void SelectCustomItem(string typeCode, string name)
        {
            if (typeCode == "charge_category")
            {
                FormCategory = FormCategories.FirstOrDefault(x => x.ItemName == name);
            }
            else if (typeCode == "charge_method")
            {
                FormMethod = Methods.FirstOrDefault(x => x.ItemName == name);
                OnPropertyChanged(nameof(PriceUnitHint));
            }
            else if (typeCode == "charge_cycle")
            {
                FormCycle = Cycles.FirstOrDefault(x => x.ItemName == name);
            }
            else if (typeCode == "charge_object")
            {
                FormObject = ChargeObjects.FirstOrDefault(x => x.ItemName == name);
            }
        }
    }
}
