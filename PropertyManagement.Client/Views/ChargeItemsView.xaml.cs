using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>收费项目维护页（PG-FIN-01）。</summary>
    public partial class ChargeItemsView : UserControl
    {
        public ChargeItemsView()
        {
            InitializeComponent();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ChargeItemsViewModel vm)
            {
                _ = vm.SearchCommand.ExecuteAsync(null);
            }
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is ChargeItemsViewModel vm)
            {
                _ = vm.SearchCommand.ExecuteAsync(null);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("导出功能随报表模块统一提供（M4-D4-6，Excel/PDF）。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}