using System;
using System.Threading.Tasks;
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

        private string _userName;
        private string _password = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isBusy;
        private bool _rememberAccount;

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

        public string HintText
        {
            get { return _api.IsMock ? "演示模式：admin / Admin@123" : string.Empty; }
        }

        public string ServiceStatusText
        {
            get { return "● 本地服务正常"; }
        }

        public IAsyncRelayCommand LoginCommand { get; }

        public event Action<LoginResult> LoginSucceeded;

        public LoginViewModel(IApiClient api)
        {
            _api = api;
            ClientPrefs.Load();
            UserName = ClientPrefs.Current.RememberAccount ? ClientPrefs.Current.SavedUserName : "admin";
            RememberAccount = ClientPrefs.Current.RememberAccount;
            LoginCommand = new AsyncRelayCommand(LoginAsync);
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
                    ExpiresAt = result.ExpiresAt
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
