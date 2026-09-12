// ── 已下线（R4 变更）：应急处置子系统仅保留「场景与步骤维护」，应急发起模块停止提供 ──
// 本文件为源码存档，已从 PropertyManagement.Client.csproj 移除，不参与编译；
// 恢复上线需同步恢复 ShellViewModel 导航、MainWindow DataTemplate 与工程项。
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Emergency;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>发起页场景卡行（选中态 + 卡脚文案）。</summary>
    public class EmergencySceneCardRow : ObservableObject
    {
        private bool _isSelected;

        public EmergencySceneDto Dto { get; set; }
        public string Name { get { return Dto.Name ?? string.Empty; } }
        /// <summary>描述行：DTO 无场景描述字段，取类别（真实数据，不拼造）。</summary>
        public string Description { get { return string.IsNullOrWhiteSpace(Dto.Category) ? "点击选择场景" : Dto.Category; } }
        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); OnPropertyChanged(nameof(FootText)); }
        }
        /// <summary>卡脚文案（原型行 108-110）。</summary>
        public string FootText { get { return IsSelected ? "已选中 · 点击右侧一键发起" : "点击选择场景"; } }
    }

    /// <summary>自动匹配责任人行（BR-EMG-03：角色 + 当日排班 + 在岗）。</summary>
    public class EmergencyMatchRow : ObservableObject
    {
        public EmergencyMatchDto Dto { get; set; }
        public string Initial { get { string n = Dto.Name ?? string.Empty; return n.Length > 0 ? n.Substring(0, 1) : "?"; } }
        /// <summary>原型口径：张强 · 保安班长（早班 · 在岗）。</summary>
        public string DisplayLine
        {
            get
            {
                string d = (Dto.Name ?? "—") + " · " + (string.IsNullOrWhiteSpace(Dto.PositionName) ? (Dto.Role ?? "责任人") : Dto.PositionName);
                if (Dto.OnDuty) d += "（" + (string.IsNullOrWhiteSpace(Dto.ShiftName) ? "在岗" : Dto.ShiftName + " · 在岗") + "）";
                else d += "（未排班）";
                return d;
            }
        }
        public string Phone { get { return string.IsNullOrWhiteSpace(Dto.Phone) ? "—" : Dto.Phone; } }
        public bool HasPhone { get { return !string.IsNullOrWhiteSpace(Dto.Phone); } }
    }

    /// <summary>应急发起（PG-EMG-02，UC-EMG-003/004，BR-EMG-01/03）。</summary>
    public class EmergencyLaunchViewModel : BaseInfoPageViewModel
    {
        private EmergencySceneCardRow _selectedCard;
        private string _location = string.Empty;
        private string _detailLocation = string.Empty;
        private string _description = string.Empty;
        private int _levelIndex = 1; // 默认 Ⅱ 级（较大），Level = LevelIndex + 1
        private DateTime _eventTime = DateTime.Now;
        private bool _isConfirmVisible;
        private string _confirmText = string.Empty;
        private bool _canCancel;
        private string _cancelCountdownText = string.Empty;
        private int _launchedEventId;
        private readonly DispatcherTimer _cancelTimer;
        private int _cancelSecondsLeft;

        /// <summary>跨页导航回调（发起成功后跳转事件工作台，由 ShellViewModel 注入；未注入时保持页内提示）。</summary>
        private readonly Action<string> _navigateToPage;

        public EmergencyLaunchViewModel(IApiClient api) : this(api, null) { }

        public EmergencyLaunchViewModel(IApiClient api, Action<string> navigateToPage) : base(api)
        {
            _navigateToPage = navigateToPage;
            _cancelTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cancelTimer.Tick += OnCancelTimerTick;
            LoadCommand = new AsyncRelayCommand(LoadAsync);
            SelectSceneCommand = new RelayCommand<EmergencySceneCardRow>(s => SelectedCard = s);
            LaunchCommand = new AsyncRelayCommand(RequestLaunchAsync);
            ConfirmLaunchCommand = new AsyncRelayCommand(ConfirmLaunchAsync);
            CancelConfirmCommand = new RelayCommand(() => IsConfirmVisible = false);
            CancelEventCommand = new AsyncRelayCommand(CancelEventAsync);
            DialCommand = new RelayCommand<EmergencyMatchRow>(DialMatch);
            DialServiceCommand = new RelayCommand<string>(n => DialNumber(n, n == "119" ? "火警" : "报警"));
            _ = LoadAsync();
        }

        public ObservableCollection<EmergencySceneCardRow> Scenes { get; } = new ObservableCollection<EmergencySceneCardRow>();
        public ObservableCollection<EmergencyMatchRow> Matches { get; } = new ObservableCollection<EmergencyMatchRow>();

        public EmergencySceneCardRow SelectedCard
        {
            get { return _selectedCard; }
            set
            {
                if (SetProperty(ref _selectedCard, value))
                {
                    foreach (var s in Scenes) s.IsSelected = ReferenceEquals(s, value);
                    OnPropertyChanged(nameof(FormTitle));
                    _ = LoadMatchesAsync();
                }
            }
        }

        /// <summary>表单标题「事件信息（场景名）」（原型行 140）。</summary>
        public string FormTitle { get { return SelectedCard == null ? "事件信息" : "事件信息（" + SelectedCard.Name + "）"; } }
        public string LocationText { get { return _location; } set { SetProperty(ref _location, value); } }
        public string DetailLocation { get { return _detailLocation; } set { SetProperty(ref _detailLocation, value); } }
        public string Description { get { return _description; } set { SetProperty(ref _description, value); } }
        /// <summary>ComboBox 索引 0/1/2 ↔ Ⅰ/Ⅱ/Ⅲ 级（Level = Index + 1，默认 Ⅱ 级 = 2）。</summary>
        public int LevelIndex { get { return _levelIndex; } set { SetProperty(ref _levelIndex, value); } }
        public int Level { get { return _levelIndex + 1; } }
        public string LevelText { get { return Level == 1 ? "Ⅰ 级" : (Level == 2 ? "Ⅱ 级" : "Ⅲ 级"); } }
        public DateTime EventTime { get { return _eventTime; } set { SetProperty(ref _eventTime, value); } }
        public bool IsConfirmVisible { get { return _isConfirmVisible; } set { SetProperty(ref _isConfirmVisible, value); } }
        public string ConfirmText { get { return _confirmText; } set { SetProperty(ref _confirmText, value); } }
        /// <summary>1 分钟内撤销按钮（60 秒倒计时，原型「误发起可在 1 分钟内撤销（留痕）」）。</summary>
        public bool CanCancel { get { return _canCancel; } private set { SetProperty(ref _canCancel, value); } }
        public string CancelCountdownText { get { return _cancelCountdownText; } private set { SetProperty(ref _cancelCountdownText, value); } }
        public bool HasMatches { get { return Matches.Count > 0; } }

        public IAsyncRelayCommand LoadCommand { get; }
        public IRelayCommand<EmergencySceneCardRow> SelectSceneCommand { get; }
        public IAsyncRelayCommand LaunchCommand { get; }
        public IAsyncRelayCommand ConfirmLaunchCommand { get; }
        public IRelayCommand CancelConfirmCommand { get; }
        public IAsyncRelayCommand CancelEventCommand { get; }
        public IRelayCommand<EmergencyMatchRow> DialCommand { get; }
        public IRelayCommand<string> DialServiceCommand { get; }

        private async Task LoadAsync()
        {
            await RunAsync(async () =>
            {
                var list = await Api.GetEmergencyScenesAsync();
                Scenes.Clear();
                foreach (var s in list.Where(x => x.Status == 0)) Scenes.Add(new EmergencySceneCardRow { Dto = s }); // 仅启用场景（BR-EMG-01）
                if (Scenes.Count > 0) SelectedCard = Scenes[0];
            }, "场景已加载");
        }

        /// <summary>选中场景即刷新自动匹配责任人面板（GET scenes/{id}/match）。</summary>
        private async Task LoadMatchesAsync()
        {
            if (SelectedCard == null) return;
            int sceneId = SelectedCard.Dto.Id;
            await RunAsync(async () =>
            {
                var list = await Api.GetEmergencyMatchesAsync(sceneId);
                Matches.Clear();
                foreach (var m in list) Matches.Add(new EmergencyMatchRow { Dto = m });
                OnPropertyChanged(nameof(HasMatches));
            }, "责任人已匹配");
        }

        private async Task RequestLaunchAsync()
        {
            if (SelectedCard == null) { ErrorText = "请选择应急场景"; return; }
            if (string.IsNullOrWhiteSpace(LocationText)) { ErrorText = "请填写事发地点"; return; }
            ConfirmText = "确认发起" + SelectedCard.Name + "应急事件（" + LevelText + "）？";
            IsConfirmVisible = true;
        }

        private async Task ConfirmLaunchAsync()
        {
            if (SelectedCard == null) return;
            var scene = SelectedCard;
            await RunAsync(async () =>
            {
                var detail = await Api.CreateEmergencyEventAsync(new EmergencyEventCreateRequest
                {
                    SceneId = scene.Dto.Id, EventTime = EventTime,
                    Location = LocationText, DetailLocation = DetailLocation, Description = Description, Level = Level
                });
                IsConfirmVisible = false;
                _launchedEventId = detail.Event.Id;
                string eventNo = string.IsNullOrEmpty(detail.Event.EventNo) ? ("EM-" + detail.Event.Id) : detail.Event.EventNo;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已发起应急事件 " + eventNo + "（已广播并推送责任人）；可在 1 分钟内撤销";
                StartCancelCountdown();
                // 发起成功后跳转事件工作台（PG-EMG-03）；ShellViewModel 注入导航回调后生效
                if (_navigateToPage != null) _navigateToPage("事件工作台");
            }, "应急事件已发起");
        }

        private async Task CancelEventAsync()
        {
            if (_launchedEventId <= 0) return;
            int id = _launchedEventId;
            await RunAsync(async () =>
            {
                await Api.CancelEmergencyEventAsync(id);
                StopCancelCountdown();
                _launchedEventId = 0;
                StatusText = DateTime.Now.ToString("HH:mm:ss ") + "应急事件已撤销（留痕）";
            }, "应急事件已撤销");
        }

        private void StartCancelCountdown()
        {
            _cancelSecondsLeft = 60;
            CanCancel = true;
            UpdateCountdownText();
            _cancelTimer.Start();
        }

        private void StopCancelCountdown()
        {
            _cancelTimer.Stop();
            CanCancel = false;
        }

        private void OnCancelTimerTick(object sender, EventArgs e)
        {
            _cancelSecondsLeft--;
            if (_cancelSecondsLeft <= 0)
            {
                StopCancelCountdown();
                CancelCountdownText = string.Empty;
                return;
            }
            UpdateCountdownText();
        }

        private void UpdateCountdownText()
        {
            CancelCountdownText = "撤销事件（剩余 " + _cancelSecondsLeft + " 秒）";
        }

        /// <summary>拨打责任人（记录版口径：弹窗显示号码 + 复制，不接系统话机）。</summary>
        private void DialMatch(EmergencyMatchRow row)
        {
            if (row == null || !row.HasPhone) return;
            DialNumber(row.Phone, row.Dto.Name ?? "责任人");
        }

        /// <summary>拨打（119/110 或责任人电话）：显示号码并可复制到剪贴板。</summary>
        private void DialNumber(string phone, string target)
        {
            try
            {
                var result = System.Windows.MessageBox.Show(
                    "拨打" + target + "\n号码：" + phone + "\n\n（拨打走系统话机并自动生成通话记录（时长/结果）写入事件）\n\n是否复制号码？",
                    "拨打确认", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    System.Windows.Clipboard.SetText(phone);
                    StatusText = DateTime.Now.ToString("HH:mm:ss ") + "已复制号码 " + phone + " 到剪贴板";
                }
            }
            catch (Exception)
            {
                // 剪贴板被占用等场景忽略，不影响页面
            }
        }
    }
}
