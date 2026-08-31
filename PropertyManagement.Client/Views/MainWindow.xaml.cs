using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Client.ViewModels;

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

        public MainWindow()
        {
            InitializeComponent();

            _vm = new ShellViewModel(ApiClientFactory.Create());
            _vm.LogoutRequested += OnLogoutRequested;
            _vm.ChangePasswordRequested += OnChangePasswordRequested;
            DataContext = _vm;

            UserMenuPopup.Closed += (sender, args) => _vm.IsUserMenuOpen = false;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WindowProc);
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
            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();
            Close();
        }

        private void OnChangePasswordRequested()
        {
            var dialog = new ChangePasswordWindow(_vm.Api)
            {
                Owner = this
            };
            dialog.ShowDialog();
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