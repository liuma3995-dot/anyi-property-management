using System;
using System.Collections.Generic;
using System.Data;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.PhoneBook;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>便民电话簿仓储（M6 D6-3，UC-TEL-001~006 + BR-TEL-01~04）。</summary>
    public interface IPhoneBookRepository
    {
        List<PhoneCategoryDto> ListCategories(IDbConnection connection);
        int InsertCategory(IDbConnection connection, IDbTransaction transaction, PhoneCategoryDto dto);
        void UpdateCategory(IDbConnection connection, IDbTransaction transaction, PhoneCategoryDto dto);
        void DeleteCategory(IDbConnection connection, IDbTransaction transaction, int id);
        List<PhoneTypeDto> ListTypes(IDbConnection connection);
        int InsertType(IDbConnection connection, IDbTransaction transaction, PhoneTypeDto dto);
        void UpdateType(IDbConnection connection, IDbTransaction transaction, PhoneTypeDto dto);
        void DeleteType(IDbConnection connection, IDbTransaction transaction, int id);
        int CountEnabledEntriesByType(IDbConnection connection, int typeId);

        PhoneEntryDto GetEntry(IDbConnection connection, int id);
        PageResult<PhoneEntryDto> QueryEntries(IDbConnection connection, PhoneEntryQueryRequest query, out int total);
        int InsertEntry(IDbConnection connection, IDbTransaction transaction, PhoneEntryDto dto);
        void UpdateEntry(IDbConnection connection, IDbTransaction transaction, PhoneEntryDto dto);

        /// <summary>状态+停用来源一并落库（disableSource：0 手工 1 离职联动）。</summary>
        void SetEntryStatus(IDbConnection connection, IDbTransaction transaction, int id, PhoneEntryStatus status, int disableSource);
        void SetEntryTop(IDbConnection connection, IDbTransaction transaction, int id, bool isTop);

        /// <summary>按 employee_id 精确匹配员工条目（BR-TEL-01 同步幂等主键，取 id 最小一条）。</summary>
        PhoneEntryDto GetEntryByEmployeeId(IDbConnection connection, int employeeId);

        /// <summary>按名称匹配未回填 employee_id 的存量员工条目（entry_type=2，幂等兜底）。</summary>
        PhoneEntryDto FindLegacyEmployeeEntryByName(IDbConnection connection, string name);

        /// <summary>号码归一化（去空格/-）后查重，返回 id/name/status 供冲突提示。</summary>
        PhoneEntryDto GetEntryByPhone(IDbConnection connection, string phone, int excludeId);

        /// <summary>统计仍启用（未停用/未删除）且引用该分类的条目数，用于删除分类前的引用校验。</summary>
        int CountEnabledEntriesByCategory(IDbConnection connection, int categoryId);
    }
}
