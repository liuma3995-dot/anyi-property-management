using System.Windows;
using PropertyManagement.Client.Services;
using PropertyManagement.Client.ViewModels;
using PropertyManagement.Contract.Auth;

namespace PropertyManagement.Client.Views
{
    /// <summary>
    /// 个人信息设置（R17）：顶栏管理员下拉进入，小表单就地填写，不新增独立页面。
    /// 保存成功后刷新顶栏（名字/头像），并保留主窗口不跳转。
    /// </summary>
    public partial class ProfileWindow : Window
    {
        private readonly ProfileViewModel _vm;

        public ProfileWindow(IApiClient api)
        {
            InitializeComponent();

            _vm = new ProfileViewModel(api);
            _vm.Saved += OnSaved;
            DataContext = _vm;
        }

        /// <summary>保存成功（供主窗口回填顶栏）。</summary>
        public event System.Action<UserProfileDto> ProfileSaved;

        private void OnSaved(UserProfileDto profile)
        {
            System.Action<UserProfileDto> handler = ProfileSaved;
            if (handler != null)
            {
                handler(profile);
            }
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
