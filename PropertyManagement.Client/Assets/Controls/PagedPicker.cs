using System;
using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PropertyManagement.Client.Assets.Controls
{
    /// <summary>
    /// 可检索 + 可翻页的单选下拉（CHG-v1.3.1-01）。
    ///
    /// 背景（负责人 2026-09-23）：「业主-房产关系 → 手动绑定」的房产 / 业主下拉框只取服务端前 100 条，
    /// 检索又是在这 100 条里做本地过滤 —— 数据量一大就「只显示一部分、搜什么都搜不到」。
    ///
    /// 本控件的口径：
    /// 1) 输入框里的文字由使用方（ViewModel）持有，防抖后走**服务端检索**，控件只渲染当前这一页；
    /// 2) 下拉浮层**内部**带翻页行（共 N 条 · 第 X/Y 页 + 上一页 / 下一页），翻页命令由使用方提供；
    /// 3) 选中口径由使用方的 SelectedItem 承载：集合重建导致的"选择复位"（null）一律忽略，
    ///    避免旧版「过滤刷新把已选值清空 → 保存失败」的老问题复现；
    /// 4) 浮层用 Popup + StaysOpen=False：点浮层内部（含翻页按钮）不收起，点浮层以外才收起。
    /// </summary>
    public class PagedPicker : Control
    {
        /// <summary>当前页条目（由 ViewModel 提供）。</summary>
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(PagedPicker),
                new PropertyMetadata(null, OnItemsSourceChanged));

        /// <summary>选中项（双向；null 表示"未选"，由使用方按需忽略）。</summary>
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register("SelectedItem", typeof(object), typeof(PagedPicker),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

        /// <summary>展示字段名（如 UnitPath / OwnerDisplayName）。</summary>
        public static readonly DependencyProperty DisplayMemberPathProperty =
            DependencyProperty.Register("DisplayMemberPath", typeof(string), typeof(PagedPicker),
                new PropertyMetadata(string.Empty));

        /// <summary>输入框文字（双向；使用方据此做服务端检索）。</summary>
        public static readonly DependencyProperty SearchTextProperty =
            DependencyProperty.Register("SearchText", typeof(string), typeof(PagedPicker),
                new FrameworkPropertyMetadata(string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        /// <summary>输入框占位提示。</summary>
        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register("Placeholder", typeof(string), typeof(PagedPicker),
                new PropertyMetadata(string.Empty));

        /// <summary>页脚文案（如「共 349 条 · 第 1/18 页」）。</summary>
        public static readonly DependencyProperty PageTextProperty =
            DependencyProperty.Register("PageText", typeof(string), typeof(PagedPicker),
                new PropertyMetadata(string.Empty));

        /// <summary>上一页命令（使用方提供；不可用时按钮自动置灰）。</summary>
        public static readonly DependencyProperty PrevCommandProperty =
            DependencyProperty.Register("PrevCommand", typeof(ICommand), typeof(PagedPicker),
                new PropertyMetadata(null));

        /// <summary>下一页命令（使用方提供；不可用时按钮自动置灰）。</summary>
        public static readonly DependencyProperty NextCommandProperty =
            DependencyProperty.Register("NextCommand", typeof(ICommand), typeof(PagedPicker),
                new PropertyMetadata(null));

        /// <summary>下拉浮层是否展开（双向）。</summary>
        public static readonly DependencyProperty IsDropDownOpenProperty =
            DependencyProperty.Register("IsDropDownOpen", typeof(bool), typeof(PagedPicker),
                new FrameworkPropertyMetadata(false,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        private TextBox _searchBox;
        private ListBox _list;
        private Popup _popup;
        private ToggleButton _toggle;
        private DateTime _lastPopupClose = DateTime.MinValue;
        private bool _syncing;

        public IEnumerable ItemsSource
        {
            get { return (IEnumerable)GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }

        public object SelectedItem
        {
            get { return GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }

        public string DisplayMemberPath
        {
            get { return (string)GetValue(DisplayMemberPathProperty); }
            set { SetValue(DisplayMemberPathProperty, value); }
        }

        public string SearchText
        {
            get { return (string)GetValue(SearchTextProperty); }
            set { SetValue(SearchTextProperty, value); }
        }

        public string Placeholder
        {
            get { return (string)GetValue(PlaceholderProperty); }
            set { SetValue(PlaceholderProperty, value); }
        }

        public string PageText
        {
            get { return (string)GetValue(PageTextProperty); }
            set { SetValue(PageTextProperty, value); }
        }

        public ICommand PrevCommand
        {
            get { return (ICommand)GetValue(PrevCommandProperty); }
            set { SetValue(PrevCommandProperty, value); }
        }

        public ICommand NextCommand
        {
            get { return (ICommand)GetValue(NextCommandProperty); }
            set { SetValue(NextCommandProperty, value); }
        }

        public bool IsDropDownOpen
        {
            get { return (bool)GetValue(IsDropDownOpenProperty); }
            set { SetValue(IsDropDownOpenProperty, value); }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            DetachTemplateParts();

            _searchBox = GetTemplateChild("PART_SearchBox") as TextBox;
            _list = GetTemplateChild("PART_List") as ListBox;
            _popup = GetTemplateChild("PART_Popup") as Popup;
            _toggle = GetTemplateChild("PART_Toggle") as ToggleButton;

            if (_searchBox != null)
            {
                // CHG-v1.3.1-02（现场反馈：点输入框"闪现一下下拉框"且打不了字）：
                // 浮层必须在**鼠标抬起之后**才打开 —— StaysOpen=False 的 Popup 一旦在"按下"阶段打开，
                // 会立刻捕获鼠标，把这次点击剩下的"抬起"判成"点在外面"→ 浮层瞬间收起（闪现），
                // 输入框同时丢掉键盘焦点（后续按键无处可去）。
                _searchBox.PreviewMouseLeftButtonUp += OnSearchBoxClickUp;
                // 键盘输入同样展开（程序化回填文字不会走到这里）
                _searchBox.PreviewTextInput += OnSearchBoxActivated;
                _searchBox.PreviewKeyDown += OnSearchBoxKeyDown;
            }
            if (_list != null)
            {
                _list.SelectionChanged += OnListSelectionChanged;
                _list.PreviewMouseLeftButtonUp += OnListMouseUp;
            }
            if (_popup != null)
            {
                _popup.Opened += OnPopupOpened;
                _popup.Closed += OnPopupClosed;
            }
            if (_toggle != null)
            {
                _toggle.Click += OnToggleClick;
            }
            SyncListSelection();
        }

        private void DetachTemplateParts()
        {
            if (_searchBox != null)
            {
                _searchBox.PreviewMouseLeftButtonUp -= OnSearchBoxClickUp;
                _searchBox.PreviewTextInput -= OnSearchBoxActivated;
                _searchBox.PreviewKeyDown -= OnSearchBoxKeyDown;
            }
            if (_list != null)
            {
                _list.SelectionChanged -= OnListSelectionChanged;
                _list.PreviewMouseLeftButtonUp -= OnListMouseUp;
            }
            if (_popup != null)
            {
                _popup.Opened -= OnPopupOpened;
                _popup.Closed -= OnPopupClosed;
            }
            if (_toggle != null)
            {
                _toggle.Click -= OnToggleClick;
            }
        }

        private void OnSearchBoxActivated(object sender, EventArgs e)
        {
            if (!IsDropDownOpen) { IsDropDownOpen = true; }
            if (_searchBox != null) { _searchBox.Focus(); }
        }

        /// <summary>点输入框：抬起后再展开浮层，并把键盘焦点锁在输入框上（保证"点一下就能打字"）。</summary>
        private void OnSearchBoxClickUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) { return; }
            // 再推迟一个调度器节拍，确保这次点击（含焦点设置）彻底走完，浮层不会自我关闭
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsDropDownOpen) { IsDropDownOpen = true; }
                if (_searchBox != null) { _searchBox.Focus(); }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                IsDropDownOpen = false;
                return;
            }
            // 退格 / 删除 / 下方向键同样算"用户要选东西"
            if ((e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Down) && !IsDropDownOpen)
            {
                IsDropDownOpen = true;
            }
        }

        private void OnToggleClick(object sender, RoutedEventArgs e)
        {
            if (IsDropDownOpen)
            {
                IsDropDownOpen = false;
                return;
            }
            // StaysOpen=False 的焦点陷阱：点击展开按钮时浮层先被关掉，紧接着 Click 又会打开 → 抖动。
            // 这里用"刚刚自动关闭"的时间窗把这次抖动吃掉。
            if ((DateTime.Now - _lastPopupClose).TotalMilliseconds < 250) { return; }
            IsDropDownOpen = true;
        }

        private void OnPopupOpened(object sender, EventArgs e)
        {
            SyncListSelection();
        }

        private void OnPopupClosed(object sender, EventArgs e)
        {
            _lastPopupClose = DateTime.Now;
            if (IsDropDownOpen) { IsDropDownOpen = false; }
        }

        private void OnListMouseUp(object sender, MouseButtonEventArgs e)
        {
            // 点到条目上才收起；点在浮层空白处/翻页行上保持展开（方便连翻几页）
            if (_list == null) { return; }
            var src = (e.OriginalSource as DependencyObject) ?? (e.Source as DependencyObject);
            var item = ItemsControl.ContainerFromElement(_list, src) as ListBoxItem;
            if (item != null) { IsDropDownOpen = false; }
        }

        private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _list == null) { return; }
            // 集合重建（服务端检索返回新一页）会让 ListBox 选择复位成 null —— 一律忽略，
            // 真正的"未选"只由使用方把 SelectedItem 置空表达。
            if (_list.SelectedItem == null) { return; }
            SelectedItem = _list.SelectedItem;
        }

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var picker = (PagedPicker)d;
            picker.SyncListSelection();
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var picker = (PagedPicker)d;
            if (e.OldValue is System.Collections.Specialized.INotifyCollectionChanged)
            {
                ((System.Collections.Specialized.INotifyCollectionChanged)e.OldValue).CollectionChanged -= picker.OnItemsCollectionChanged;
            }
            if (e.NewValue is System.Collections.Specialized.INotifyCollectionChanged)
            {
                ((System.Collections.Specialized.INotifyCollectionChanged)e.NewValue).CollectionChanged += picker.OnItemsCollectionChanged;
            }
            picker.SyncListSelection();
        }

        private void OnItemsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // 换页后把选中行重新高亮（按展示文本匹配，跨页面实例也能对上）
            SyncListSelection();
        }

        /// <summary>把当前选中项在下拉里高亮：集合重建后按展示文本重新匹配。</summary>
        private void SyncListSelection()
        {
            if (_list == null) { return; }
            object selected = SelectedItem;
            if (selected == null)
            {
                _syncing = true;
                try { _list.SelectedItem = null; }
                finally { _syncing = false; }
                return;
            }

            object match = null;
            string want = DisplayText(selected);
            if (_list.ItemsSource != null)
            {
                foreach (object item in _list.ItemsSource)
                {
                    if (string.Equals(DisplayText(item), want, StringComparison.Ordinal))
                    {
                        match = item;
                        break;
                    }
                }
            }
            _syncing = true;
            try { _list.SelectedItem = match; }
            finally { _syncing = false; }
        }

        private string DisplayText(object item)
        {
            if (item == null) { return string.Empty; }
            string path = DisplayMemberPath;
            if (string.IsNullOrEmpty(path)) { return item.ToString() ?? string.Empty; }
            PropertyInfo prop = item.GetType().GetProperty(path);
            if (prop == null) { return item.ToString() ?? string.Empty; }
            object value = prop.GetValue(item, null);
            return value == null ? string.Empty : value.ToString();
        }
    }
}
