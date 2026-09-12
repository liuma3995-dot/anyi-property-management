using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PropertyManagement.Client.Services;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>修改密码页（PG-COM-04，UC-COM-002）。
    /// HandyControl PasswordBox 的 Password 为普通 CLR 属性（非依赖属性），XAML 直接 Binding
    /// 会在 InitializeComponent 抛 XamlParseException（P0 崩溃根因，原 12/14/16 行）。
    /// 参照 ChangePasswordWindow.xaml.cs（2026-08-31 修复）：经内部 ActualPasswordBox 的
    /// PasswordChanged 事件回填 VM；VM 清空输入时反向同步清空密码框。</summary>
    public partial class ChangePasswordPageView : UserControl
    {
        private bool _wired;

        public ChangePasswordPageView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ChangePasswordPageViewModel;
            if (vm == null) return;

            vm.Succeeded -= OnSucceeded; // 防重复订阅（Loaded 可能多次触发）
            vm.Succeeded += OnSucceeded;
            vm.PropertyChanged -= OnVmPropertyChanged;
            vm.PropertyChanged += OnVmPropertyChanged;

            WireAll(vm);
            if (!_wired)
            {
                // 模板尚未应用（ActualPasswordBox 未就绪）时延迟一次
                Dispatcher.BeginInvoke(new System.Action(() => WireAll(vm)),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ChangePasswordPageViewModel;
            if (vm == null) return;
            vm.Succeeded -= OnSucceeded;
            vm.PropertyChanged -= OnVmPropertyChanged;
        }

        private void WireAll(ChangePasswordPageViewModel vm)
        {
            if (_wired) return;
            if (OldPasswordInput == null || NewPasswordInput == null || ConfirmPasswordInput == null) return;
            if (OldPasswordInput.ActualPasswordBox == null || NewPasswordInput.ActualPasswordBox == null
                || ConfirmPasswordInput.ActualPasswordBox == null) return;

            Wire(OldPasswordInput, () => vm.OldPassword, value => vm.OldPassword = value);
            Wire(NewPasswordInput, () => vm.NewPassword, value => vm.NewPassword = value);
            Wire(ConfirmPasswordInput, () => vm.ConfirmPassword, value => vm.ConfirmPassword = value);
            _wired = true;
        }

        /// <summary>密码框 → VM（用户输入）；VM 清空 → 密码框同步（取消/成功后）。</summary>
        private void Wire(HandyControl.Controls.PasswordBox box, System.Func<string> getter, System.Action<string> setter)
        {
            var inner = box.ActualPasswordBox;
            if (inner == null) return;
            inner.PasswordChanged += (s, e) =>
            {
                if (!string.Equals(box.Password, getter(), System.StringComparison.Ordinal))
                {
                    setter(box.Password);
                }
            };
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var vm = (ChangePasswordPageViewModel)sender;
            SyncIfCleared(e.PropertyName, vm.OldPassword, OldPasswordInput);
            SyncIfCleared(e.PropertyName, vm.NewPassword, NewPasswordInput);
            SyncIfCleared(e.PropertyName, vm.ConfirmPassword, ConfirmPasswordInput);
        }

        // ---------- 眼睛图标：密码明文 / 密文切换（原型 PG-COM-04 每格右侧） ----------
        private void ToggleOldPassword_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ChangePasswordPageViewModel;
            if (vm == null) return;
            TogglePlain(OldPasswordInput, OldPasswordPlain, () => vm.OldPassword, value => vm.OldPassword = value);
        }

        private void ToggleNewPassword_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ChangePasswordPageViewModel;
            if (vm == null) return;
            TogglePlain(NewPasswordInput, NewPasswordPlain, () => vm.NewPassword, value => vm.NewPassword = value);
        }

        private void ToggleConfirmPassword_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ChangePasswordPageViewModel;
            if (vm == null) return;
            TogglePlain(ConfirmPasswordInput, ConfirmPasswordPlain, () => vm.ConfirmPassword, value => vm.ConfirmPassword = value);
        }

        /// <summary>切换某一格的明文输入框与密文框；两者共用同一 VM 属性，切换时同步当前值。</summary>
        private static void TogglePlain(HandyControl.Controls.PasswordBox box, TextBox plain,
            System.Func<string> getter, System.Action<string> setter)
        {
            if (box == null || plain == null) return;
            string value = getter() ?? string.Empty;

            if (plain.Visibility != Visibility.Visible)
            {
                plain.Text = value;
                plain.Visibility = Visibility.Visible;
                box.Visibility = Visibility.Collapsed;
                plain.Focus();
                plain.CaretIndex = plain.Text.Length;
            }
            else
            {
                plain.Visibility = Visibility.Collapsed;
                box.Visibility = Visibility.Visible;
                if (!string.Equals(box.Password, value, System.StringComparison.Ordinal))
                {
                    box.Password = value;
                }
                box.Focus();
            }
        }

        private static void SyncIfCleared(string propertyName, string value, HandyControl.Controls.PasswordBox box)
        {
            if (box == null) return;
            if (propertyName == "OldPassword" || propertyName == "NewPassword" || propertyName == "ConfirmPassword")
            {
                if (string.IsNullOrEmpty(value) && box.Password.Length > 0)
                {
                    box.Password = string.Empty; // 触发 PasswordChanged 回填 VM（同为空值不再变化）
                }
            }
        }

        /// <summary>改密成功强制退出：清会话并切换登录窗（同 ChangePasswordWindow.xaml.cs:46-57 既有链路，
        /// 仅复用 LoginWindow/SessionManager，不改 Shell/登录共享代码）。</summary>
        private void OnSucceeded()
        {
            SessionManager.Instance.Clear();

            Window oldMain = Application.Current.MainWindow;
            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();

            if (oldMain != null && !ReferenceEquals(oldMain, login))
            {
                oldMain.Close();
            }
        }
    }
}
