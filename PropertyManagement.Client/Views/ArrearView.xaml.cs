using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>欠费台账页（PG-FIN-06）。</summary>
    public partial class ArrearView : UserControl
    {
        public ArrearView()
        {
            InitializeComponent();
        }

        private void KeywordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ArrearViewModel vm)
            {
                _ = vm.SearchCommand.ExecuteAsync(null);
            }
        }

        private void CollectButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("请在「收款登记」页办理收款（PG-FIN-03）。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BatchRemindButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("请逐条点击【催缴】选择渠道并登记（当前单机版支持单条催缴留痕）。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("导出功能随报表模块统一提供（M4-D4-6，Excel/PDF）。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
