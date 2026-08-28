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

    /// <summary>电话条目（t_phone_entry，UC-TEL-002/003/004/006）。</summary>
    public class PhoneEntryDto
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public PhoneEntryType EntryType { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Note { get; set; }
        public bool IsTop { get; set; }
        public PhoneEntryStatus Status { get; set; }
    }

    public class PhoneCategoryRequest
    {
        public string Name { get; set; }
        public int Sort { get; set; }
    }

    public class PhoneEntryRequest
    {
        public int CategoryId { get; set; }
        public PhoneEntryType EntryType { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Note { get; set; }
        public bool IsTop { get; set; }
    }

    /// <summary>电话条目状态变更请求（UC-TEL-006 停用/启用）。</summary>
    public class PhoneEntryStatusRequest
    {
        public PhoneEntryStatus Status { get; set; }
    }

    /// <summary>电话查询条件（UC-TEL-003，关键词搜索 + 分页）。</summary>
    public class PhoneEntryQueryRequest : PageRequest
    {
        public int? CategoryId { get; set; }
        public PhoneEntryType? EntryType { get; set; }
        public PhoneEntryStatus? Status { get; set; }
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
        public int TotalEmployees { get; set; }
        public int Synced { get; set; }
        public int Skipped { get; set; }
    }
}
