namespace PropertyManagement.Contract.Common
{
    /// <summary>分页查询请求约定（所有列表接口通用）。</summary>
    public class PageRequest
    {
        public PageRequest()
        {
            PageIndex = 1;
            PageSize = 20;
        }

        public int PageIndex { get; set; }

        public int PageSize { get; set; }

        public string Keyword { get; set; }
    }
}
