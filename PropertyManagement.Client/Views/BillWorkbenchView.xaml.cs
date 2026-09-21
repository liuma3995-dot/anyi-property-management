using System;
using System.Windows;
using System.Windows.Controls;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>账单工作台页（PG-FIN-02）。</summary>
    public partial class BillWorkbenchView : UserControl
    {
        public BillWorkbenchView()
        {
            InitializeComponent();
        }

        /// <summary>行勾选写回：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，需在点击时显式同步到行对象。</summary>
        private void RowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is BillBatchRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
        }

        /// <summary>CHG-v1.1.0-10：缴费对象勾选写回（生成账单弹窗内只读 DataGrid）。</summary>
        private void ObjectCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is BillObjectRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
        }

        /// <summary>
        /// CHG-v1.1.2-34：缴费对象行内「手填」计量参数写回。
        /// 只读 DataGrid 中的模板控件不参与数据网格编辑，故与勾选框同口径显式同步到行对象。
        /// </summary>
        private void MeasureTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var box = sender as TextBox;
            var input = box == null ? null : box.DataContext as BillMeasureInputRow;
            if (input == null) { return; }
            string text = box.Text ?? string.Empty;
            if (!string.Equals(input.ValueText ?? string.Empty, text, StringComparison.Ordinal))
            {
                input.ValueText = text;
            }
        }

        /// <summary>
        /// CHG-v1.1.2-50：缴费对象行「出账改价」输入写回。
        /// 与「手填计量」同口径 —— 只读 DataGrid 的模板控件不参与数据网格编辑，故显式同步到行对象。
        /// </summary>
        private void PriceOverrideTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var box = sender as TextBox;
            var row = box == null ? null : box.DataContext as BillObjectRow;
            if (row == null) { return; }
            string text = box.Text ?? string.Empty;
            if (!string.Equals(row.UnitPriceOverrideText ?? string.Empty, text, StringComparison.Ordinal))
            {
                row.UnitPriceOverrideText = text;
            }
        }
    }
}
