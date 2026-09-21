using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PropertyManagement.Client.Assets.Controls
{
    /// <summary>
    /// 小数输入框（CHG-v1.1.2-08）：可复用控件，解决「输入过程中小数点被吞 / 变成 12.12.00」的根因。
    ///
    /// 根因回顾：原写法为 Text 绑定 + StringFormat + UpdateSourceTrigger=PropertyChanged，
    /// 每次按键都把 decimal 回写并重新格式化，于是 12.12 被格式化成 12.12.00、小数点被吞掉。
    ///
    /// 本控件口径：
    /// 1) 以文本态承载输入 —— 输入过程中不回写格式化；
    /// 2) 只允许数字与 1 个小数点，小数位上限可配（默认 2 位），非法字符与非法粘贴直接拒绝；
    /// 3) 失焦时规范化（12. → 12.00、.5 → 0.50）；
    /// 4) 对外暴露 Value（decimal?）依赖属性，供表单直接绑定。
    /// </summary>
    public class DecimalTextBox : TextBox
    {
        /// <summary>数值（decimal?）；默认双向绑定。</summary>
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(decimal?), typeof(DecimalTextBox),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnValueChanged));

        /// <summary>允许的小数位上限（默认 2 位）。</summary>
        public static readonly DependencyProperty DecimalPlacesProperty =
            DependencyProperty.Register("DecimalPlaces", typeof(int), typeof(DecimalTextBox),
                new PropertyMetadata(2, OnDecimalPlacesChanged));

        private bool _syncing;

        public DecimalTextBox()
        {
            DataObject.AddPastingHandler(this, OnPasting);
        }

        public decimal? Value
        {
            get { return (decimal?)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public int DecimalPlaces
        {
            get { return (int)GetValue(DecimalPlacesProperty); }
            set { SetValue(DecimalPlacesProperty, value); }
        }

        private int MaxDecimals
        {
            get { return DecimalPlaces < 0 ? 0 : DecimalPlaces; }
        }

        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            base.OnPreviewTextInput(e);
            e.Handled = !IsAcceptable(Prospective(e.Text));
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            // 空格等会破坏数字输入，一并拦截（退格/删除/方向键/回车等控制键不受影响）
            if (e.Key == Key.Space)
            {
                e.Handled = true;
            }
        }

        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            base.OnTextChanged(e);
            if (_syncing) { return; }

            // 输入过程中只做合法性过滤与数值同步，不做格式化回写
            if (!IsAcceptable(Text))
            {
                string filtered = Filter(Text);
                _syncing = true;
                int caret = Math.Min(CaretIndex, filtered.Length);
                Text = filtered;
                CaretIndex = caret;
                _syncing = false;
            }
            SyncValueFromText();
        }

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            base.OnLostFocus(e);
            NormalizeText();
        }

        private void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
            {
                e.CancelCommand();
                return;
            }
            string pasted = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
            if (!IsAcceptable(Prospective(pasted)))
            {
                e.CancelCommand();
            }
        }

        private string Prospective(string inserted)
        {
            if (string.IsNullOrEmpty(inserted)) { return Text; }
            int caret = Math.Max(0, Math.Min(CaretIndex, Text.Length));
            return Text.Substring(0, caret) + inserted + Text.Substring(caret);
        }

        /// <summary>合法性判定：只允许数字与 1 个小数点，且小数位不超过上限。</summary>
        private bool IsAcceptable(string text)
        {
            if (string.IsNullOrEmpty(text)) { return true; }
            bool seenDot = false;
            int decimals = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '0' && c <= '9')
                {
                    if (seenDot) { decimals++; }
                    continue;
                }
                if (c == '.' || c == '。')
                {
                    if (seenDot) { return false; }
                    seenDot = true;
                    continue;
                }
                return false;
            }
            return decimals <= MaxDecimals;
        }

        private string Filter(string text)
        {
            var sb = new System.Text.StringBuilder();
            bool seenDot = false;
            int decimals = 0;
            foreach (char raw in text ?? string.Empty)
            {
                char c = raw == '。' ? '.' : raw;
                if (c >= '0' && c <= '9')
                {
                    if (seenDot)
                    {
                        if (decimals >= MaxDecimals) { continue; }
                        decimals++;
                    }
                    sb.Append(c);
                }
                else if (c == '.' && !seenDot)
                {
                    seenDot = true;
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private void SyncValueFromText()
        {
            decimal parsed;
            decimal? value = decimal.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? (decimal?)parsed
                : null;
            if (value == Value) { return; }

            _syncing = true;
            SetCurrentValue(ValueProperty, value);
            _syncing = false;
        }

        private void NormalizeText()
        {
            if (string.IsNullOrEmpty(Text)) { return; }
            decimal parsed;
            if (!decimal.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return;
            }
            string formatted = parsed.ToString("0." + new string('0', MaxDecimals), CultureInfo.InvariantCulture);
            if (formatted == Text) { return; }

            _syncing = true;
            Text = formatted;
            CaretIndex = Text.Length;
            _syncing = false;
            SyncValueFromText();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = (DecimalTextBox)d;
            if (box._syncing) { return; }

            decimal? incoming = (decimal?)e.NewValue;
            decimal parsed;
            decimal? current = decimal.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? (decimal?)parsed
                : null;
            // 用户正在输入（文本与数值已一致）时不回写格式化 —— 修复「小数点被吞」的关键
            if (current == incoming) { return; }

            box._syncing = true;
            box.Text = incoming.HasValue
                ? incoming.Value.ToString("0." + new string('0', box.MaxDecimals), CultureInfo.InvariantCulture)
                : string.Empty;
            box._syncing = false;
        }

        private static void OnDecimalPlacesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = (DecimalTextBox)d;
            if (!string.IsNullOrEmpty(box.Text) && !box.IsAcceptable(box.Text))
            {
                box.Text = box.Filter(box.Text);
            }
        }
    }
}
