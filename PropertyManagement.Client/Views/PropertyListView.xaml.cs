using System.Windows;
using System.Windows.Controls;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    public partial class PropertyListView : UserControl
    {
        public PropertyListView()
        {
            InitializeComponent();
        }

        /// <summary>行勾选写回：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，需在点击时显式同步到行对象。</summary>
        private void RowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is PropertyRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
        }
    }
}
