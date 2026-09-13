using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyManagement.Client.Services;
using PropertyManagement.Contract.Auth;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>修改密码（UC-COM-002，改密入口；成功后强制重新登录）。</summary>
    public class ChangePasswordViewModel : ObservableObject
    {
        private readonly IApiClient _api;

        private string _oldPassword = string.Empty;
        private string _newPassword = string.Empty;
        private string _confirmPassword = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isBusy;

        public string OldPassword
        {
            get { return _oldPassword; }
            set { SetProperty(ref _oldPassword, value); }
        }

        public string NewPassword
        {
            get { return _newPassword; }
            set { SetProperty(ref _newPassword, value); }
        }

        public string ConfirmPassword
        {
            get { return _confirmPassword; }
            set { SetProperty(ref _confirmPassword, value); }
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

        public IAsyncRelayCommand SaveCommand { get; }

        public event Action Succeeded;

        public ChangePasswordViewModel(IApiClient api)
        {
            _api = api;
            SaveCommand = new AsyncRelayCommand(SaveAsync);
        }

        private async Task SaveAsync()
        {
            ErrorMessage = string.Empty;

            if (string.IsNullOrEmpty(NewPassword) || NewPassword.Length < 6)
            {
                ErrorMessage = "新密码长度不能少于 6 位";
                return;
            }
            if (NewPassword.Length > 20)
            {
                ErrorMessage = "新密码长度不能超过 20 位";
                return;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(NewPassword, "[A-Za-z]") ||
                !System.Text.RegularExpressions.Regex.IsMatch(NewPassword, "[0-9]"))
            {
                ErrorMessage = "新密码必须同时包含字母和数字";
                return;
            }
            if (NewPassword != ConfirmPassword)
            {
                ErrorMessage = "两次输入的新密码不一致";
                return;
            }

            IsBusy = true;
            try
            {
                await _api.ChangePasswordAsync(new ChangePasswordRequest
                {
                    OldPassword = OldPassword,
                    NewPassword = NewPassword
                });
                Succeeded?.Invoke();
            }
            catch (ApiClientException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (Exception)
            {
                ErrorMessage = "修改密码失败：无法连接后端服务";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
