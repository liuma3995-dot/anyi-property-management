namespace PropertyManagement.Client.ViewModels
{
    /// <summary>导航占位页（M3-D6：M4~M6 业务切片实现前展示）。</summary>
    public class PlaceholderViewModel
    {
        public string Title { get; }

        public string Message
        {
            get { return "「" + Title + "」模块将在 M4~M6 按业务切片实现，本页为全局框架占位。"; }
        }

        public PlaceholderViewModel(string title)
        {
            Title = title;
        }
    }
}
