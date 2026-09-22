using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Common;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>待办列表项（仪表盘，原型 PG-DASH 下半区）。</summary>
    public class ReminderItem
    {
        public string Title { get; set; }

        public string Meta { get; set; }

        public string Tag { get; set; }

        public string IconKey { get; set; }

        public Brush IconForeground { get; set; }

        public Brush IconBackground { get; set; }

        public Brush TagForeground { get; set; }

        public Brush TagBackground { get; set; }

        /// <summary>是否最后一条（原型仅前两条有分隔线）。</summary>
        public bool IsLast { get; set; }

        /// <summary>R17：来源待办（携带跳转目标 模块/页面/id）。</summary>
        public TodoItemDto Source { get; set; }
    }

    /// <summary>
    /// 仪表盘月份下拉候选项（R17）。
    /// CHG-v1.2.0-21：月份列表按「选中的年份」重建（原来只有近 24 个月一长条，无法直接选年份），
    /// 因此需要 IsSelected / IsCurrent 参与高亮，改为可观察对象。
    /// </summary>
    public class MonthOption : ObservableObject
    {
        public string Period { get; set; }

        public string Text { get; set; }

        private bool _isSelected;

        /// <summary>是否为当前统计口径月份（下拉里高亮）。</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        /// <summary>是否为系统当前自然月（用于「本月」标记）。</summary>
        public bool IsCurrent { get; set; }
    }

    /// <summary>仪表盘首页（PG-DASH，UC-COM-006，按原型 §2.3 一比一修版）。</summary>
    public class DashboardViewModel : ObservableObject
    {
        /// <summary>
        /// CHG-v1.1.2-56：收缴率目标（原型 PG-DASH「本月收缴概览」口径，固定 85%）。
        /// 仅用于「已达成 / 还差 N 个百分点」文案，不参与任何计算。
        /// </summary>
        private const decimal CollectionTargetRate = 85m;

        private static readonly Brush Primary = Brush("#1F4B43");
        private static readonly Brush PrimarySoft = Brush("#E9F0EE");
        private static readonly Brush Success = Brush("#12805C");
        private static readonly Brush SuccessSoft = Brush("#E8F7F1");
        private static readonly Brush Warning = Brush("#B76E00");
        private static readonly Brush WarningSoft = Brush("#FFF5DC");
        private static readonly Brush Danger = Brush("#D64545");
        private static readonly Brush DangerSoft = Brush("#FDECEC");
        private static readonly Brush Info = Brush("#7A5AF8");
        private static readonly Brush InfoSoft = Brush("#F1EDFF");

        private readonly IApiClient _api;
        private bool _isBusy = true;
        private string _loadError = string.Empty;

        // 欢迎区
        private string _welcomeTitle = "下午好，系统管理员";
        private string _welcomeDate = string.Empty;
        private string _monthText = string.Empty;
        private string _monthShortText = string.Empty;
        private string _periodLabelText = "本月";

        // 财务指标行
        private string _monthReceivableText = "¥ 0";
        private string _receivableTrend = "较上月 0%";
        private string _monthReceivedText = "¥ 0";
        private string _receivedTrend = "收缴率 0%";
        private string _arrearAmountText = "¥ 0";
        private string _arrearTrend = "涉及 0 户";
        private string _overdueCountText = "0 户";
        private string _overdueTrend = "较上月 0 户";

        // 运营指标行
        private int _pendingReminders;

        private int _handlingEmergency;
        private int _handlingDisputes;
        private int _maintenanceDue;
        private int _dutyToday;

        // 收缴概览
        private string _collectionRateText = "0%";
        private double _collectionRateValue;
        private string _collectionTrend = "↑ 0%";
        private string _receivedLegendText = "已收 ¥0";
        private string _arrearLegendText = "待收 ¥0";
        private string _collectionGapText = "目标收缴率 85%，还差 0%";
        private string _collectionHintText = "建议优先跟进 0 户逾期业主";

        // R17：月份选择器 + 待办跳转
        private bool _isMonthPickerOpen;
        private string _selectedPeriod;
        private int _pickerYear;
        private bool _isAnnualPeriod;
        private readonly Action<string> _navigate;
        private readonly Action<string, string, string> _openPage;
        private readonly Action _openTodoCenter;
        private readonly Func<string> _userNameProvider;
        /// <summary>CHG-v1.2.0-28：统计口径变化回调（由 ShellViewModel 持有，用于跨页面记住所选月/年）。</summary>
        private readonly Action<string> _periodChanged;

        public string WelcomeTitle
        {
            get { return _welcomeTitle; }
            private set { SetProperty(ref _welcomeTitle, value); }
        }

        public string WelcomeDate
        {
            get { return _welcomeDate; }
            private set { SetProperty(ref _welcomeDate, value); }
        }

        public string MonthText
        {
            get { return _monthText; }
            private set { SetProperty(ref _monthText, value); }
        }

        /// <summary>
        /// CHG-v1.2.0-26：当前统计口径是否为「按年」（true = 年度应收/已收/收缴率，false = 月度）。
        /// 卡片文案（本月/本年、较上月/较上年）与弹层「全年」格高亮均据此切换。
        /// </summary>
        public bool IsAnnualPeriod
        {
            get { return _isAnnualPeriod; }
            private set { SetProperty(ref _isAnnualPeriod, value); }
        }

        /// <summary>收缴概览角标（当前月 "08 月"；同年历史月 "8 月"；跨年份 "2026-08"）。</summary>
        public string MonthShortText
        {
            get { return _monthShortText; }
            private set { SetProperty(ref _monthShortText, value); }
        }

        /// <summary>口径前缀（当前月="本月"，历史月="8 月"/"2026-08"）。</summary>
        public string PeriodLabelText
        {
            get { return _periodLabelText; }
            private set
            {
                if (SetProperty(ref _periodLabelText, value))
                {
                    OnPropertyChanged(nameof(ReceivableLabelText));
                    OnPropertyChanged(nameof(ReceivedLabelText));
                    OnPropertyChanged(nameof(CollectionOverviewTitle));
                }
            }
        }

        public string ReceivableLabelText
        {
            get { return PeriodLabelText + "应收"; }
        }

        public string ReceivedLabelText
        {
            get { return PeriodLabelText + "已收"; }
        }

        public string CollectionOverviewTitle
        {
            get { return PeriodLabelText + "收缴概览"; }
        }

        public string MonthReceivableText
        {
            get { return _monthReceivableText; }
            private set { SetProperty(ref _monthReceivableText, value); }
        }

        public string ReceivableTrend
        {
            get { return _receivableTrend; }
            private set { SetProperty(ref _receivableTrend, value); }
        }

        public string MonthReceivedText
        {
            get { return _monthReceivedText; }
            private set { SetProperty(ref _monthReceivedText, value); }
        }

        public string ReceivedTrend
        {
            get { return _receivedTrend; }
            private set { SetProperty(ref _receivedTrend, value); }
        }

        public string ArrearAmountText
        {
            get { return _arrearAmountText; }
            private set { SetProperty(ref _arrearAmountText, value); }
        }

        public string ArrearTrend
        {
            get { return _arrearTrend; }
            private set { SetProperty(ref _arrearTrend, value); }
        }

        public string OverdueCountText
        {
            get { return _overdueCountText; }
            private set { SetProperty(ref _overdueCountText, value); }
        }

        public string OverdueTrend
        {
            get { return _overdueTrend; }
            private set { SetProperty(ref _overdueTrend, value); }
        }

        public int PendingReminders
        {
            get { return _pendingReminders; }
            private set { SetProperty(ref _pendingReminders, value); }
        }

        public int HandlingEmergency
        {
            get { return _handlingEmergency; }
            private set { SetProperty(ref _handlingEmergency, value); }
        }

        public int HandlingDisputes
        {
            get { return _handlingDisputes; }
            private set { SetProperty(ref _handlingDisputes, value); }
        }

        public int MaintenanceDue
        {
            get { return _maintenanceDue; }
            private set { SetProperty(ref _maintenanceDue, value); }
        }

        public int DutyToday
        {
            get { return _dutyToday; }
            private set { SetProperty(ref _dutyToday, value); }
        }

        public string CollectionRateText
        {
            get { return _collectionRateText; }
            private set { SetProperty(ref _collectionRateText, value); }
        }

        /// <summary>
        /// CHG-v1.1.2-56：收缴率数值（0–100，供进度条按比例显示）。
        /// 原实现进度条填充宽度是**写死的 245px**，与收缴率无关（100% 和 30% 画出来一样长）。
        /// </summary>
        public double CollectionRateValue
        {
            get { return _collectionRateValue; }
            private set { SetProperty(ref _collectionRateValue, value); }
        }

        public string CollectionTrend
        {
            get { return _collectionTrend; }
            private set { SetProperty(ref _collectionTrend, value); }
        }

        public string ReceivedLegendText
        {
            get { return _receivedLegendText; }
            private set { SetProperty(ref _receivedLegendText, value); }
        }

        public string ArrearLegendText
        {
            get { return _arrearLegendText; }
            private set { SetProperty(ref _arrearLegendText, value); }
        }

        public string CollectionGapText
        {
            get { return _collectionGapText; }
            private set { SetProperty(ref _collectionGapText, value); }
        }

        public string CollectionHintText
        {
            get { return _collectionHintText; }
            private set { SetProperty(ref _collectionHintText, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set { SetProperty(ref _isBusy, value); }
        }

        public string LoadError
        {
            get { return _loadError; }
            private set { SetProperty(ref _loadError, value); }
        }

        public ObservableCollection<ReminderItem> Reminders { get; } = new ObservableCollection<ReminderItem>();

        /// <summary>月份下拉候选（近 24 个月，倒序）。</summary>
        public ObservableCollection<MonthOption> Months { get; } = new ObservableCollection<MonthOption>();

        public bool IsMonthPickerOpen
        {
            get { return _isMonthPickerOpen; }
            set { SetProperty(ref _isMonthPickerOpen, value); }
        }

        /// <summary>CHG-v1.2.0-21：月份下拉里当前展示的年份（可上下翻年）。</summary>
        public int PickerYear
        {
            get { return _pickerYear; }
            private set
            {
                if (SetProperty(ref _pickerYear, value))
                {
                    OnPropertyChanged(nameof(PickerYearText));
                    OnPropertyChanged(nameof(CanPickNextYear));
                }
            }
        }

        public string PickerYearText
        {
            get { return _pickerYear + " 年"; }
        }

        /// <summary>不允许翻到未来年份（统计口径无意义）。</summary>
        public bool CanPickNextYear
        {
            get { return _pickerYear < DateTime.Today.Year; }
        }

        /// <summary>当前统计口径月份（yyyy-MM）。</summary>
        public string SelectedPeriod
        {
            get { return _selectedPeriod; }
            private set { SetProperty(ref _selectedPeriod, value); }
        }

        public IAsyncRelayCommand RefreshCommand { get; }

        public IRelayCommand<string> QuickEntryCommand { get; }

        public IRelayCommand ToggleMonthPickerCommand { get; }

        public IAsyncRelayCommand<MonthOption> SelectMonthCommand { get; }

        /// <summary>CHG-v1.2.0-21：月份下拉内 < / > 翻年。</summary>
        public IRelayCommand<string> ShiftPickerYearCommand { get; }

        /// <summary>CHG-v1.2.0-26：选「全年」→ 切换为**年度**统计口径。</summary>
        public IAsyncRelayCommand SelectAnnualCommand { get; }

        /// <summary>CHG-v1.2.0-21：「回到本月」。</summary>
        public IAsyncRelayCommand BackToCurrentMonthCommand { get; }

        /// <summary>点击待办项 → 跳转对应模块页（R17）。</summary>
        public IRelayCommand<ReminderItem> OpenTodoCommand { get; }

        /// <summary>「查看全部」→ 打开顶部铃铛待办中心（R17）。</summary>
        public IRelayCommand ViewAllTodosCommand { get; }

        /// <param name="initialPeriod">
        /// CHG-v1.2.0-28：进入仪表盘时的初始统计口径（`yyyy-MM` 或 `yyyy`）—— 由 ShellViewModel 传入上次选择，
        /// 为空 = 当前月。原实现每次进入都重置为当前月，「切页返回就跳回当天数据」。
        /// </param>
        /// <param name="periodChanged">口径变化回调（ShellViewModel 记录，供下次进入仪表盘复用）。</param>
        public DashboardViewModel(IApiClient api, Action<string> navigate,
            Action<string, string, string> openPage = null, Action openTodoCenter = null,
            Func<string> userNameProvider = null, string initialPeriod = null,
            Action<string> periodChanged = null)
        {
            _api = api;
            _navigate = navigate;
            _openPage = openPage;
            _openTodoCenter = openTodoCenter;
            _userNameProvider = userNameProvider;
            _periodChanged = periodChanged;
            RefreshCommand = new AsyncRelayCommand(LoadAsync);
            QuickEntryCommand = new RelayCommand<string>(QuickEntry);
            ToggleMonthPickerCommand = new RelayCommand(() => IsMonthPickerOpen = !IsMonthPickerOpen);
            SelectMonthCommand = new AsyncRelayCommand<MonthOption>(SelectMonthAsync);
            ShiftPickerYearCommand = new RelayCommand<string>(ShiftPickerYear);
            SelectAnnualCommand = new AsyncRelayCommand(SelectAnnualAsync);
            BackToCurrentMonthCommand = new AsyncRelayCommand(BackToCurrentMonthAsync);
            OpenTodoCommand = new RelayCommand<ReminderItem>(OpenTodo);
            ViewAllTodosCommand = new RelayCommand(OpenTodoCenter);

            var now = DateTime.Now;
            // CHG-v1.2.0-28：优先沿用上次选择的口径（跨页面持久化），否则回到当前月
            string start = string.IsNullOrWhiteSpace(initialPeriod) ? now.ToString("yyyy-MM") : initialPeriod.Trim();
            bool startAnnual = start.Length == 4;
            DateTime startBasis = ParsePeriod(start, startAnnual);
            SelectedPeriod = start;
            IsAnnualPeriod = startAnnual;
            MonthText = startAnnual ? startBasis.Year + " 年（全年）" : startBasis.ToString("yyyy 年 M 月");
            ApplyPeriodLabels(startBasis, startAnnual);
            PickerYear = startBasis.Year;
            BuildMonths(startBasis.Year);
            ApplyGreeting();

            _ = LoadAsync();
        }

        /// <summary>按当前时段生成问候语前缀：上午好 / 中午好 / 下午好 / 晚上好。</summary>
        private static string GreetingPrefix(DateTime now)
        {
            int hour = now.Hour;
            if (hour >= 5 && hour < 11)
            {
                return "上午好";
            }
            if (hour >= 11 && hour < 13)
            {
                return "中午好";
            }
            if (hour >= 13 && hour < 18)
            {
                return "下午好";
            }
            return "晚上好";
        }

        /// <summary>
        /// 问候语 = 时段问候 + 个人信息中的名字（未填写时回退登录账号）。
        /// 格式：上午好，张伟（R18）。
        /// </summary>
        private void ApplyGreeting()
        {
            string name = _userNameProvider == null ? null : _userNameProvider();
            if (string.IsNullOrWhiteSpace(name))
            {
                var session = SessionManager.Instance.Current;
                name = session == null ? "系统管理员" : session.DisplayName;
            }

            WelcomeTitle = GreetingPrefix(DateTime.Now) + "，" + name;
        }

        /// <summary>个人信息保存后刷新问候语（由 ShellViewModel 调用）。</summary>
        public void RefreshGreeting()
        {
            ApplyGreeting();
        }

        /// <summary>
        /// CHG-v1.2.0-21：按「年份」重建 12 个月份候选 —— 原实现是滚动近 24 个月的长列表，
        /// 只能逐个月份下拉、不能直接选年份；现改为「年份可翻 + 12 个月网格」。
        /// </summary>
        private void BuildMonths(int year)
        {
            var today = DateTime.Today;
            Months.Clear();
            for (int month = 1; month <= 12; month++)
            {
                string period = new DateTime(year, month, 1).ToString("yyyy-MM");
                Months.Add(new MonthOption
                {
                    Period = period,
                    Text = month + " 月",
                    IsCurrent = year == today.Year && month == today.Month,
                    IsSelected = string.Equals(period, SelectedPeriod, StringComparison.Ordinal)
                });
            }
        }

        /// <summary>翻年（delta = -1 上一年 / +1 下一年）；不允许翻到未来年份。</summary>
        private void ShiftPickerYear(string delta)
        {
            int step;
            if (!int.TryParse(delta, out step) || step == 0)
            {
                step = 1;
            }

            int target = PickerYear + Math.Sign(step);
            if (target > DateTime.Today.Year)
            {
                target = DateTime.Today.Year;
            }
            if (target < 1970)
            {
                target = 1970;
            }
            if (target == PickerYear)
            {
                return;
            }

            PickerYear = target;
            BuildMonths(target);
        }

        private async Task BackToCurrentMonthAsync()
        {
            var today = DateTime.Today;
            PickerYear = today.Year;
            BuildMonths(today.Year);
            await ApplyPeriodAsync(today.Year + "-" + today.Month.ToString("D2"), false);
        }

        /// <summary>
        /// 切换统计口径（下拉里点月份 / 点「全年」/「回到本月」共用）。
        /// CHG-v1.2.0-26：annual = true 时 period 为 `yyyy`（**年度**应收/已收/收缴率），false 为 `yyyy-MM`。
        /// </summary>
        private async Task ApplyPeriodAsync(string period, bool annual)
        {
            SelectedPeriod = period;
            IsAnnualPeriod = annual;
            DateTime basis = ParsePeriod(period, annual);
            MonthText = annual ? basis.Year + " 年（全年）" : basis.ToString("yyyy 年 M 月");
            ApplyPeriodLabels(basis, annual);
            foreach (MonthOption option in Months)
            {
                option.IsSelected = !annual && string.Equals(option.Period, period, StringComparison.Ordinal);
            }
            IsMonthPickerOpen = false;
            // CHG-v1.2.0-28：把所选口径交给 ShellViewModel 记住（切页返回后仍显示该月/年，直到点「回到本月」）
            if (_periodChanged != null)
            {
                _periodChanged(period);
            }
            await LoadAsync();
        }

        private async Task SelectMonthAsync(MonthOption option)
        {
            if (option == null)
            {
                return;
            }

            await ApplyPeriodAsync(option.Period, false);
        }

        /// <summary>CHG-v1.2.0-26：选「全年」→ 年度口径（统计年度应收/已收/收缴率）。</summary>
        private async Task SelectAnnualAsync()
        {
            await ApplyPeriodAsync(PickerYear.ToString(CultureInfo.InvariantCulture), true);
        }

        /// <summary>
        /// CHG-v1.2.0-22：仪表盘「快捷入口」按「模块｜页面」精确跳转。
        /// 原实现只传模块 key（如 finance），落到该模块的**首个子页**（收费项目维护）——
        /// 于是「收款登记」「生成账单」都跳错页。传「模块｜页面」时走页级跳转，只传模块时保持原行为。
        /// </summary>
        private void QuickEntry(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            string raw = key.Trim();
            int bar = raw.IndexOf('|');
            if (bar > 0 && bar < raw.Length - 1 && _openPage != null)
            {
                _openPage(raw.Substring(0, bar).Trim(), raw.Substring(bar + 1).Trim(), null);
                return;
            }

            if (_navigate != null)
            {
                _navigate(raw);
            }
        }

        /// <summary>
        /// 按所选周期刷新口径标签：当前月/当前年显示「本月」「本年」，历史周期显示周期前缀。
        /// CHG-v1.2.0-26：年度口径前缀为「本年」/「2025 年」，月度口径沿用「本月」/「8 月」/「2025-08」。
        /// </summary>
        private void ApplyPeriodLabels(DateTime period, bool annual)
        {
            var today = DateTime.Today;
            if (annual)
            {
                bool isCurrentYear = period.Year == today.Year;
                PeriodLabelText = isCurrentYear ? "本年" : period.Year + " 年";
                MonthShortText = period.Year + " 年";
                return;
            }

            bool isCurrentMonth = period.Year == today.Year && period.Month == today.Month;
            PeriodLabelText = isCurrentMonth
                ? "本月"
                : (period.Year == today.Year ? period.Month + " 月" : period.ToString("yyyy-MM"));
            MonthShortText = isCurrentMonth
                ? period.ToString("MM") + " 月"
                : (period.Year == today.Year ? period.Month + " 月" : period.ToString("yyyy-MM"));
        }

        /// <summary>解析统计周期：年度 = `yyyy`（返回当年 1 月），月度 = `yyyy-MM`；非法回退当前月。</summary>
        private static DateTime ParsePeriod(string period, bool annual)
        {
            string raw = (period ?? string.Empty).Trim();
            int year;
            if (annual && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out year) &&
                year >= 1900 && year <= 2999)
            {
                return new DateTime(year, 1, 1);
            }

            DateTime parsed;
            return raw.Length > 0 &&
                   DateTime.TryParseExact(raw + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                       DateTimeStyles.None, out parsed)
                ? parsed
                : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        }

        private void OpenTodo(ReminderItem item)
        {
            if (item == null || item.Source == null)
            {
                return;
            }
            if (_openPage != null)
            {
                _openPage(item.Source.TargetModule, item.Source.TargetPage, null);
            }
        }

        private void OpenTodoCenter()
        {
            if (_openTodoCenter != null)
            {
                _openTodoCenter();
            }
        }

        private async Task LoadAsync()
        {
            IsBusy = true;
            LoadError = string.Empty;
            try
            {
                var dto = await _api.GetDashboardAsync(SelectedPeriod);

                // CHG-v1.2.0-26：以服务端返回的粒度为准（年度口径 = 本年应收/已收/收缴率），
                // 并据此把「本月/较上月」类文案切换为「本年/较上年」。
                bool annual = dto.Annual;
                IsAnnualPeriod = annual;
                if (annual)
                {
                    DateTime yearBasis = ParsePeriod(dto.Period, true);
                    MonthText = yearBasis.Year + " 年（全年）";
                    ApplyPeriodLabels(yearBasis, true);
                    PickerYear = yearBasis.Year;
                }
                else
                {
                    DateTime monthBasis = ParsePeriod(dto.Period, false);
                    MonthText = monthBasis.ToString("yyyy 年 M 月");
                    ApplyPeriodLabels(monthBasis, false);
                    PickerYear = monthBasis.Year;
                }
                BuildMonths(PickerYear);
                string periodWord = annual ? "本年" : "本月";

                var now = DateTime.Now;
                WelcomeDate = now.ToString("yyyy 年 M 月 d 日  ·  ", CultureInfo.GetCultureInfo("zh-CN"))
                              + WeekdayOf(now)
                              + "  ·  今日共有 " + dto.PendingReminders + " 项待办";

                // CHG-v1.1.2-55：金额一律保留 2 位小数（与收款登记/账单/台账/报表导出统一）；
                // 原 N0 会把 1,143.80 与 1,143.45 各四舍五入成 1,144 与 1,143，放大成「相差 1 元」的假象。
                MonthReceivableText = "¥ " + dto.MonthReceivable.ToString("N2");
                ReceivableTrend = dto.ReceivableTrend ?? (annual ? "较上年 +0%" : "较上月 +0%");
                MonthReceivedText = "¥ " + dto.MonthReceived.ToString("N2");
                ReceivedTrend = dto.ReceivedTrend ?? "收缴率 " + dto.CollectionRate.ToString("0.0") + "%";
                ArrearAmountText = "¥ " + dto.ArrearAmount.ToString("N2");
                ArrearTrend = "涉及 " + dto.ArrearCount + " 户";
                OverdueCountText = dto.ArrearCount + " 户";
                OverdueTrend = dto.OverdueTrend ?? (annual ? "较上年 0 户" : "较上月 0 户");

                PendingReminders = dto.PendingReminders;
                HandlingEmergency = dto.HandlingEmergency;
                HandlingDisputes = dto.HandlingDisputes;
                MaintenanceDue = dto.MaintenanceDue;
                DutyToday = dto.DutyToday;

                // CHG-v1.1.2-56：本期无账期账单时不显示「0.0% + 还差 85 个百分点」这类无意义口径
                bool hasCollectionScope = dto.MonthReceivable > 0m;
                CollectionRateText = hasCollectionScope ? dto.CollectionRate.ToString("0.0") + "%" : "—";
                CollectionRateValue = hasCollectionScope ? (double)dto.CollectionRate : 0d;
                // CHG-v1.1.2-55：收缴率＝本期账期账单「已收/应收」（同源，≤100%）；
                // 环比改用服务端给出的百分点差（原实现借用了「应收」环比，语义不对）。
                CollectionTrend = string.IsNullOrWhiteSpace(dto.CollectionRateTrend)
                    ? periodWord + "账期账单清缴率"
                    : dto.CollectionRateTrend;
                ReceivedLegendText = periodWord + "账期已收 ¥" + dto.MonthCycleReceived.ToString("N2");
                ArrearLegendText = periodWord + "账期待收 ¥" +
                    (dto.MonthReceivable - dto.MonthCycleReceived).ToString("N2");
                // 目标对比文案：达标显示「已达成（超额 N 个百分点）」，未达标显示「还差 N 个百分点」。
                // 原实现固定算 85 - 收缴率，收缴率 100% 时输出「还差 -15.0%」（负数 + 用 % 表述百分点差）。
                string target = CollectionTargetRate.ToString("0.#");
                if (!hasCollectionScope)
                {
                    CollectionGapText = periodWord + "暂无账期账单，无需考核收缴率";
                }
                else if (dto.CollectionRate >= CollectionTargetRate)
                {
                    CollectionGapText = "目标收缴率 " + target + "%，已达成（超额 " +
                        (dto.CollectionRate - CollectionTargetRate).ToString("0.0") + " 个百分点）";
                }
                else
                {
                    CollectionGapText = "目标收缴率 " + target + "%，还差 " +
                        (CollectionTargetRate - dto.CollectionRate).ToString("0.0") + " 个百分点";
                }
                CollectionHintText = dto.ArrearCount > 0
                    ? "建议优先跟进 " + dto.ArrearCount + " 户逾期业主"
                    : periodWord + "无逾期业主，保持常规跟进";

                Reminders.Clear();
                if (dto.Todos != null)
                {
                    var list = new System.Collections.Generic.List<ReminderItem>();
                    foreach (var todo in dto.Todos)
                    {
                        list.Add(BuildTodoItem(todo));
                    }
                    // 原型面板展示 3 条；总数徽标仍取 dto.PendingReminders
                    int shown = Math.Min(3, list.Count);
                    for (int i = 0; i < shown; i++)
                    {
                        list[i].IsLast = i == shown - 1;
                        Reminders.Add(list[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                LoadError = "统计数据加载失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>待办项 → 面板展示模型（R17：统一由服务端待办中心投影，替换原先按字符串猜类型的分支）。</summary>
        private static ReminderItem BuildTodoItem(TodoItemDto todo)
        {
            Brush tagForeground;
            Brush tagBackground;
            switch (todo.Level)
            {
                case "danger":
                    tagForeground = Danger; tagBackground = DangerSoft; break;
                case "warning":
                    tagForeground = Warning; tagBackground = WarningSoft; break;
                case "info":
                    tagForeground = Info; tagBackground = InfoSoft; break;
                default:
                    tagForeground = Primary; tagBackground = PrimarySoft; break;
            }

            string iconKey;
            switch (todo.Kind)
            {
                case "maintenance": iconKey = "Icon.HardHat"; break;
                case "arrears": iconKey = "Icon.CircleDollarSign"; break;
                case "dispute": iconKey = "Icon.MessagesSquare"; break;
                default: iconKey = "Icon.ClipboardCheck"; break;
            }

            return new ReminderItem
            {
                Title = todo.Title,
                Meta = todo.Meta,
                Tag = todo.Tag,
                IsLast = false,
                IconKey = iconKey,
                IconForeground = tagForeground,
                IconBackground = tagBackground,
                TagForeground = tagForeground,
                TagBackground = tagBackground,
                Source = todo
            };
        }

        private static string ExtractPercent(string trend)
        {
            if (!string.IsNullOrEmpty(trend))
            {
                var match = Regex.Match(trend, @"([+-]?\d+(?:\.\d+)?)%");
                if (match.Success)
                {
                    return match.Groups[1].Value.TrimStart('+');
                }
            }
            return "0";
        }

        private static string DaysText(int days)
        {
            return days <= 0 ? "今日到期" : days + " 天后到期";
        }

        private static string WeekdayOf(DateTime d)
        {
            string[] names = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
            return names[(int)d.DayOfWeek];
        }

        private static Brush Brush(string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }

        private static ReminderItem Reminder(string title, string meta, string tag,
            Brush tagFg, Brush tagBg, string iconKey, Brush iconFg, Brush iconBg)
        {
            return new ReminderItem
            {
                Title = title,
                Meta = meta,
                Tag = tag,
                TagForeground = tagFg,
                TagBackground = tagBg,
                IconKey = iconKey,
                IconForeground = iconFg,
                IconBackground = iconBg
            };
        }
    }
}
