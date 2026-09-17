using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>退款/减免/调整页（PG-FIN-04）。</summary>
    public partial class RefundAdjustmentView : UserControl
    {
        public RefundAdjustmentView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// CHG-v1.1.0-16：关联账单多选浮层的展开/收起（唯一状态入口）。
        /// 说明：原实现为 ToggleButton.IsChecked TwoWay + Popup.StaysOpen=False：
        /// 点击按钮时 Popup 先按「点外关闭」把 IsOpen 置 false，随后 ToggleButton 的点击又把 IsChecked 翻转回 true，
        /// 于是浮层永远收不起来。现改为：Popup 常驻不自动关（StaysOpen=True），
        /// 由本处理器统一翻转状态，页面其它区域点击由 Root_PreviewMouseDown 收起。
        /// </summary>
        private void BillPickerToggle_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is RefundAdjustmentViewModel vm)
            {
                vm.IsBillPickerOpen = !vm.IsBillPickerOpen;
            }
        }

        /// <summary>点击选择器之外的区域时收起浮层（Popup 为独立窗口，故只在页面内处理）。</summary>
        private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!(DataContext is RefundAdjustmentViewModel vm) || !vm.IsBillPickerOpen) { return; }
            var source = e.OriginalSource as DependencyObject;
            if (IsDescendantOf(RefundBillSelector, source)) { return; }   // 选择器自身：交给 Click 处理
            vm.IsBillPickerOpen = false;
        }

        private static bool IsDescendantOf(DependencyObject ancestor, DependencyObject node)
        {
            if (ancestor == null || node == null) { return false; }
            var current = node;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor)) { return true; }
                current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return false;
        }
    }
}
