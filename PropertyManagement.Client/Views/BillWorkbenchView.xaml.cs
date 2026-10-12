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
            // CHG-v1.5.0-07：弹窗打开瞬间按「页面实际可用宽高」重算尺寸 ——
            // 打开弹窗时页面尺寸并未变化，SizeChanged 不会触发，若不重算会沿用旧值（可能仍大于页面）。
            GenerateOverlay.IsVisibleChanged += (s, e) =>
            {
                if (GenerateOverlay.IsVisible)
                {
                    Dispatcher.BeginInvoke(new Action(ApplyGenerateDialogHeight),
                        System.Windows.Threading.DispatcherPriority.Loaded);
                }
            };
            // CHG-v2.0.0-02：账单级删除子窗口同样在「打开瞬间」按页面可用区重算尺寸
            BillDeleteOverlay.IsVisibleChanged += (s, e) =>
            {
                if (BillDeleteOverlay.IsVisible)
                {
                    Dispatcher.BeginInvoke(new Action(ApplyBillDeleteDialogSize),
                        System.Windows.Threading.DispatcherPriority.Loaded);
                }
            };
        }

        /// <summary>
        /// CHG-v2.0.0-02：账单级删除子窗口尺寸自适应 ——
        /// 与「生成账单」同一口径：以**子窗口所在页面（PageGrid）的实际宽高**为基准
        /// （窗口宽会被左侧导航与页面边距误导，v1.5.0 已踩坑），
        /// 宽 = Clamp(页宽 − 24, 720, 1180)、高 = Min(页高 − 24, 820)，
        /// 正文表格内部滚动、页脚固定，1366×768 与最大化两档都不裁切。
        /// </summary>
        private void ApplyBillDeleteDialogSize()
        {
            if (BillDeleteDialog == null) { return; }
            double availWidth = PageGrid != null && PageGrid.ActualWidth > 0 ? PageGrid.ActualWidth : ActualWidth;
            double availHeight = PageGrid != null && PageGrid.ActualHeight > 0 ? PageGrid.ActualHeight : ActualHeight;
            if (availWidth <= 0 || availHeight <= 0)
            {
                Window host = Window.GetWindow(this);
                availWidth = host == null || host.ActualWidth <= 0 ? 1366d : host.ActualWidth;
                availHeight = host == null || host.ActualHeight <= 0 ? 768d : host.ActualHeight;
            }
            BillDeleteDialog.Width = Math.Max(720d, Math.Min(1180d, availWidth - 24d));
            BillDeleteDialog.MaxHeight = Math.Min(820d, Math.Max(360d, availHeight - 24d));
        }

        /// <summary>CHG-v2.0.0-02：子窗口行勾选写回（只读 DataGrid 的 CheckBox 不写回源）。</summary>
        private void BillDeleteRowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is BillDeleteRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
        }

        /// <summary>行勾选写回：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，需在点击时显式同步到行对象。</summary>
        private void RowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is BillBatchRow row)
            {
                row.IsChecked = cb.IsChecked == true;
            }
        }

        /// <summary>
        /// CHG-v1.4.0-08（负责人 2026-10-08 反馈「生成账单浮层最大化后缴费对象表格只显示 3 行」）：
        /// 浮层高度随窗口自适应 —— 可用高度 = 窗口高度 − 上下留白（上限 900、下限 620）。
        /// 根因：浮层内滚动区原为固定 `MaxHeight=700`、缴费对象表格固定 `MaxHeight=150`
        /// （150 − 表头 48 = 102 → 32px 行高只能放 3 行），与本机窗口尺寸无关。
        /// 现表格上限放宽到 8 行（304 = 8×32 + 表头 48），滚动区上限随窗口增长，
        /// 1366×768 下仍保证底部按钮与滚动条不被遮挡。
        ///
        /// CHG-v1.5.0-02（负责人 2026-10-11 反馈「1366×768 下数据表格最后一列『缴费人』看不到」）：
        /// 宽度同样改为随窗口自适应 —— 弹窗原为**固定 Width=1000**，而表格列宽合计 1024px
        /// （勾选 48 + 缴费对象 170 + 规格 212 + 单价 130 + 计量 196 + 预估金额 100 + 状态 88 + 缴费人 80），
        /// 内容区可用 ≈ 946px，末列被整列挤出可视区（与窗口大小无关，因弹窗宽度写死）。
        /// 现口径：宽度 = Clamp(宿主宽度 − 48, 980, 1240)，最大化时放宽到上限，1366×768 下也能完整显示末列。
        ///
        /// CHG-v1.5.0-06（负责人 2026-10-11 反馈「窗口未最大化时生成账单页仍被遮挡」）：
        /// 上一轮只按「最大化」场景收口：高度下限写死 620、宽度下限写死 980 —— 窗口未最大化（宿主更小）时
        /// 弹窗反而比宿主还大，上下/左右都被切掉。现改为**真自适应**：
        /// 高度 = Clamp(宿主高度 − 96, 300, 900)（内部滚动，绝不高过宿主），
        /// 宽度 = Clamp(宿主宽度 − 24, 520, 1240)；表格列改用「星宽 + 最小宽」自动收放，
        /// 窄窗口下末列「缴费人」同样完整可见。
        /// </summary>
        private void PageGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyGenerateDialogHeight();
            ApplyBillDeleteDialogSize();
        }

        private void ApplyGenerateDialogHeight()
        {
            if (GenerateDialogScroll == null) { return; }
            // CHG-v1.5.0-07（负责人 2026-10-11 反馈「非最大化下弹窗仍被左右切掉」）：
            // 根因是**参考宽度取错了对象** —— 上一版用「宿主窗口宽度 − 24」（窗口 ≈1366 → 弹窗 1240），
            // 但弹窗实际居中在「页面可用区」（窗口扣掉左侧导航与页面外边距后仅约 1000～1100），
            // 于是弹窗比可用区更宽、左右各被切掉一截（现场表现为标题「生成账单（草稿）」只剩「单（草稿）」）。
            // 现口径：一律以**弹窗所在页面（PageGrid）的实际宽高**为准，弹窗绝不超出页面可用区。
            double availWidth = PageGrid != null && PageGrid.ActualWidth > 0 ? PageGrid.ActualWidth : ActualWidth;
            double availHeight = PageGrid != null && PageGrid.ActualHeight > 0 ? PageGrid.ActualHeight : ActualHeight;
            if (availWidth <= 0 || availHeight <= 0)
            {
                Window host = Window.GetWindow(this);
                availWidth = host == null || host.ActualWidth <= 0 ? 1366d : host.ActualWidth;
                availHeight = host == null || host.ActualHeight <= 0 ? 768d : host.ActualHeight;
            }

            if (GenerateDialog != null)
            {
                // 高宽都按页面可用区自适应（各留 12），弹窗整体始终在页面内；
                // 正文超出时由内部 ScrollViewer 承载，「生成草稿 / 取消」页脚固定在弹窗底部。
                GenerateDialog.MaxHeight = Math.Min(900d, Math.Max(320d, availHeight - 24d));
                GenerateDialog.Width = Math.Max(520d, Math.Min(1240d, availWidth - 24d));
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

        /// <summary>
        /// CHG-v1.2.0-18：缴费对象行「规格手选」写回。
        ///
        /// 根因（负责人 2026-09-21 反馈「逐行手选无法匹配其它规格」，界面实测复现）：
        /// 本表格 `IsReadOnly="True"` —— **只读 DataGrid 的模板单元格里，控件的双向绑定不会回写源**
        /// （改选下拉框后，行对象的 SelectedSpec 仍是「自动匹配」，金额自然不变）。
        /// 与勾选框 / 手填计量 / 出账改价三处同口径：显示用 OneWay，选择变化由本处理器显式同步到行对象。
        /// </summary>
        private void RowSpecComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            var row = combo == null ? null : combo.DataContext as BillObjectRow;
            if (row == null) { return; }
            var option = combo.SelectedItem as BillSpecOption;
            if (option == null || ReferenceEquals(row.SelectedSpec, option)) { return; }
            row.SelectedSpec = option;
        }
    }
}
