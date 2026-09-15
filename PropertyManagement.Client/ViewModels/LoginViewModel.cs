using System;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Auth;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>登录页（PG-LOGIN，UC-COM-001）：记住账号、本地服务状态。</summary>
    public class LoginViewModel : ObservableObject
    {
        private readonly IApiClient _api;

        private static readonly SolidColorBrush ReadyBrush = Freeze(Color.FromRgb(0x12, 0x80, 0x5C));
        private static readonly SolidColorBrush PendingBrush = Freeze(Color.FromRgb(0x8A, 0x6E, 0x3C));
        private static readonly SolidColorBrush FailedBrush = Freeze(Color.FromRgb(0xD6, 0x45, 0x45));

        private string _userName;
        private string _password = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isBusy;
        private bool _rememberAccount;
        private string _serviceStatusText = "● 本地服务检测中…";
        private SolidColorBrush _serviceStatusBrush = PendingBrush;
        private string _hintText;

        public string UserName
        {
            get { return _userName; }
            set { SetProperty(ref _userName, value); }
        }

        public string Password
        {
            get { return _password; }
            set { SetProperty(ref _password, value); }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            private set { SetProperty(ref _errorMessage, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set { SetProperty(ref _isBusy, value); }
        }

        public bool RememberAccount
        {
            get { return _rememberAccount; }
            set { SetProperty(ref _rememberAccount, value); }
        }

        public bool IsMock
        {
            get { return _api.IsMock; }
        }

        /// <summary>登录页辅助提示：演示模式提示，或后端未就绪时的排查指引（M8 T8-4-3）。</summary>
        public string HintText
        {
            get { return _hintText; }
            private set { SetProperty(ref _hintText, value); }
        }

        /// <summary>本地服务状态（M8 T8-4-1）：未启动 → 启动中（含重试次数）→ 正常 / 超时。</summary>
        public string ServiceStatusText
        {
            get { return _serviceStatusText; }
            private set { SetProperty(ref _serviceStatusText, value); }
        }

        /// <summary>本地服务状态配色：就绪=绿、等待/启动=暖金、超时=红。</summary>
        public SolidColorBrush ServiceStatusBrush
        {
            get { return _serviceStatusBrush; }
            private set { SetProperty(ref _serviceStatusBrush, value); }
        }

        /// <summary>启动编排是否已判定本地服务就绪。</summary>
        public bool IsBackendReady { get; private set; }

        public IAsyncRelayCommand LoginCommand { get; }

        public event Action<LoginResult> LoginSucceeded;

        public LoginViewModel(IApiClient api)
        {
            _api = api;
            ClientPrefs.Load();
            UserName = ClientPrefs.Current.RememberAccount ? ClientPrefs.Current.SavedUserName : "admin";
            RememberAccount = ClientPrefs.Current.RememberAccount;
            _hintText = _api.IsMock ? "演示模式：admin / Admin@123" : string.Empty;
            LoginCommand = new AsyncRelayCommand(LoginAsync);
        }

        /// <summary>登录页提示（如「登录状态已失效，请重新登录」）。</summary>
        public void ShowNotice(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                ErrorMessage = message;
            }
        }

        /// <summary>接收启动编排进度（M8 T8-4-1）；须在 UI 线程调用。</summary>
        public void ApplyStartupProgress(BackendStartupProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            ServiceStatusText = progress.Text;
            switch (progress.State)
            {
                case BackendStartupState.Ready:
                    ServiceStatusBrush = ReadyBrush;
                    break;
                case BackendStartupState.TimedOut:
                    ServiceStatusBrush = FailedBrush;
                    break;
                default:
                    ServiceStatusBrush = PendingBrush;
                    break;
            }
        }

        /// <summary>接收启动编排结果（M8 T8-4-3：超时给出含日志路径的排查指引）。</summary>
        public void ApplyStartupResult(BackendStartupResult result)
        {
            if (result == null)
            {
                return;
            }

            IsBackendReady = result.Ready;
            if (result.Ready)
            {
                ServiceStatusText = "● 本地服务正常";
                ServiceStatusBrush = ReadyBrush;
                return;
            }

            ServiceStatusText = "● 本地服务未就绪（已超时）";
            ServiceStatusBrush = FailedBrush;
            HintText = BackendStartup.BuildTimeoutGuidance(result);
        }

        private static SolidColorBrush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrEmpty(Password))
            {
                ErrorMessage = "请输入用户名和密码";
                return;
            }

            IsBusy = true;
            ErrorMessage = string.Empty;
            try
            {
                var result = await _api.LoginAsync(new LoginRequest
                {
                    UserName = UserName.Trim(),
                    Password = Password
                });

                SessionManager.Instance.Save(new SessionInfo
                {
                    Token = result.Token,
                    DisplayName = result.DisplayName,
                    ExpiresAt = result.ExpiresAt,
                    MustChangePassword = result.MustChangePassword
                });

                ClientPrefs.Current.RememberAccount = RememberAccount;
                ClientPrefs.Current.SavedUserName = RememberAccount ? UserName.Trim() : string.Empty;
                ClientPrefs.Current.Save();

                LoginSucceeded?.Invoke(result);
            }
            catch (ApiClientException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (Exception)
            {
                ErrorMessage = "无法连接后端服务，请确认服务已启动（演示模式可直接登录）";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
