using System.Windows.Controls;

namespace PropertyManagement.Client.Views
{
    /// <summary>仪表盘首页（PG-DASH）。</summary>
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
            // CHG-v1.4.1-01：月份浮层降出置顶带（WPF Popup 默认是 WS_EX_TOPMOST 独立顶层窗口）
            MonthPickerPopup.Opened += (sender, args) =>
                Services.NativeWindowInterop.DemotePopupFromTopmost(MonthPickerPopup);
        }
    }
}
