using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PropertyManagement.Client.Services;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>财务页公共基类（M4：统一忙碌/状态/错误展示与异常转译）。</summary>
    public abstract class FinancePageViewModel : ObservableObject
    {
        private bool _isBusy;
        private string _statusText = string.Empty;
        private string _errorText = string.Empty;

        protected FinancePageViewModel(IApiClient api)
        {
            Api = api;
        }

        protected IApiClient Api { get; }

        public bool IsBusy
        {
            get { return _isBusy; }
            protected set { SetProperty(ref _isBusy, value); }
        }

        public string StatusText
        {
            get { return _statusText; }
            protected set { SetProperty(ref _statusText, value); }
        }

        public string ErrorText
        {
            get { return _errorText; }
            protected set { SetProperty(ref _errorText, value); }
        }

        /// <summary>统一执行异步动作：忙碌标记 + 异常转译为 ErrorText。</summary>
        protected async Task RunAsync(Func<Task> action, string successMessage)
        {
            IsBusy = true;
            ErrorText = string.Empty;
            try
            {
                await action();
                StatusText = string.IsNullOrEmpty(successMessage) ? string.Empty : DateTime.Now.ToString("HH:mm:ss ") + successMessage;
            }
            catch (ApiClientException ex)
            {
                ErrorText = ex.Message;
            }
            catch (Exception ex)
            {
                ErrorText = "操作失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
