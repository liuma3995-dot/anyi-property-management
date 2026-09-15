using System;
using System.Threading.Tasks;
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
        private bool _startupChecked;
        private bool _isPasswordVisible;

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


        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            var inner = PasswordInput.ActualPasswordBox;
            if (inner != null)
            {
                inner.PasswordChanged += OnPasswordChanged;
                HookEnterSubmit(inner);
            }
            // 明文框与密文框内容同步回 VM，保证「回车登录」「点击登录」用同一份密码
            PasswordPlainInput.TextChanged += (s, args) => { _vm.Password = PasswordPlainInput.Text; };
            // 回车登录：挂在各输入控件上（handledEventsToo=true，避免被 HandyControl 模板的类处理器吃掉）
            HookEnterSubmit(UserNameInput);
            HookEnterSubmit(PasswordPlainInput);
            HookEnterSubmit(this);
            PasswordInput.Focus();

            await RunBackendStartupAsync();
        }

        /// <summary>
        /// 启动编排（M8 T8-4-1~T8-4-3）：探测 /health → 必要时兜底拉起 → 轮询等待 →
        /// 状态显示（未启动 / 启动中（含重试次数）/ 正常 / 超时+排查指引）。
        /// 不阻塞登录表单：编排期间用户仍可输入，登录失败仍走原有提示口径。
        /// </summary>
        private async Task RunBackendStartupAsync()
        {
            if (_startupChecked)
            {
                return;
            }
            _startupChecked = true;

            try
            {
                var result = await BackendStartup.EnsureAsync(
                    progress => OnUi(() => _vm.ApplyStartupProgress(progress)));
                OnUi(() => _vm.ApplyStartupResult(result));
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
            }
        }

        private void OnUi(Action action)
        {
            if (Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.Invoke(action);
            }
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            _vm.Password = PasswordInput.Password;
        }

        /// <summary>
        /// 回车即登录（M8 登录体验修复）：在窗口层接管 Enter 并执行登录命令。
        /// 说明：仅设置按钮 IsDefault 在本页不生效——焦点位于用户名/密码输入控件时
        /// Enter 被输入控件消费，不会冒泡到默认按钮；这里用 PreviewKeyDown 兜住该路径。
        /// </summary>
        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            TrySubmitLogin(e);
        }

        /// <summary>
        /// 为元素挂回车提交处理。handledEventsToo=true 是关键：HandyControl 的 TextBox 模板
        /// 会在类处理器中把回车标记为已处理，普通 `+=` 订阅收不到事件（M8 实测）。
        /// </summary>
        private void HookEnterSubmit(System.Windows.UIElement element)
        {
            if (element == null)
            {
                return;
            }

            element.AddHandler(System.Windows.Input.Keyboard.PreviewKeyDownEvent,
                new System.Windows.Input.KeyEventHandler(OnInputPreviewKeyDown), true);
            element.AddHandler(System.Windows.Input.Keyboard.KeyDownEvent,
                new System.Windows.Input.KeyEventHandler(OnInputPreviewKeyDown), true);
        }

        /// <summary>输入控件上的回车处理（覆盖用户名 / 密文 / 明文三个输入框）。</summary>
        private void OnInputPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            TrySubmitLogin(e);
        }

        private void TrySubmitLogin(System.Windows.Input.KeyEventArgs e)
        {
            if (e == null || e.Key != System.Windows.Input.Key.Enter)
            {
                return;
            }

            var command = _vm.LoginCommand;
            if (command != null && command.CanExecute(null))
            {
                command.Execute(null);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 显示/隐藏密码（M8 登录页修复）：明文与密文控件共用同一格，切换时保持文本与光标位置一致。
        /// 明文内容始终回填 VM.Password，回车登录与点击登录行为完全一致。
        /// </summary>
        private void TogglePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordVisible = !_isPasswordVisible;

            if (_isPasswordVisible)
            {
                string current = PasswordInput.Password ?? string.Empty;
                PasswordPlainInput.Text = current;
                _vm.Password = current;
                PasswordInput.Visibility = Visibility.Collapsed;
                PasswordPlainInput.Visibility = Visibility.Visible;
                PasswordPlainInput.Focus();
                PasswordPlainInput.CaretIndex = PasswordPlainInput.Text.Length;
                TogglePasswordIcon.Stroke = (System.Windows.Media.Brush)FindResource("AccentDeepBrush");
                TogglePasswordButton.ToolTip = "隐藏密码";
            }
            else
            {
                string current = PasswordPlainInput.Text ?? string.Empty;
                PasswordInput.Password = current;
                _vm.Password = current;
                PasswordPlainInput.Visibility = Visibility.Collapsed;
                PasswordInput.Visibility = Visibility.Visible;
                PasswordInput.Focus();
                TogglePasswordIcon.Stroke = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x98, 0xA2, 0xB3));
                TogglePasswordButton.ToolTip = "显示密码";
            }
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
