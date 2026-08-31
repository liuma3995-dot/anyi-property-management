using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PropertyManagement.Client.ViewModels
{
    /// <summary>导航节点（PG-SHELL 两级树）：仪表盘为无子页节点，模块节点含子页。</summary>
    public class NavNode : ObservableObject
    {
        private bool _isExpanded;
        private bool _isSelected;

        public string Key { get; set; }

        public string Title { get; set; }

        public string IconKey { get; set; }

        public bool IsModule { get; set; }

        public ObservableCollection<NavPage> Pages { get; } = new ObservableCollection<NavPage>();

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set { SetProperty(ref _isExpanded, value); }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }
    }

    /// <summary>导航子页。</summary>
    public class NavPage : ObservableObject
    {
        private bool _isSelected;

        public string Key { get; set; }

        public string Title { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }
    }
}
