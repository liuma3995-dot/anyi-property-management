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

        /// <summary>
        /// F-01：建筑面积按键过滤——只放行数字与一个小数点，全角数字/句点先归一化为半角。
        /// 允许尾随小数点（<c>123.</c> 为合法中间态）；超过 2 位小数交给 ViewModel 出行内提示。
        /// </summary>
        private void AreaBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;
            string normalized = AreaValue.Normalize(e.Text);
            if (normalized.Length == 0) { e.Handled = true; return; }
            if (!IsAreaText(Concat(box, normalized))) { e.Handled = true; return; }
            if (normalized == e.Text) return;   // 常规输入交给 WPF
            e.Handled = true;
            int caret = box.SelectionStart;
            box.Text = box.Text.Remove(caret, box.SelectionLength).Insert(caret, normalized);
            box.CaretIndex = caret + normalized.Length;
        }

        /// <summary>F-01：粘贴过滤——全角/空白/千分位先清洗，非法内容直接丢弃并保留原文本。</summary>
        private void AreaBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) { e.CancelCommand(); return; }
            string raw = e.DataObject.GetData(DataFormats.UnicodeText) as string;
            string normalized = AreaValue.Normalize(raw);
            if (normalized.Length == 0 || !IsAreaText(Concat(box, normalized))) { e.CancelCommand(); return; }
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, normalized);
            e.DataObject = data;
        }

        /// <summary>过滤判据：仅数字与**一个**小数点（小数位数不在此拦截，由 ViewModel 给提示）。</summary>
        private static bool IsAreaText(string text)
        {
            int dots = 0;
            foreach (char c in text)
            {
                if (c == '.') { dots++; if (dots > 1) return false; continue; }
                if (c < '0' || c > '9') return false;
            }
            return true;
        }

        private static string Concat(TextBox box, string insert)
        {
            return box.Text.Remove(box.SelectionStart, box.SelectionLength).Insert(box.SelectionStart, insert);
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
