using System.Windows;
using System.Windows.Controls;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>收款登记页（PG-FIN-03）。</summary>
    public partial class PaymentEntryView : UserControl
    {
        public PaymentEntryView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// CHG-v1.1.0-12：应缴明细勾选写回。
        /// 只读 DataGrid 中 CheckBox 的 IsChecked 绑定不写回源，点击时显式同步到行对象。
        /// </summary>
        private void BillCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is PaymentBillRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
            // CHG-v1.1.0-20：用户主动勾选 → 解锁收据预览（回到实时预览，不再停留在上一次收款结果）
            (DataContext as PaymentEntryViewModel)?.NotifyUserChangedSelection();
        }

        /// <summary>
        /// CHG-v1.1.0-20：用户在应缴明细中主动切换选中账单 → 解锁收据预览。
        /// 仅由界面交互触发（内部列表刷新不经过本处理器），因此不会覆盖收款结果卡片。
        /// </summary>
        private void BillsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            (DataContext as PaymentEntryViewModel)?.NotifyUserChangedSelection();
        }
    }
}
