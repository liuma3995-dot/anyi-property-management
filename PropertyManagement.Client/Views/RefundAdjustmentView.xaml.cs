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

        /// <summary>记录表列头高度（与 Theme.xaml 的 DataGridColumnHeader.Height 一致）。</summary>
        private const double RecordsHeaderHeight = 36;

        /// <summary>记录表行高（与 Theme.xaml 的 DataGrid.RowHeight 一致）。</summary>
        private const double RecordsRowHeight = 44;

        /// <summary>记录区标题行高度（与 XAML 第一行 RowDefinition 一致）。</summary>
        private const double RecordsTitleHeight = 32;

        /// <summary>保底可见行数：1366×768 下至少 2 整行。</summary>
        private const int RecordsMinRows = 2;

        private void PageGrid_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyRecordsTableHeight();
        }

        /// <summary>
        /// CHG-v1.1.2-45：记录表高度随窗口尺寸自适应 ——
        /// 「表头 + N 整行」，N = 可用高度能放下的完整行数（下限 2 行，见 RecordsMinRows）。
        /// 口径依据：上一轮仅保证 1366×768 显示 2 行，但**最大化后应把可用高度用满**（能放几行展示几行，
        /// 其余滚动），因此这里改为按可用高度计算，既不会在宽屏下浪费空间，也不会留下被切一半的行。
        /// </summary>
        private void PageGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyRecordsTableHeight();
        }

        private void ApplyRecordsTableHeight()
        {
            if (PageGrid == null || RecordsCard == null || RecordsGrid == null) { return; }
            if (PageGrid.RowDefinitions.Count < 3) { return; }

            // 页面可用高度＝本控件（页面）的真实高度减去页面外边距 —— 该高度由外壳宿主决定，与记录表高度无关，
            // 因此不会自反馈（不能取「记录区行高」：它由卡片内容决定，实测会一路涨到十几行）。
            double viewHeight = ActualHeight > 0
                ? ActualHeight
                : (Parent as FrameworkElement) == null ? 0 : ((FrameworkElement)Parent).ActualHeight;
            double pageHeight = viewHeight - PageGrid.Margin.Top - PageGrid.Margin.Bottom;
            if (pageHeight <= 0) { return; }

            // 扣除记录区之外各行（表单/Auto 行、间距、状态条）已占用高度 → 得到记录区可用高度
            double used = 0;
            foreach (System.Windows.Controls.RowDefinition row in PageGrid.RowDefinitions)
            {
                if (row != PageGrid.RowDefinitions[2]) { used += row.ActualHeight; }
            }
            double available = pageHeight - used;
            if (available <= 0) { return; }

            double forRows = available - RecordsTitleHeight - RecordsHeaderHeight;
            int rows = (int)System.Math.Floor(forRows / RecordsRowHeight);
            if (rows < RecordsMinRows) { rows = RecordsMinRows; }

            double tableHeight = RecordsHeaderHeight + rows * RecordsRowHeight;
            // 注意：未显式设高的控件 Height 为 NaN，NaN 参与比较恒为 false —— 必须先判 NaN，否则高度永远设不上去。
            if (double.IsNaN(RecordsGrid.Height) || System.Math.Abs(RecordsGrid.Height - tableHeight) > 0.5)
            {
                RecordsGrid.Height = tableHeight;
                RecordsCard.Height = RecordsTitleHeight + tableHeight;
            }
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
