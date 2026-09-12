using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>主窗口框架（PG-SHELL）：左侧两级导航树/顶栏/状态栏。</summary>
    public class ShellViewModel : ObservableObject
    {
        private readonly IApiClient _api;

        private NavNode _selectedNode;
        private NavPage _selectedPage;
        private object _currentViewModel;
        private string _pageTitle = "仪表盘";
        private string _pagePath = "首页  /  工作概览";
        private string _sidebarStatusText = "本地服务运行正常";
        private string _syncTimeText = "数据更新于 --:--";
        private Brush _statusDotBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xA2, 0x3C));
        private bool _isUserMenuOpen;
        private string _searchText;
        private int? _pendingDisputeId;
        private readonly bool _mustChangePassword;

        /// <summary>导航树：仪表盘 + 8 个模块（两级，原型评审记录 §七 定稿）。</summary>
        public ObservableCollection<NavNode> NavNodes { get; } = new ObservableCollection<NavNode>();

        public string UserName
        {
            get
            {
                var session = SessionManager.Instance.Current;
                return session == null ? "系统管理员" : session.DisplayName;
            }
        }

        public string UserRoleText
        {
            get { return "超级管理员"; }
        }

        public string UserAvatar
        {
            get { return "管"; }
        }

        public IApiClient Api
        {
            get { return _api; }
        }

        /// <summary>顶栏页面标题（原型：18px/700）。</summary>
        public string PageTitle
        {
            get { return _pageTitle; }
            private set { SetProperty(ref _pageTitle, value); }
        }

        /// <summary>顶栏面包屑路径（原型：10px muted）。</summary>
        public string PagePath
        {
            get { return _pagePath; }
            private set { SetProperty(ref _pagePath, value); }
        }

        public object CurrentViewModel
        {
            get { return _currentViewModel; }
            private set { SetProperty(ref _currentViewModel, value); }
        }

        /// <summary>状态栏：当前用户摘要（原型 §2.2）。</summary>
        public string StatusSummary
        {
            get { return "当前用户：" + UserName; }
        }

        public string TodoSummary
        {
            get { return "待办 6 项"; }
        }

        public string TodoDetail
        {
            get { return "欠费 1  ·  应急 1  ·  纠纷 3  ·  到期 2"; }
        }

        public string VersionText
        {
            get { return "LAN-TING  V1.0.0"; }
        }

        /// <summary>侧栏系统状态条（原型：在线点 + 状态 + 数据更新时间）。</summary>
        public string SidebarStatusText
        {
            get { return _sidebarStatusText; }
            private set { SetProperty(ref _sidebarStatusText, value); }
        }

        public string SyncTimeText
        {
            get { return _syncTimeText; }
            private set { SetProperty(ref _syncTimeText, value); }
        }

        public Brush StatusDotBrush
        {
            get { return _statusDotBrush; }
            private set { SetProperty(ref _statusDotBrush, value); }
        }

        public bool IsUserMenuOpen
        {
            get { return _isUserMenuOpen; }
            set { SetProperty(ref _isUserMenuOpen, value); }
        }

        /// <summary>顶栏全局搜索框（PG-SHELL；M4 起接入查询，本期仅界面）。</summary>
        public string SearchText
        {
            get { return _searchText; }
            set { SetProperty(ref _searchText, value); }
        }

        public IAsyncRelayCommand LogoutCommand { get; }

        public IAsyncRelayCommand ChangePasswordCommand { get; }

        public IRelayCommand<NavNode> SelectNodeCommand { get; }

        public IRelayCommand<NavPage> SelectPageCommand { get; }

        public IRelayCommand ToggleUserMenuCommand { get; }

        public event Action LogoutRequested;

        public event Action ChangePasswordRequested;

        public ShellViewModel(IApiClient api)
        {
            _api = api;
            // UC-COM-002：登录返回 MustChangePassword（首登/管理员标记；R16 已下线 90 天强制更换）→ 进入后强制改密
            var session = SessionManager.Instance.Current;
            _mustChangePassword = session != null && session.MustChangePassword;

            BuildNav();

            LogoutCommand = new AsyncRelayCommand(LogoutAsync);
            ChangePasswordCommand = new AsyncRelayCommand(OpenChangePasswordAsync);
            SelectNodeCommand = new RelayCommand<NavNode>(SelectNode);
            SelectPageCommand = new RelayCommand<NavPage>(SelectPage);
            ToggleUserMenuCommand = new RelayCommand(() => IsUserMenuOpen = !IsUserMenuOpen);

            SelectNode(NavNodes.First());
            _ = InitializeAsync();

            if (_mustChangePassword)
            {
                NavigateToPage("system", "修改密码");
            }
        }

        /// <summary>T4F-2-1：财务模块内页级跳转（如账单工作台"催缴"→欠费台账）。</summary>
        public void NavigateToFinancePage(string pageTitle)
        {
            NavigateToPage("finance", pageTitle);
        }

        /// <summary>M6：通用模块内页级跳转（DIS 列表"纠纷登记"→登记页等）；EMG 仅保留场景与步骤维护，无内页跳转。</summary>
        public void NavigateToPage(string moduleKey, string pageTitle)
        {
            var node = NavNodes.FirstOrDefault(n => string.Equals(n.Key, moduleKey, StringComparison.OrdinalIgnoreCase));
            if (node == null) { return; }
            var page = node.Pages.FirstOrDefault(pg => string.Equals(pg.Title, pageTitle, StringComparison.Ordinal));
            if (page != null)
            {
                SelectPage(page);
            }
        }

        /// <summary>快速入口跳转（仪表盘）：按模块 key 定位并展开。</summary>
        public void NavigateTo(string key)
        {
            var node = NavNodes.FirstOrDefault(n => string.Equals(n.Key, key, StringComparison.OrdinalIgnoreCase));
            if (node != null)
            {
                SelectNode(node);
            }
        }

        private void BuildNav()
        {
            NavNodes.Add(new NavNode
            {
                Key = "dashboard",
                Title = "仪表盘",
                IconKey = "Icon.LayoutDashboard",
                IsModule = false
            });

            NavNodes.Add(Module("baseinfo", "基础信息", "Icon.Building",
                "房产列表", "业主档案", "业主-房产关系", "车位维护", "基础数据导入"));
            NavNodes.Add(Module("finance", "财务收费", "Icon.WalletCards",
                "收费项目维护", "账单工作台", "收款登记", "退款/减免/调整", "支出登记", "欠费台账", "财务报表", "收支明细流水"));
            NavNodes.Add(Module("emergency", "应急处置", "Icon.Siren",
                "场景与步骤维护"));
            NavNodes.Add(Module("org", "人员组织", "Icon.Users",
                "员工列表", "排班表", "考勤记录"));
            NavNodes.Add(Module("phonebook", "便民电话簿", "Icon.ContactRound",
                "电话查询", "电话条目维护"));
            NavNodes.Add(Module("dispute", "纠纷调解", "Icon.MessagesSquare",
                "纠纷列表", "纠纷登记", "处理与结案"));
            NavNodes.Add(Module("equipment", "设备台账", "Icon.HardHat",
                "设备列表", "保养年检登记", "故障登记", "到期提醒"));
            NavNodes.Add(Module("system", "系统设置", "Icon.Settings",
                "参数/字典维护", "备份与恢复", "审计日志查询", "修改密码"));
        }

        private static NavNode Module(string key, string title, string iconKey, params string[] pageTitles)
        {
            var node = new NavNode
            {
                Key = key,
                Title = title,
                IconKey = iconKey,
                IsModule = true
            };
            foreach (var pageTitle in pageTitles)
            {
                node.Pages.Add(new NavPage { Key = pageTitle, Title = pageTitle });
            }
            return node;
        }

        private void SelectNode(NavNode node)
        {
            if (node == null)
            {
                return;
            }

            foreach (var n in NavNodes)
            {
                n.IsSelected = ReferenceEquals(n, node);
                if (!n.IsModule)
                {
                    continue;
                }

                if (ReferenceEquals(n, node))
                {
                    n.IsExpanded = !n.IsExpanded;
                }
                else if (!ReferenceEquals(n, node))
                {
                    // 原型约定：当前页所属模块展开，其余模块收起
                    n.IsExpanded = false;
                }

                foreach (var p in n.Pages)
                {
                    p.IsSelected = false;
                }
            }

            _selectedNode = node;

            if (!node.IsModule)
            {
                _selectedPage = null;
                PageTitle = "仪表盘";
                PagePath = "首页  /  工作概览";
                CurrentViewModel = new DashboardViewModel(_api, NavigateTo);
            }
            else
            {
                // 财务收费/基础信息已落地真实子页：点击模块不再显示全局框架占位，直入首个业务子页
                bool hasRealPages = string.Equals(node.Title, "财务收费", StringComparison.Ordinal)
                                    || string.Equals(node.Title, "基础信息", StringComparison.Ordinal)
                                    || string.Equals(node.Title, "人员组织", StringComparison.Ordinal)
                                    || string.Equals(node.Title, "便民电话簿", StringComparison.Ordinal)
                                    || string.Equals(node.Title, "纠纷调解", StringComparison.Ordinal)
                                    || string.Equals(node.Title, "应急处置", StringComparison.Ordinal)
                                    || string.Equals(node.Title, "设备台账", StringComparison.Ordinal)
                                    || string.Equals(node.Title, "系统设置", StringComparison.Ordinal);
                if (hasRealPages && node.Pages.Count > 0)
                {
                    // 展开：进入首个子页；收起：仅收起子菜单，保留当前页（修复“只支持展开不支持收起”）
                    if (node.IsExpanded) SelectPage(node.Pages[0]);
                    else _selectedPage = null;
                }
                else
                {
                    _selectedPage = null;
                    PageTitle = node.Title;
                    PagePath = "首页  /  " + node.Title;
                    CurrentViewModel = new PlaceholderViewModel(node.Title);
                }
            }
        }

        private void SelectPage(NavPage page)
        {
            if (page == null)
            {
                return;
            }

            var node = NavNodes.FirstOrDefault(n => n.Pages.Contains(page));
            if (node == null)
            {
                return;
            }

            foreach (var n in NavNodes)
            {
                n.IsSelected = ReferenceEquals(n, node);
                n.IsExpanded = ReferenceEquals(n, node);
                foreach (var p in n.Pages)
                {
                    p.IsSelected = ReferenceEquals(p, page);
                }
            }

            _selectedNode = node;
            _selectedPage = page;

            PageTitle = page.Title;
            PagePath = "首页  /  " + node.Title + "  /  " + page.Title;
            CurrentViewModel = CreatePageViewModel(page.Key, node.Title);
        }

        /// <summary>按导航页 Key 创建页面 VM；财务 8 页走真实页面，其余模块暂用占位页（M4）。</summary>
        private object CreatePageViewModel(string pageKey, string moduleTitle)
        {
            if (string.Equals(moduleTitle, "基础信息", StringComparison.Ordinal))
            {
                switch (pageKey)
                {
                    case "房产列表": return new PropertyListViewModel(_api);
                    case "业主档案": return new OwnerProfileViewModel(_api);
                    case "业主-房产关系": return new OwnerRelationViewModel(_api);
                    case "车位维护": return new ParkingViewModel(_api);
                    case "基础数据导入": return new DataImportViewModel(_api);
                }
            }
            if (string.Equals(moduleTitle, "财务收费", StringComparison.Ordinal))
            {
                switch (pageKey)
                {
                    case "收费项目维护": return new ChargeItemsViewModel(_api);
                    case "账单工作台": return new BillWorkbenchViewModel(_api, NavigateToFinancePage);
                    case "收款登记": return new PaymentEntryViewModel(_api);
                    case "退款/减免/调整": return new RefundAdjustmentViewModel(_api);
                    case "支出登记": return new ExpenseViewModel(_api);
                    case "欠费台账": return new ArrearViewModel(_api);
                    case "财务报表": return new FinancialReportViewModel(_api);
                    case "收支明细流水": return new LedgerViewModel(_api);
                }
            }
            if (string.Equals(moduleTitle, "人员组织", StringComparison.Ordinal))
            {
                switch (pageKey)
                {
                    case "员工列表": return new EmployeeListViewModel(_api);
                    case "排班表": return new ScheduleViewModel(_api);
                    case "考勤记录": return new AttendanceViewModel(_api);
                }
            }
            if (string.Equals(moduleTitle, "便民电话簿", StringComparison.Ordinal))
            {
                switch (pageKey)
                {
                    case "电话查询": return new PhoneQueryViewModel(_api);
                    case "电话条目维护": return new PhoneEntryMaintainViewModel(_api);
                }
            }
            if (string.Equals(moduleTitle, "纠纷调解", StringComparison.Ordinal))
            {
                switch (pageKey)
                {
                    case "纠纷列表":
                        var listVm = new DisputeListViewModel(_api);
                        listVm.OpenHandle += NavigateToDispute;
                        listVm.OpenCreate += () => NavigateToPage("dispute", "纠纷登记");
                        return listVm;
                    case "纠纷登记": return new DisputeCreateViewModel(_api);
                    case "处理与结案":
                        var handleVm = new DisputeHandleViewModel(_api);
                        if (_pendingDisputeId.HasValue)
                        {
                            int id = _pendingDisputeId.Value;
                            _ = handleVm.LoadCaseAsync(id);
                        }
                        return handleVm;
                }
            }
            if (string.Equals(moduleTitle, "应急处置", StringComparison.Ordinal))
            {
                switch (pageKey)
                {
                    case "场景与步骤维护": return new EmergencySceneStepViewModel(_api);
                }
            }
            if (string.Equals(moduleTitle, "设备台账", StringComparison.Ordinal))
            {
                switch (pageKey)
                {
                    case "设备列表": return new DeviceListViewModel(_api);
                    case "保养年检登记": return new DeviceMaintainViewModel(_api);
                    case "故障登记": return new DeviceFaultViewModel(_api);
                    case "到期提醒": return new DeviceReminderViewModel(_api);
                }
            }
            if (string.Equals(moduleTitle, "系统设置", StringComparison.Ordinal))
            {
                switch (pageKey)
                {
                    case "参数/字典维护": return new DictParamViewModel(_api);
                    case "备份与恢复": return new BackupViewModel(_api);
                    case "审计日志查询": return new AuditLogViewModel(_api);
                    case "修改密码": return new ChangePasswordPageViewModel(_api, !_mustChangePassword);
                }
            }
            return new PlaceholderViewModel(pageKey);
        }

        private void NavigateToDispute(int caseId)
        {
            _pendingDisputeId = caseId;
            var node = NavNodes.FirstOrDefault(n => string.Equals(n.Key, "dispute", StringComparison.OrdinalIgnoreCase));
            if (node == null) { _pendingDisputeId = null; return; }
            var page = node.Pages.FirstOrDefault(pg => string.Equals(pg.Title, "处理与结案", StringComparison.Ordinal));
            if (page != null) SelectPage(page);
            _pendingDisputeId = null;
        }

        private async Task InitializeAsync()
        {
            SyncTimeText = "数据更新于 " + DateTime.Now.ToString("HH:mm");

            if (_api.IsMock)
            {
                StatusDotBrush = new SolidColorBrush(Color.FromRgb(0x36, 0xC9, 0x93));
                SidebarStatusText = "本地服务运行正常";
                bool started = BackendLauncher.TryStartServer();
                if (started)
                {
                    await Task.Delay(1500);
                    try
                    {
                        var health = await BackendLauncher.ProbeAsync();
                        bool ok = health != null && string.Equals(health.Status, "ok", StringComparison.OrdinalIgnoreCase);
                        SidebarStatusText = ok
                            ? "本地服务运行正常"
                            : "演示模式（Mock 数据，后端已启动但健康检查未通过）";
                    }
                    catch (Exception)
                    {
                        SidebarStatusText = "演示模式（Mock 数据，后端启动中，请稍候）";
                    }
                }
                else
                {
                    SidebarStatusText = BackendLauncher.IsPortInUse(5210)
                        ? "演示模式（Mock 数据，端口 5210 被占用但服务无响应）"
                        : "演示模式（Mock 数据，后端未启动；M2 落地后自动切换真实接口）";
                }
                return;
            }

            try
            {
                var health = await _api.GetHealthAsync();
                bool ok = health != null && string.Equals(health.Status, "ok", StringComparison.OrdinalIgnoreCase);
                if (ok)
                {
                    StatusDotBrush = new SolidColorBrush(Color.FromRgb(0x36, 0xC9, 0x93));
                    SidebarStatusText = "本地服务运行正常";
                }
                else
                {
                    SidebarStatusText = "后端服务：响应异常";
                }
            }
            catch (Exception)
            {
                StatusDotBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0x45, 0x45));
                SidebarStatusText = "后端服务：未连接";
            }
        }

        private Task OpenChangePasswordAsync()
        {
            IsUserMenuOpen = false;
            ChangePasswordRequested?.Invoke();
            return Task.CompletedTask;
        }

        private async Task LogoutAsync()
        {
            try
            {
                await _api.LogoutAsync();
            }
            catch
            {
                // 退出时后端不可达不阻断本地登出
            }

            SessionManager.Instance.Clear();
            IsUserMenuOpen = false;
            LogoutRequested?.Invoke();
        }
    }
}
