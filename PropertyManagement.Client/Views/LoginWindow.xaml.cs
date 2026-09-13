using System;
using System.Windows;
using PropertyManagement.Client.Services;
using PropertyManagement.Client.ViewModels;
using PropertyManagement.Contract.Auth;

namespace PropertyManagement.Client.Views
{
    /// <summary>登录页（PG-LOGIN，UC-COM-001）。
    /// 说明：HandyControl PasswordBox 的 Password 为普通 CLR 属性（非依赖属性），
    /// 不能 XAML 绑定，故通过内部 PasswordBox 的 PasswordChanged 事件回填 VM（2026-08-31 修复）。</summary>
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _vm;

        public LoginWindow()
            : this(null)
        {
        }

        /// <param name="notice">非空时显示在登录页提示区（会话失效回落时使用）。</param>
        public LoginWindow(string notice)
        {
            InitializeComponent();

            _vm = new LoginViewModel(ApiClientFactory.Create());
            _vm.LoginSucceeded += OnLoginSucceeded;
            DataContext = _vm;
            _vm.ShowNotice(notice);

            Loaded += OnLoaded;
        }


        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var inner = PasswordInput.ActualPasswordBox;
            if (inner != null)
            {
                inner.PasswordChanged += OnPasswordChanged;
            }
            PasswordInput.Focus();
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            _vm.Password = PasswordInput.Password;
        }

        private void OnLoginSucceeded(LoginResult result)
        {
            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
            Close();
        }

        // 无边框窗口：背景区域左键按下拖动窗口（输入框/按钮自身已处理鼠标按下，不会触发拖动）
        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { /* 非左键按下时的防御 */ }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
