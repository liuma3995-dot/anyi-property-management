using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>支出登记页（PG-FIN-05）。</summary>
    public partial class ExpenseView : UserControl
    {
        public ExpenseView()
        {
            InitializeComponent();
            // CHG-v2.0.0-11：「导出」下拉的定位/尺寸在打开瞬间与页面尺寸变化时重算
            // （页面尺寸没变时 SizeChanged 不触发，必须在打开瞬间显式算一次）
            ExportMenuLayer.IsVisibleChanged += (s, e) =>
            {
                if (ExportMenuLayer.IsVisible)
                {
                    Dispatcher.BeginInvoke(new System.Action(UpdateExportMenuPlacement),
                        System.Windows.Threading.DispatcherPriority.Loaded);
                }
            };
        }

        /// <summary>
        /// CHG-v2.0.0-11（负责人 2026-10-12 反馈「导出按钮点击后无法收起」）：
        /// 「导出 ▾」按钮 → 展开/收起下拉。
        /// 打开时下拉铺一层透明遮罩：点空白处、点「导出」本身或点「登记支出」都会先把下拉收起，
        /// 因此不会出现“按了没反应”或“下拉压住表单”的情况（遮罩吃掉的那一次点击不会被下层的按钮执行）。
        /// </summary>
        private void ExportMenu_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ExpenseViewModel vm)
            {
                vm.ToggleExportMenuCommand.Execute(null);
            }
        }

        private void ExportMenuCatcher_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ExpenseViewModel vm)
            {
                vm.CloseExportMenuCommand.Execute(null);
            }
        }

        private void PageGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateExportMenuPlacement();
        }

        /// <summary>把下拉面板对齐到「导出」按钮正下方（页面可用区坐标；右对齐按钮右缘）。</summary>
        private void UpdateExportMenuPlacement()
        {
            if (PageGrid == null || ExportMenuPanel == null || ExportButton == null) { return; }
            if (PageGrid.ActualWidth <= 0 || PageGrid.ActualHeight <= 0) { return; }
            Point pos = ExportButton.TransformToAncestor(PageGrid).Transform(new Point(0, 0));
            double right = Math.Max(0, PageGrid.ActualWidth - (pos.X + ExportButton.ActualWidth));
            ExportMenuPanel.Margin = new Thickness(0, pos.Y + ExportButton.ActualHeight + 4, right, 0);
        }


        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ExpenseViewModel vm)
            {
                _ = vm.SearchCommand.ExecuteAsync(null);
            }
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is ExpenseViewModel vm)
            {
                _ = vm.SearchCommand.ExecuteAsync(null);
            }
        }

        /// <summary>行勾选写回（v1.1.0-⑤）：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，点击时显式同步。</summary>
        private void ExpenseCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox box && box.DataContext is ExpenseRow row)
            {
                row.IsSelected = box.IsChecked == true;
            }
            if (DataContext is ExpenseViewModel vm) { vm.RefreshSelectAllState(); }
        }
    }
}
