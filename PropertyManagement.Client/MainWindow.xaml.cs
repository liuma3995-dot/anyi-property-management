using System;
using System.Windows;
using PropertyManagement.Client.Services;

namespace PropertyManagement.Client
{
    /// <summary>
    /// 主窗口骨架：左侧导航 + 顶部栏 + 内容区 + 状态栏。
    /// 启动时调用后端健康检查，验证前后端连通（D0-5）。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ApiClient _api = new ApiClient();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var health = await _api.GetHealthAsync();
                if (health != null && string.Equals(health.Status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    StatusText.Text = "后端服务：已连接（" + health.Service + " " + health.Version + "）";
                }
                else
                {
                    StatusText.Text = "后端服务：响应异常";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "后端服务：未连接（" + ex.Message + "）";
            }
        }
    }
}
