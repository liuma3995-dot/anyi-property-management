using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>备份与恢复页（PG-COM-02）。
    /// 高危恢复弹层内 HandyControl PasswordBox 的 Password 为 CLR 属性（非依赖属性），
    /// 经内部 ActualPasswordBox 的 PasswordChanged 事件回填 VM（同 ChangePasswordWindow 方案，避免 XamlParseException）。</summary>
    public partial class BackupView : UserControl
    {
        private bool _wired;

        public BackupView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as BackupViewModel;
            if (vm == null) return;
            vm.PropertyChanged -= OnVmPropertyChanged;
            vm.PropertyChanged += OnVmPropertyChanged;

            WireAll(vm);
            if (!_wired)
            {
                Dispatcher.BeginInvoke(new System.Action(() => WireAll(vm)),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as BackupViewModel;
            if (vm == null) return;
            vm.PropertyChanged -= OnVmPropertyChanged;
        }

        private void WireAll(BackupViewModel vm)
        {
            if (_wired) return;
            if (OperationPasswordInput == null) return;
            if (OperationPasswordInput.ActualPasswordBox == null) return;

            Wire(OperationPasswordInput, () => vm.OperationPassword, value => vm.OperationPassword = value);
            _wired = true;
        }

        private void Wire(HandyControl.Controls.PasswordBox box, System.Func<string> getter, System.Action<string> setter)
        {
            var inner = box.ActualPasswordBox;
            if (inner == null) return;
            inner.PasswordChanged += (s, e) =>
            {
                if (!string.Equals(box.Password, getter(), System.StringComparison.Ordinal))
                {
                    setter(box.Password);
                }
            };
        }

        /// <summary>VM 清空密码（取消/恢复成功）→ 同步清空密码框。</summary>
        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var vm = (BackupViewModel)sender;
            if (e.PropertyName == "OperationPassword" && string.IsNullOrEmpty(vm.OperationPassword))
            {
                ClearIfSet(OperationPasswordInput);
            }
        }

        private static void ClearIfSet(HandyControl.Controls.PasswordBox box)
        {
            if (box != null && box.Password.Length > 0)
            {
                box.Password = string.Empty;
            }
        }

        /// <summary>行勾选写回（R13）：只读 DataGrid 中 CheckBox 的 IsChecked 绑定不会写回源，点击时显式同步。</summary>
        private void RecordCheck_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox box && box.DataContext is BackupRecordRow row)
            {
                row.IsSelected = box.IsChecked == true;
            }
            var vm = DataContext as BackupViewModel;
            if (vm != null) { vm.RefreshSelectAllState(); }
        }
    }
}
