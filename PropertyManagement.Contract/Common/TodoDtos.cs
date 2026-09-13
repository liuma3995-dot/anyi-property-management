using System;
using System.Collections.Generic;

namespace PropertyManagement.Contract.Common
{
    /// <summary>
    /// 待办提醒项（UC-COM-006；R17 新增：顶部铃铛通知中心与仪表盘「待办事项」共用同一数据源）。
    /// 归属：公共（COM）——本身不拥有业务表，仅跨模块只读聚合。
    /// </summary>
    public class TodoItemDto
    {
        /// <summary>唯一标识（kind + 目标 id，如 reminder-12 / bill-8 / dispute-3）。</summary>
        public string Id { get; set; }

        /// <summary>分类：maintenance（保养/年检/质保/合同到期）/ arrears（欠费）/ dispute（纠纷）。</summary>
        public string Kind { get; set; }

        /// <summary>分类中文（到期提醒 / 欠费催缴 / 纠纷处理）。</summary>
        public string KindText { get; set; }

        public string Title { get; set; }

        public string Meta { get; set; }

        /// <summary>右侧标签（逾期 / 今日 / 临近到期 / 待催缴 / 处理中）。</summary>
        public string Tag { get; set; }

        /// <summary>紧急度：danger（逾期/超期）/ warning（今日/临近）/ info（处理中）。</summary>
        public string Level { get; set; }

        public DateTime? DueAt { get; set; }

        /// <summary>跳转目标模块 key（equipment / finance / dispute）。</summary>
        public string TargetModule { get; set; }

        /// <summary>跳转目标页面标题（到期提醒 / 欠费台账 / 纠纷列表）。</summary>
        public string TargetPage { get; set; }

        /// <summary>目标业务记录 id（提醒 id / 账单 id / 案件 id）。</summary>
        public int TargetId { get; set; }
    }

    /// <summary>待办中心（顶部铃铛面板；Total = 当前待处理总数）。</summary>
    public class TodoCenterDto
    {
        public int Total { get; set; }

        /// <summary>分类计数（kind → 数量），用于徽标与状态栏摘要。</summary>
        public Dictionary<string, int> CountByKind { get; set; }

        public List<TodoItemDto> Items { get; set; }
    }
}
