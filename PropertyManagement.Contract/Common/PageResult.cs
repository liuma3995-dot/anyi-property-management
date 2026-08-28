using System.Collections.Generic;

namespace PropertyManagement.Contract.Common
{
    /// <summary>分页查询响应约定（所有列表接口通用）。</summary>
    public class PageResult<T>
    {
        public int PageIndex { get; set; }

        public int PageSize { get; set; }

        public int Total { get; set; }

        public List<T> Items { get; set; }
    }
}
