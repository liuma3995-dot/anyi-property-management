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
            // CHG-v1.4.1-13/-14：VM 的「定位行」请求 → 选中并滚动到可视区（三页共用同一适配器）
            RowFocusAdapter.Bind(this, ArrearsGrid);
        }

        /// <summary>行勾选写回：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，需在点击时显式同步到行对象。</summary>
        private void RowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ArrearRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
        }

        private void KeywordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ArrearViewModel vm)
            {
                _ = vm.SearchCommand.ExecuteAsync(null);
            }
        }

        // CHG-v1.4.0-21：批量催缴入口与占位处理器已下线（功能未实现，负责人裁定下线）
    }
}
