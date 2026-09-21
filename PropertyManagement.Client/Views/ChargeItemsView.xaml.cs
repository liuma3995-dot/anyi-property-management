using System.ComponentModel;
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
            DataContextChanged += OnDataContextChanged;
        }

        /// <summary>
        /// CHG-v1.1.2-26：批量删除的勾选框列默认不显示，点击「批量删除」后才出现。
        /// DataGridColumn 不在可视化树内，无法用 RelativeSource 绑定，故由代码统一控制列可见性。
        /// </summary>
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyPropertyChanged oldVm)
            {
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            }
            if (e.NewValue is INotifyPropertyChanged newVm)
            {
                newVm.PropertyChanged += OnViewModelPropertyChanged;
            }
            UpdateCheckColumnVisibility();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChargeItemsViewModel.IsBatchMode) || string.IsNullOrEmpty(e.PropertyName))
            {
                UpdateCheckColumnVisibility();
            }
        }

        private void UpdateCheckColumnVisibility()
        {
            Visibility visibility = (DataContext as ChargeItemsViewModel) != null &&
                                    ((ChargeItemsViewModel)DataContext).IsBatchMode
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (ItemCheckColumn != null) { ItemCheckColumn.Visibility = visibility; }
            if (SpecCheckColumn != null) { SpecCheckColumn.Visibility = visibility; }
        }

        /// <summary>行勾选写回：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，需在点击时显式同步到行对象。</summary>
        private void RowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ChargeItemRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ChargeItemsViewModel vm)
            {
                _ = vm.SearchCommand.ExecuteAsync(null);
            }
        }

        /// <summary>CHG-v1.1.2-26：价目表规格行勾选写回（只读 DataGrid 的 CheckBox 不自动回写源）。</summary>
        private void SpecCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ChargeSpecRow row)
            {
                row.IsChecked = cb.IsChecked == true;
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
                        MessageBox.Show("导出功能随报表模块统一提供（Excel/PDF）。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
