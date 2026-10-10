using System;
using System.Windows;
using System.Windows.Controls;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>
    /// CHG-v1.4.1-14：把页面 ViewModel 的「定位行」请求接到 DataGrid —— 选中该行并滚动到可视区。
    /// 欠费台账 / 设备到期提醒 / 纠纷列表三页共用（避免各写一份 code-behind 订阅逻辑）。
    /// </summary>
    internal static class RowFocusAdapter
    {
        public static void Bind(FrameworkElement view, DataGrid grid)
        {
            if (view == null || grid == null) { return; }

            IFocusTargetHost subscribed = null;
            Action<object> handler = row =>
            {
                if (row == null) { return; }
                grid.UpdateLayout();
                grid.SelectedItem = row;
                grid.ScrollIntoView(row);
            };

            view.DataContextChanged += (sender, e) =>
            {
                if (subscribed != null) { subscribed.FocusRowRequested -= handler; }
                subscribed = e.NewValue as IFocusTargetHost;
                if (subscribed != null) { subscribed.FocusRowRequested += handler; }
            };
        }
    }
}
