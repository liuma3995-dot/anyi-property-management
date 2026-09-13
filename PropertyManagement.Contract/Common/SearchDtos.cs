using System.Collections.Generic;

namespace PropertyManagement.Contract.Common
{
    /// <summary>
    /// 顶栏全局搜索（PG-SHELL；R17 新增）：跨模块只读查询，命中后跳转对应模块页并带关键词过滤。
    /// </summary>
    public class GlobalSearchItemDto
    {
        public string Title { get; set; }

        public string Subtitle { get; set; }

        /// <summary>目标模块 key（baseinfo / equipment / phonebook / org / dispute）。</summary>
        public string TargetModule { get; set; }

        /// <summary>目标页面标题（房产列表 / 设备列表 / 电话查询 / 员工列表 / 纠纷列表）。</summary>
        public string TargetPage { get; set; }

        /// <summary>跳转后用于过滤列表页的关键词。</summary>
        public string Keyword { get; set; }

        /// <summary>目标记录 id（供后续精确定位扩展）。</summary>
        public int TargetId { get; set; }
    }

    /// <summary>按模块分组的结果（前端以下拉分组展示）。</summary>
    public class GlobalSearchGroupDto
    {
        public string Module { get; set; }

        public string ModuleName { get; set; }

        public List<GlobalSearchItemDto> Items { get; set; }
    }

    public class GlobalSearchResultDto
    {
        public string Keyword { get; set; }

        public int Total { get; set; }

        public List<GlobalSearchGroupDto> Groups { get; set; }
    }
}
