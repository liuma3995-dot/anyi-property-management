using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PropertyManagement.Client.ViewModels;
using PropertyManagement.Contract.Common;

namespace PropertyManagement.Client.Views
{
    public partial class PropertyListView : UserControl
    {
        public PropertyListView()
        {
            InitializeComponent();
            // F-01：面积框为纯数值输入，关闭输入法（避免中文输入法把「.」送成「。」）
            InputMethod.SetIsInputMethodEnabled(AreaBox, false);
        }

        // CHG-v1.1.2-08：面积框的按键/粘贴过滤已收敛到公共控件 Assets\Controls\DecimalTextBox，
        // 原页面局部实现（AreaBox_PreviewTextInput / AreaBox_Pasting）随之下线。
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
