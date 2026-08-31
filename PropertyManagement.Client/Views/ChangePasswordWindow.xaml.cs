using System.Windows;
using PropertyManagement.Client.Services;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>修改密码（UC-COM-002）：成功后清会话并强制重新登录。
    /// 说明：HandyControl PasswordBox 的 Password 为普通 CLR 属性（非依赖属性），
    /// 通过内部 PasswordBox 的 PasswordChanged 事件回填 VM（2026-08-31 修复）。</summary>
    public partial class ChangePasswordWindow : Window
    {
        private readonly ChangePasswordViewModel _vm;

        public ChangePasswordWindow(IApiClient api)
        {
            InitializeComponent();

            _vm = new ChangePasswordViewModel(api);
            _vm.Succeeded += OnSucceeded;
            DataContext = _vm;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Wire(OldPasswordInput, value => _vm.OldPassword = value);
            Wire(NewPasswordInput, value => _vm.NewPassword = value);
            Wire(ConfirmPasswordInput, value => _vm.ConfirmPassword = value);
        }

        private static void Wire(HandyControl.Controls.PasswordBox box, System.Action<string> setter)
        {
            var inner = box.ActualPasswordBox;
            if (inner != null)
            {
                inner.PasswordChanged += (s, e) => setter(box.Password);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnSucceeded()
        {
            SessionManager.Instance.Clear();

            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();

            var owner = Owner;
            Close();
            owner?.Close();
        }
    }
}

