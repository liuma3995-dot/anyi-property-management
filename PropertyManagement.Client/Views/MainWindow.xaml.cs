using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Client.ViewModels;
using PropertyManagement.Contract.Common;

namespace PropertyManagement.Client.Views
{
    /// <summary>主窗口框架（PG-SHELL，无边框：hc:Window + 顶栏自绘窗口按钮，2026-08-31）。</summary>
    public partial class MainWindow : HandyControl.Controls.Window
    {
        private readonly ShellViewModel _vm;
        private HwndSource _source;
        private double _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;
        private bool _dragFromMaximized;
        private System.Windows.Point _dragStart;
        private bool _returningToLogin;
        private bool _mustChangeHandled;
        /// <summary>窗口是否已关闭：关闭后不得再作为 Owner 挂载子对话框（否则抛「无法将 Owner 属性设置为已关闭的 Window」）。</summary>
        private bool _isClosed;

        public MainWindow()
        {
            InitializeComponent();

            _vm = new ShellViewModel(ApiClientFactory.Create());
            _vm.LogoutRequested += OnLogoutRequested;
            _vm.ChangePasswordRequested += OnChangePasswordRequested;
            _vm.ProfileRequested += OnProfileRequested;
            SessionManager.Instance.SessionExpired += OnSessionExpired;
            DataContext = _vm;

            UserMenuPopup.Closed += (sender, args) => _vm.IsUserMenuOpen = false;
            ContentRendered += OnContentRendered;
        }

        /// <summary>
        /// R18：登录后落地页固定为仪表盘；若命中「首登/管理员标记强制改密」，
        /// 在仪表盘之上弹出改密对话框（不再把首页替换成「修改密码」页）。
        /// </summary>
        private void OnContentRendered(object sender, EventArgs e)
        {
            if (_mustChangeHandled || !_vm.MustChangePassword)
            {
                return;
            }
            _mustChangeHandled = true;
            OnChangePasswordRequested();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WindowProc);
        }

        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;
            base.OnClosed(e);
        }

        // 无边框窗口最大化时按显示器工作区约束尺寸（修复 HandyControl 仅在任务栏自动隐藏时才约束的问题）
        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeWindowInterop.WM_GETMINMAXINFO)
            {
                NativeWindowInterop.ConstrainMaxSizeToWorkArea(hwnd, lParam);
            }
            return IntPtr.Zero;
        }

        private void OnLogoutRequested()
        {
            ReturnToLogin(null);
        }

        /// <summary>
        /// 会话失效回落（M7 修复）：token 无效/过期/改密失效时清空会话并回到登录页，
        /// 避免主窗口停留在「token 无效或过期」的报错态无法自愈。
        /// </summary>
        private void OnSessionExpired(string message)
        {
            // 统一提示口径：基础文案已含原因（如「登录状态已失效：token 无效或过期」），
            // 仅在尚未包含「请重新登录」时追加一次，避免出现「…请重新登录，请重新登录。」。
            string notice = string.IsNullOrWhiteSpace(message) ? "登录状态已失效" : message.Trim();
            if (notice.IndexOf("请重新登录", StringComparison.Ordinal) < 0)
            {
                notice += "，请重新登录。";
            }
            else if (!notice.EndsWith("。", StringComparison.Ordinal))
            {
                notice += "。";
            }
            ReturnToLogin(notice);
        }

        private void ReturnToLogin(string notice)
        {
            if (_returningToLogin)
            {
                return;
            }
            _returningToLogin = true;

            SessionManager.Instance.SessionExpired -= OnSessionExpired;
            var login = new LoginWindow(notice);
            Application.Current.MainWindow = login;
            login.Show();
            Close();
        }

        private void OnChangePasswordRequested()
        {
            var dialog = new ChangePasswordWindow(_vm.Api, _vm.MustChangePassword);
            if (!_isClosed) { dialog.Owner = this; }
            dialog.ShowDialog();
            // UC-COM-002：强制改密流程未完成即关闭对话框 → 强制退出到登录页（不可跳过）
            if (_vm.MustChangePassword && !dialog.Changed)
            {
                ForceLogout();
            }
        }

        /// <summary>清会话并返回登录页（强制改密未完成 / 会话失效复用）。</summary>
        private void ForceLogout()
        {
            SessionManager.Instance.Clear();
            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();
            Close();
        }

        /// <summary>R17：顶栏下拉 → 个人信息设置（小表单就地填写，不新增页面）。</summary>
        private void OnProfileRequested()
        {
            var dialog = new ProfileWindow(_vm.Api);
            if (!_isClosed) { dialog.Owner = this; }
            dialog.ProfileSaved += profile => _vm.ApplyProfile(profile);
            dialog.ShowDialog();
        }

        /// <summary>R17：全局搜索结果点击 → 跳转目标模块页并带关键词过滤。</summary>
        private void SearchItem_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            var item = button == null ? null : button.Tag as GlobalSearchItemDto;
            if (item != null && _vm.OpenSearchItemCommand.CanExecute(item))
            {
                _vm.OpenSearchItemCommand.Execute(item);
            }
        }

        /// <summary>R17：铃铛待办项点击 → 跳转对应模块页处理。</summary>
        private void TodoItem_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            var item = button == null ? null : button.Tag as TodoItemDto;
            if (item != null && _vm.OpenTodoCommand.CanExecute(item))
            {
                _vm.OpenTodoCommand.Execute(item);
            }
        }

        // 无边框窗口：顶栏空白区左键拖拽（按钮/输入框已自行处理鼠标按下，不会触发）；双击切换最大化/还原
        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                _dragFromMaximized = false;
                ToggleMaximize();
                e.Handled = true;
                return;
            }

            if (WindowState == WindowState.Maximized)
            {
                // 最大化状态下先不还原：若为双击，第二次点击走 ToggleMaximize；若实际拖动，在 MouseMove 中还原跟随
                _dragFromMaximized = true;
                _dragStart = e.GetPosition(this);
                e.Handled = true;
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 非左键按下时的防护
            }
        }

        private void TopBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragFromMaximized || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var pos = e.GetPosition(this);
            var screen = PointToScreen(pos);
            _dragFromMaximized = false;
            WindowState = WindowState.Normal;
            Left = screen.X - _dragStart.X;
            Top = screen.Y - _dragStart.Y;
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 非左键按下时的防护
            }
        }

        private void TopBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragFromMaximized = false;
        }

        private void ToggleMaximize()
        {
            if (WindowState == WindowState.Normal)
            {
                _restoreLeft = Left;
                _restoreTop = Top;
                _restoreWidth = Width;
                _restoreHeight = Height;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowState = WindowState.Normal;
                Dispatcher.BeginInvoke(new Action(RestoreNormalBounds),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        private void RestoreNormalBounds()
        {
            Left = _restoreLeft;
            Top = _restoreTop;
            Width = _restoreWidth;
            Height = _restoreHeight;
        }


        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
