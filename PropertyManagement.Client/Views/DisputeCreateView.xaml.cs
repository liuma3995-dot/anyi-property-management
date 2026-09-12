using System.Windows.Controls;
using System.Windows.Input;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    public partial class DisputeCreateView : UserControl
    {
        public DisputeCreateView()
        {
            InitializeComponent();
        }

        /// <summary>点选推荐调解员 → 设 MediatorId（BR-DIS-03）。</summary>
        private void MediatorItem_Click(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as DisputeCreateViewModel;
            var option = (sender as System.Windows.FrameworkElement)?.DataContext as DisputeMediatorOption;
            if (vm == null || option == null) return;
            vm.SelectMediatorCommand.Execute(option);
        }
    }
}
