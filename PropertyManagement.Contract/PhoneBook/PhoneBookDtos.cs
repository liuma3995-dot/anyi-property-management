using System;
using System.Collections.Generic;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Contract.PhoneBook
{
    /// <summary>电话分类（t_phone_category，UC-TEL-001）。</summary>
    public class PhoneCategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Sort { get; set; }
    }

    /// <summary>电话类型（t_phone_type，支持自定义新增/删除）。</summary>
    public class PhoneTypeDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Sort { get; set; }
    }

    /// <summary>电话条目（t_phone_entry，UC-TEL-002/003/004/006）。</summary>
    public class PhoneEntryDto
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public int? TypeId { get; set; }
        public PhoneEntryType EntryType { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Note { get; set; }
        public bool IsTop { get; set; }
        public PhoneEntryStatus Status { get; set; }
        /// <summary>关联员工 id（t_phone_entry.employee_id，员工同步弱关联；手工条目为 null）。</summary>
        public int? EmployeeId { get; set; }
        /// <summary>来源：0 手工 1 员工通讯录（与 EntryType=Employee 对应，用于筛选/展示）。</summary>
        public int Source { get; set; }
        /// <summary>停用来源：0 手工停用 1 离职联动自动停用（t_phone_entry.disable_source）。</summary>
        public int DisableSource { get; set; }
        /// <summary>拼音全拼（排序/搜索，无拼音库时为 ASCII 兜底）。</summary>
        public string NamePinyin { get; set; }
        /// <summary>拼音首字母（搜索）。</summary>
        public string NameInitials { get; set; }
        public string CategoryName { get; set; }
        public string TypeName { get; set; }
        public string EntryTypeText { get; set; }
        public string StatusText { get; set; }
        public string SourceText { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class PhoneCategoryRequest
    {
        public string Name { get; set; }
        public int Sort { get; set; }
    }

    public class PhoneEntryRequest
    {
        public int CategoryId { get; set; }
        public int? TypeId { get; set; }
        public PhoneEntryType EntryType { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Note { get; set; }
        public bool IsTop { get; set; }
        /// <summary>员工关联 id：新增 EntryType=Employee 条目时必传（BR-TEL-01，防手工冒充员工条目）。</summary>
        public int? EmployeeId { get; set; }
        /// <summary>拼音全拼（可选；不传则按名称做 ASCII 兜底，汉字暂为空串）。</summary>
        public string NamePinyin { get; set; }
        /// <summary>拼音首字母（可选）。</summary>
        public string NameInitials { get; set; }
    }

    /// <summary>电话类型新增/编辑请求。</summary>
    public class PhoneTypeRequest
    {
        public string Name { get; set; }
        public int Sort { get; set; }
    }

    /// <summary>电话条目状态变更请求（UC-TEL-006 停用/启用）。</summary>
    public class PhoneEntryStatusRequest
    {
        public PhoneEntryStatus Status { get; set; }
    }

    /// <summary>电话条目置顶请求（UC-TEL-004 设置常用/置顶）。</summary>
    public class PhoneEntryTopRequest
    {
        public bool IsTop { get; set; }
    }

    /// <summary>批量停用请求（批量停用=删除口径，停用后查询页不再展示）。</summary>
    public class PhoneEntryBatchStatusRequest
    {
        public List<int> Ids { get; set; }
    }

    /// <summary>批量停用结果（成功停用数 / 因紧急号码不可停用跳过数）。</summary>
    public class PhoneEntryBatchStatusResultDto
    {
        public int Disabled { get; set; }
        public int Skipped { get; set; }
    }

    /// <summary>电话查询条件（UC-TEL-003，关键词搜索 + 分类/来源/员工筛选 + 分页）。</summary>
    public class PhoneEntryQueryRequest : PageRequest
    {
        public int? CategoryId { get; set; }
        public PhoneEntryType? EntryType { get; set; }
        public PhoneEntryStatus? Status { get; set; }
        /// <summary>员工 id 筛选（员工通讯录条目）。</summary>
        public int? EmployeeId { get; set; }
        /// <summary>来源筛选：0 手工 1 员工通讯录。</summary>
        public int? Source { get; set; }
        public bool TopOnly { get; set; }
    }

    /// <summary>员工通讯录同步请求（UC-TEL-005）。</summary>
    public class EmployeeSyncRequest
    {
        public int CategoryId { get; set; }
    }

    /// <summary>员工通讯录同步结果（UC-TEL-005）。</summary>
    public class EmployeeSyncResultDto
    {
        /// <summary>在岗员工总数。</summary>
        public int TotalEmployees { get; set; }
        /// <summary>本次新建条目数。</summary>
        public int Synced { get; set; }
        /// <summary>本次更新（回写资料/恢复启用）条目数。</summary>
        public int Updated { get; set; }
        /// <summary>本次离职/离岗联动停用条目数。</summary>
        public int Disabled { get; set; }
        /// <summary>缺姓名或号码跳过的员工数。</summary>
        public int Skipped { get; set; }
    }
}
