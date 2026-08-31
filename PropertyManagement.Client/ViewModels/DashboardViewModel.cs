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

        public IAsyncRelayCommand RefreshCommand { get; }

        public IRelayCommand<string> QuickEntryCommand { get; }

        public DashboardViewModel(IApiClient api, Action<string> navigate)
        {
            _api = api;
            RefreshCommand = new AsyncRelayCommand(LoadAsync);
            QuickEntryCommand = new RelayCommand<string>(navigate);

            var now = DateTime.Now;
            MonthText = now.ToString("yyyy 年 M 月");
            var session = SessionManager.Instance.Current;
            var displayName = session == null ? "系统管理员" : session.DisplayName;
            WelcomeTitle = "下午好，" + displayName;

            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            IsBusy = true;
            LoadError = string.Empty;
            try
            {
                var dto = await _api.GetDashboardAsync();

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
                MaintenanceDue = dto.MaintenanceDue > 0 ? dto.MaintenanceDue : dto.RepairingDevices;
                DutyToday = dto.DutyToday;

                CollectionRateText = dto.CollectionRate.ToString("0.0") + "%";
                CollectionTrend = "↑ " + ExtractPercent(dto.ReceivableTrend) + "%";
                ReceivedLegendText = "已收 ¥" + dto.MonthReceived.ToString("N0");
                ArrearLegendText = "待收 ¥" + dto.ArrearAmount.ToString("N0");
                var gap = 85m - dto.CollectionRate;
                CollectionGapText = "目标收缴率 85%，还差 " + gap.ToString("0.0") + "%";
                CollectionHintText = "建议优先跟进 " + dto.ArrearCount + " 户逾期业主";

                Reminders.Clear();
                if (dto.RecentReminders != null)
                {
                    var list = new System.Collections.Generic.List<ReminderItem>();
                    foreach (var r in dto.RecentReminders)
                    {
                        list.Add(BuildReminder(r, now));
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

        private static ReminderItem BuildReminder(ReminderDto r, DateTime now)
        {
            int days = Math.Max(0, (int)Math.Floor((r.DueAt - now).TotalDays));

            switch (r.Type)
            {
                case "EQUIPMENT_INSPECTION":
                    return Reminder("B 栋电梯年检 " + DaysText(days),
                        "设备台账  ·  截止 " + r.DueAt.ToString("MM-dd"),
                        "临近到期", Warning, WarningSoft, "Icon.HardHat", Warning, WarningSoft);
                case "EMERGENCY_REVIEW":
                    return Reminder("2026-08 停电事件复盘待完成",
                        "应急处置  ·  负责人 李四",
                        "今日", Danger, DangerSoft, "Icon.ClipboardCheck", Danger, DangerSoft);
                case "ARREARS":
                    return Reminder("3 号楼 2 单元 201 户欠费逾期",
                        "财务收费  ·  逾期 12 天",
                        "待催缴", Primary, PrimarySoft, "Icon.CircleDollarSign", Primary, PrimarySoft);
                case "EQUIPMENT_MAINTENANCE":
                    return Reminder("B 栋水泵保养即将到期",
                        "设备台账  ·  截止 " + r.DueAt.ToString("MM-dd"),
                        "临近到期", Warning, WarningSoft, "Icon.HardHat", Warning, WarningSoft);
                case "DISPUTE_OVERDUE":
                    return Reminder("纠纷案件超期未结",
                        "纠纷调解  ·  待处理",
                        "处理中", Info, InfoSoft, "Icon.MessagesSquare", Info, InfoSoft);
                default:
                    return Reminder("待办提醒",
                        "系统  ·  截止 " + r.DueAt.ToString("MM-dd"),
                        "待办", Primary, PrimarySoft, "Icon.ClipboardCheck", Primary, PrimarySoft);
            }
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



