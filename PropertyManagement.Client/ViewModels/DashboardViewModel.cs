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

    /// <summary>仪表盘月份下拉候选项（R17）。</summary>
    public class MonthOption
    {
        public string Period { get; set; }

        public string Text { get; set; }
    }

    /// <summary>仪表盘首页（PG-DASH，UC-COM-006，按原型 §2.3 一比一修版）。</summary>
    public class DashboardViewModel : ObservableObject
    {
        private static readonly Brush Primary = Brush("#2B7DE9");
        private static readonly Brush PrimarySoft = Brush("#EAF3FF");
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
        private string _collectionTrend = "↑ 0%";
        private string _receivedLegendText = "已收 ¥0";
        private string _arrearLegendText = "待收 ¥0";
        private string _collectionGapText = "目标收缴率 85%，还差 0%";
        private string _collectionHintText = "建议优先跟进 0 户逾期业主";

        // R17：月份选择器 + 待办跳转
        private bool _isMonthPickerOpen;
        private string _selectedPeriod;
        private readonly Action<string, string, string> _openPage;
        private readonly Action _openTodoCenter;
        private readonly Func<string> _userNameProvider;

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

        /// <summary>点击待办项 → 跳转对应模块页（R17）。</summary>
        public IRelayCommand<ReminderItem> OpenTodoCommand { get; }

        /// <summary>「查看全部」→ 打开顶部铃铛待办中心（R17）。</summary>
        public IRelayCommand ViewAllTodosCommand { get; }

        public DashboardViewModel(IApiClient api, Action<string> navigate,
            Action<string, string, string> openPage = null, Action openTodoCenter = null,
            Func<string> userNameProvider = null)
        {
            _api = api;
            _openPage = openPage;
            _openTodoCenter = openTodoCenter;
            _userNameProvider = userNameProvider;
            RefreshCommand = new AsyncRelayCommand(LoadAsync);
            QuickEntryCommand = new RelayCommand<string>(navigate);
            ToggleMonthPickerCommand = new RelayCommand(() => IsMonthPickerOpen = !IsMonthPickerOpen);
            SelectMonthCommand = new AsyncRelayCommand<MonthOption>(SelectMonthAsync);
            OpenTodoCommand = new RelayCommand<ReminderItem>(OpenTodo);
            ViewAllTodosCommand = new RelayCommand(OpenTodoCenter);

            var now = DateTime.Now;
            MonthText = now.ToString("yyyy 年 M 月");
            SelectedPeriod = now.ToString("yyyy-MM");
            ApplyPeriodLabels(now);
            BuildMonths(now);
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

        private void BuildMonths(DateTime now)
        {
            Months.Clear();
            var cursor = new DateTime(now.Year, now.Month, 1);
            for (int i = 0; i < 24; i++)
            {
                Months.Add(new MonthOption
                {
                    Period = cursor.ToString("yyyy-MM"),
                    Text = cursor.ToString("yyyy 年 M 月")
                });
                cursor = cursor.AddMonths(-1);
            }
        }

        private async Task SelectMonthAsync(MonthOption option)
        {
            if (option == null)
            {
                return;
            }

            SelectedPeriod = option.Period;
            MonthText = option.Text;
            ApplyPeriodLabels(ParsePeriod(option.Period));
            IsMonthPickerOpen = false;
            await LoadAsync();
        }

        /// <summary>按所选月份刷新口径标签（当前月显示"本月"，历史月份显示月份前缀）。</summary>
        private void ApplyPeriodLabels(DateTime period)
        {
            var today = DateTime.Today;
            bool isCurrentMonth = period.Year == today.Year && period.Month == today.Month;
            PeriodLabelText = isCurrentMonth
                ? "本月"
                : (period.Year == today.Year ? period.Month + " 月" : period.ToString("yyyy-MM"));
            MonthShortText = isCurrentMonth
                ? period.ToString("MM") + " 月"
                : (period.Year == today.Year ? period.Month + " 月" : period.ToString("yyyy-MM"));
        }

        private static DateTime ParsePeriod(string period)
        {
            DateTime parsed;
            return !string.IsNullOrWhiteSpace(period) &&
                   DateTime.TryParseExact(period.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
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

                var now = DateTime.Now;
                WelcomeDate = now.ToString("yyyy 年 M 月 d 日  ·  ", CultureInfo.GetCultureInfo("zh-CN"))
                              + WeekdayOf(now)
                              + "  ·  今日共有 " + dto.PendingReminders + " 项待办";

                MonthReceivableText = "¥ " + dto.MonthReceivable.ToString("N0");
                ReceivableTrend = dto.ReceivableTrend ?? "较上月 +0%";
                MonthReceivedText = "¥ " + dto.MonthReceived.ToString("N0");
                ReceivedTrend = dto.ReceivedTrend ?? "收缴率 " + dto.CollectionRate.ToString("0.0") + "%";
                ArrearAmountText = "¥ " + dto.ArrearAmount.ToString("N0");
                ArrearTrend = "涉及 " + dto.ArrearCount + " 户";
                OverdueCountText = dto.ArrearCount + " 户";
                OverdueTrend = dto.OverdueTrend ?? "较上月 0 户";

                PendingReminders = dto.PendingReminders;
                HandlingEmergency = dto.HandlingEmergency;
                HandlingDisputes = dto.HandlingDisputes;
                MaintenanceDue = dto.MaintenanceDue;
                DutyToday = dto.DutyToday;

                CollectionRateText = dto.CollectionRate.ToString("0.0") + "%";
                CollectionTrend = "↑ " + ExtractPercent(dto.ReceivableTrend) + "%";
                ReceivedLegendText = "已收 ¥" + dto.MonthReceived.ToString("N0");
                ArrearLegendText = "待收 ¥" + dto.ArrearAmount.ToString("N0");
                var gap = 85m - dto.CollectionRate;
                CollectionGapText = "目标收缴率 85%，还差 " + gap.ToString("0.0") + "%";
                CollectionHintText = "建议优先跟进 " + dto.ArrearCount + " 户逾期业主";

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
