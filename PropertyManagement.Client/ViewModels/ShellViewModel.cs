using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Auth;
using PropertyManagement.Contract.Common;

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

        // ---- R17：顶栏搜索 / 待办中心 / 个人信息 ----
        private readonly DispatcherTimer _searchTimer;
        private bool _searchBusy;
        private bool _isSearchOpen;
        private string _searchSummary = string.Empty;
        private bool _isTodoCenterOpen;
        private int _todoTotal;
        private string _todoSummary = "待办 0 项";
        private string _todoDetail = "欠费 0  ·  纠纷 0  ·  到期 0";
        private string _userName;
        private string _userAvatar = "管";
        private Brush _userAvatarBrush = AvatarPalette.BrushOf(null);
        private string _userPhone = string.Empty;
        private string _userBio = string.Empty;

        /// <summary>导航树：仪表盘 + 8 个模块（两级，原型评审记录 §七 定稿）。</summary>
        public ObservableCollection<NavNode> NavNodes { get; } = new ObservableCollection<NavNode>();

        public string UserName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_userName))
                {
                    return _userName;
                }
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
            get { return _userAvatar; }
        }

        /// <summary>头像底色（R17：个人信息页选择的内置头像色）。</summary>
        public Brush UserAvatarBrush
        {
            get { return _userAvatarBrush; }
        }

        /// <summary>顶栏下拉展示的手机号（未填写时提示补全）。</summary>
        public string UserPhoneText
        {
            get { return string.IsNullOrWhiteSpace(_userPhone) ? "未填写手机号" : _userPhone; }
        }

        /// <summary>顶栏下拉展示的个人简介（未填写时给出引导文案）。</summary>
        public string UserBioText
        {
            get { return string.IsNullOrWhiteSpace(_userBio) ? "未填写个人简介" : _userBio; }
        }

        /// <summary>是否需要强制修改密码（首登/管理员标记）：落地页仍为仪表盘，仅额外弹改密对话框。</summary>
        public bool MustChangePassword
        {
            get { return _mustChangePassword; }
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
            get { return _todoSummary; }
            private set { SetProperty(ref _todoSummary, value); }
        }

        public string TodoDetail
        {
            get { return _todoDetail; }
            private set { SetProperty(ref _todoDetail, value); }
        }

        public string VersionText
        {
            get { return "ANYI  V1.1.0"; }
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

        // ==================== R17：顶栏全局搜索 ====================

        /// <summary>顶栏全局搜索框（PG-SHELL）：输入 300ms 防抖后跨模块检索（房产/业主/设备/电话/员工/纠纷）。</summary>
        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ScheduleSearch();
                }
            }
        }

        /// <summary>搜索结果（按模块分组）。</summary>
        public ObservableCollection<GlobalSearchGroupDto> SearchGroups { get; } = new ObservableCollection<GlobalSearchGroupDto>();

        public bool IsSearchOpen
        {
            get { return _isSearchOpen; }
            set { SetProperty(ref _isSearchOpen, value); }
        }

        public bool SearchBusy
        {
            get { return _searchBusy; }
            private set { SetProperty(ref _searchBusy, value); }
        }

        /// <summary>结果摘要（"共 N 条" / "未找到匹配结果"）。</summary>
        public string SearchSummary
        {
            get { return _searchSummary; }
            private set { SetProperty(ref _searchSummary, value); }
        }

        // ==================== R17：待办中心（顶部铃铛） ====================

        public ObservableCollection<TodoItemDto> Todos { get; } = new ObservableCollection<TodoItemDto>();

        public bool IsTodoCenterOpen
        {
            get { return _isTodoCenterOpen; }
            set { SetProperty(ref _isTodoCenterOpen, value); }
        }

        public int TodoTotal
        {
            get { return _todoTotal; }
            private set
            {
                if (SetProperty(ref _todoTotal, value))
                {
                    OnPropertyChanged(nameof(TodoBadgeText));
                    OnPropertyChanged(nameof(HasTodoBadge));
                }
            }
        }

        /// <summary>铃铛红点数字（>99 显示 99+）。</summary>
        public string TodoBadgeText
        {
            get { return _todoTotal > 99 ? "99+" : _todoTotal.ToString(); }
        }

        public bool HasTodoBadge
        {
            get { return _todoTotal > 0; }
        }

        public IAsyncRelayCommand LogoutCommand { get; }

        public IAsyncRelayCommand ChangePasswordCommand { get; }

        public IRelayCommand<NavNode> SelectNodeCommand { get; }

        public IRelayCommand<NavPage> SelectPageCommand { get; }

        public IRelayCommand ToggleUserMenuCommand { get; }

        public IAsyncRelayCommand RefreshTodosCommand { get; }

        public IRelayCommand ToggleTodoCenterCommand { get; }

        public IRelayCommand<TodoItemDto> OpenTodoCommand { get; }

        public IRelayCommand CloseTodoCenterCommand { get; }

        public IRelayCommand<GlobalSearchItemDto> OpenSearchItemCommand { get; }

        public IRelayCommand OpenFirstSearchResultCommand { get; }

        public IRelayCommand CloseSearchCommand { get; }

        public IAsyncRelayCommand OpenProfileCommand { get; }

        public event Action LogoutRequested;

        public event Action ChangePasswordRequested;

        /// <summary>顶栏下拉「个人信息设置」被点击（由 MainWindow 打开设置窗口）。</summary>
        public event Action ProfileRequested;

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
            RefreshTodosCommand = new AsyncRelayCommand(RefreshTodosAsync);
            ToggleTodoCenterCommand = new RelayCommand(ToggleTodoCenter);
            OpenTodoCommand = new RelayCommand<TodoItemDto>(OpenTodo);
            CloseTodoCenterCommand = new RelayCommand(() => IsTodoCenterOpen = false);
            OpenSearchItemCommand = new RelayCommand<GlobalSearchItemDto>(OpenSearchItem);
            OpenFirstSearchResultCommand = new RelayCommand(OpenFirstSearchResult);
            CloseSearchCommand = new RelayCommand(CloseSearch);
            OpenProfileCommand = new AsyncRelayCommand(OpenProfileAsync);

            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchTimer.Tick += async (sender, args) =>
            {
                _searchTimer.Stop();
                await RunSearchAsync();
            };

            SelectNode(NavNodes.First());
            _ = InitializeAsync();
            _ = LoadProfileAsync();
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
                    CurrentViewModel = new DashboardViewModel(_api, NavigateTo, NavigateToPageWithKeyword,
                        OpenTodoCenterFromDashboard, () => UserName);
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

        // ==================== R17：全局搜索 / 待办中心 / 个人信息 ====================

        private void ScheduleSearch()
        {
            _searchTimer.Stop();
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                SearchGroups.Clear();
                SearchSummary = string.Empty;
                IsSearchOpen = false;
                return;
            }

            IsSearchOpen = true;
            SearchSummary = "搜索中…";
            _searchTimer.Start();
        }

        private async Task RunSearchAsync()
        {
            string keyword = (_searchText ?? string.Empty).Trim();
            if (keyword.Length == 0)
            {
                IsSearchOpen = false;
                return;
            }

            SearchBusy = true;
            try
            {
                GlobalSearchResultDto result = await _api.SearchAsync(keyword);
                // 结果过期保护：请求期间用户已改词则丢弃本次结果
                if (!string.Equals((_searchText ?? string.Empty).Trim(), keyword, StringComparison.Ordinal))
                {
                    return;
                }

                SearchGroups.Clear();
                if (result.Groups != null)
                {
                    foreach (GlobalSearchGroupDto group in result.Groups)
                    {
                        SearchGroups.Add(group);
                    }
                }
                SearchSummary = result.Total == 0 ? "未找到匹配结果" : "共 " + result.Total + " 条匹配";
                IsSearchOpen = true;
            }
            catch (Exception)
            {
                SearchGroups.Clear();
                SearchSummary = "搜索失败，请确认本地服务已启动";
                IsSearchOpen = true;
            }
            finally
            {
                SearchBusy = false;
            }
        }

        private void CloseSearch()
        {
            IsSearchOpen = false;
        }

        private void OpenFirstSearchResult()
        {
            GlobalSearchItemDto first = SearchGroups
                .SelectMany(g => g.Items ?? new List<GlobalSearchItemDto>())
                .FirstOrDefault();
            OpenSearchItem(first);
        }

        private void OpenSearchItem(GlobalSearchItemDto item)
        {
            if (item == null)
            {
                return;
            }
            IsSearchOpen = false;
            NavigateToPageWithKeyword(item.TargetModule, item.TargetPage, item.Keyword);
        }

        /// <summary>跳转模块页并带关键词过滤（R17：搜索结果 / 待办跳转复用；无关键词时仅跳页）。</summary>
        public void NavigateToPageWithKeyword(string moduleKey, string pageTitle, string keyword)
        {
            NavigateToPage(moduleKey, pageTitle);
            if (string.IsNullOrWhiteSpace(keyword) || CurrentViewModel == null)
            {
                return;
            }

            switch (CurrentViewModel)
            {
                case PropertyListViewModel property:
                    property.SearchText = keyword;
                    property.QueryCommand.Execute(null);
                    break;
                case OwnerProfileViewModel owner:
                    owner.SearchText = keyword;
                    owner.SearchCommand.Execute(null);
                    break;
                case DeviceListViewModel device:
                    device.Keyword = keyword;
                    device.SearchCommand.Execute(null);
                    break;
                case PhoneQueryViewModel phone:
                    phone.Keyword = keyword; // setter 内部自动重载
                    break;
                case EmployeeListViewModel employee:
                    employee.Keyword = keyword;
                    employee.SearchCommand.Execute(null);
                    break;
                case DisputeListViewModel dispute:
                    dispute.Keyword = keyword; // setter 内部自动重载
                    break;
            }
        }

        private void ToggleTodoCenter()
        {
            IsTodoCenterOpen = !IsTodoCenterOpen;
            if (IsTodoCenterOpen)
            {
                _ = RefreshTodosAsync();
            }
        }

        /// <summary>仪表盘「查看全部」→ 打开顶部铃铛待办中心（同一数据源）。</summary>
        private void OpenTodoCenterFromDashboard()
        {
            IsTodoCenterOpen = true;
            _ = RefreshTodosAsync();
        }

        private async Task RefreshTodosAsync()
        {
            try
            {
                TodoCenterDto center = await _api.GetTodosAsync(20);
                Todos.Clear();
                if (center.Items != null)
                {
                    foreach (TodoItemDto todo in center.Items)
                    {
                        Todos.Add(todo);
                    }
                }

                TodoTotal = center.Total;
                TodoSummary = "待办 " + center.Total + " 项";
                // 与待办中心同源（应急发起/工作台已按 R4 下线，故摘要不含应急项）
                TodoDetail = "欠费 " + CountOf(center.CountByKind, "arrears") +
                             "  ·  纠纷 " + CountOf(center.CountByKind, "dispute") +
                             "  ·  到期 " + CountOf(center.CountByKind, "maintenance");
            }
            catch (Exception)
            {
                TodoTotal = 0;
                TodoSummary = "待办 -- 项";
                TodoDetail = "待办加载失败（本地服务未就绪）";
            }
        }

        private static int CountOf(Dictionary<string, int> counts, string key)
        {
            int value;
            return counts != null && counts.TryGetValue(key, out value) ? value : 0;
        }

        private void OpenTodo(TodoItemDto todo)
        {
            if (todo == null)
            {
                return;
            }
            IsTodoCenterOpen = false;
            NavigateToPage(todo.TargetModule, todo.TargetPage);
        }

        private Task OpenProfileAsync()
        {
            IsUserMenuOpen = false;
            ProfileRequested?.Invoke();
            return Task.CompletedTask;
        }

        /// <summary>加载个人信息并刷新顶栏（失败静默：回退会话默认展示）。</summary>
        public async Task LoadProfileAsync()
        {
            try
            {
                ApplyProfile(await _api.GetProfileAsync());
            }
            catch (Exception)
            {
                // 顶栏保持会话默认值即可，不打断用户
            }
        }

        /// <summary>应用个人信息（设置页保存后即时回填顶栏，无需重登）。</summary>
        public void ApplyProfile(UserProfileDto profile)
        {
            if (profile == null)
            {
                return;
            }

            _userName = string.IsNullOrWhiteSpace(profile.DisplayName) ? null : profile.DisplayName.Trim();
            _userPhone = profile.Phone ?? string.Empty;
            _userBio = profile.Bio ?? string.Empty;
            _userAvatar = AvatarPalette.IsKnown(profile.AvatarKey)
                ? AvatarPalette.GlyphOf(profile.AvatarKey)
                : InitialOf(UserName);
            _userAvatarBrush = AvatarPalette.BrushOf(profile.AvatarKey);

            OnPropertyChanged(nameof(UserName));
            OnPropertyChanged(nameof(UserAvatar));
            OnPropertyChanged(nameof(UserAvatarBrush));
            OnPropertyChanged(nameof(UserPhoneText));
            OnPropertyChanged(nameof(UserBioText));
            OnPropertyChanged(nameof(StatusSummary));

            // 问候语使用个人信息中的名字（跨模块引用：仪表盘 ← 个人信息）
            var dashboard = CurrentViewModel as DashboardViewModel;
            if (dashboard != null)
            {
                dashboard.RefreshGreeting();
            }
        }

        private static string InitialOf(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? "管" : text.Trim().Substring(0, 1);
        }

        private async Task InitializeAsync()
        {
            SyncTimeText = "数据更新于 " + DateTime.Now.ToString("HH:mm");
            await RefreshTodosAsync();

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
                    // M8 T8-4-2：健康检查未通过时兜底拉起一次（同目录存在 Server.exe）
                    if (await ProbeBackendWithFallbackAsync())
                    {
                        StatusDotBrush = new SolidColorBrush(Color.FromRgb(0x36, 0xC9, 0x93));
                        SidebarStatusText = "本地服务运行正常";
                    }
                    else
                    {
                        StatusDotBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0x45, 0x45));
                        SidebarStatusText = "后端服务：响应异常（已尝试启动，可查看日志目录排查）";
                    }
                }
            }
            catch (Exception)
            {
                if (await ProbeBackendWithFallbackAsync())
                {
                    StatusDotBrush = new SolidColorBrush(Color.FromRgb(0x36, 0xC9, 0x93));
                    SidebarStatusText = "本地服务运行正常";
                }
                else
                {
                    StatusDotBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0x45, 0x45));
                    SidebarStatusText = "后端服务：未连接";
                }
            }
        }

        /// <summary>
        /// M8 T8-4-2：健康检查失败时，兜底拉起一次本地后端并在约 5 s 内复探（D8-4 口径 1：判定只认 /health）。
        /// </summary>
        private static async Task<bool> ProbeBackendWithFallbackAsync()
        {
            if (await ProbeBackendOnceAsync())
            {
                return true;
            }

            if (!BackendLauncher.TryStartServer())
            {
                return false;
            }

            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(1000);
                if (await ProbeBackendOnceAsync())
                {
                    return true;
                }
            }
            return false;
        }

        private static async Task<bool> ProbeBackendOnceAsync()
        {
            try
            {
                var health = await BackendLauncher.ProbeAsync(2000);
                return health != null && string.Equals(health.Status, "ok", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
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
