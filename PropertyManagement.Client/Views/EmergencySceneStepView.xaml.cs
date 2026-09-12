using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PropertyManagement.Client.ViewModels;

namespace PropertyManagement.Client.Views
{
    /// <summary>场景与步骤维护（PG-EMG-01）。步骤行拖拽排序在代码后台处理（DataGrid DragDrop → VM MoveStepAsync）。</summary>
    public partial class EmergencySceneStepView : UserControl
    {
        private Point _dragStartPoint;
        private EmergencyStepRow _dragRow;


        public EmergencySceneStepView()
        {
            InitializeComponent();
        }

        private EmergencySceneStepViewModel ViewModel { get { return DataContext as EmergencySceneStepViewModel; } }

        private void StepsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _dragRow = null;

            var row = FindRow(e.OriginalSource as DependencyObject);
            _dragRow = row != null ? row.Item as EmergencyStepRow : null;
        }

        private void StepsGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragRow == null) return;
            var pos = e.GetPosition(null);
            double dx = Math.Abs(pos.X - _dragStartPoint.X);
            double dy = Math.Abs(pos.Y - _dragStartPoint.Y);
            if (dx > SystemParameters.MinimumHorizontalDragDistance || dy > SystemParameters.MinimumVerticalDragDistance)
            {

                var data = new DataObject(typeof(EmergencyStepRow), _dragRow);
                try
                {
                    DragDrop.DoDragDrop(StepsGrid, data, DragDropEffects.Move);
                }
                finally
                {
                    _dragRow = null;
                }
            }
        }

        private void StepsGrid_Drop(object sender, DragEventArgs e)
        {
            var source = e.Data.GetData(typeof(EmergencyStepRow)) as EmergencyStepRow;
            _dragRow = null;
            if (source == null || ViewModel == null) return;
            var row = FindRow(e.OriginalSource as DependencyObject);
            if (row == null) return;
            int targetIndex = StepsGrid.ItemContainerGenerator.IndexFromContainer(row);
            if (targetIndex < 0) targetIndex = StepsGrid.Items.IndexOf(row.Item);
            int sourceIndex = StepsGrid.Items.IndexOf(source);
            if (targetIndex < 0 || sourceIndex < 0 || sourceIndex == targetIndex) return;
            _ = ViewModel.MoveStepAsync(sourceIndex, targetIndex);
        }

        private static DataGridRow FindRow(DependencyObject source)
        {
            while (source != null && !(source is DataGridRow))
            {
                source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }
            return source as DataGridRow;
        }
    }
}
