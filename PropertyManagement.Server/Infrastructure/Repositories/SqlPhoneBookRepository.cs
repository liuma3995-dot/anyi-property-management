using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.PhoneBook;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>便民电话簿仓储 SQLite 实现（M6 D6-3，Dapper）。</summary>
    public class SqlPhoneBookRepository : IPhoneBookRepository
    {
        private const string EntryBaseSql =
            "SELECT e.id, e.category_id AS CategoryId, e.type_id AS TypeId, e.entry_type AS EntryType, e.name, e.phone, e.note, " +
            "e.employee_id AS EmployeeId, e.disable_source AS DisableSource, e.is_top AS IsTop, e.status, " +
            "COALESCE(c.name,'') AS CategoryName, COALESCE(t.name,'') AS TypeName, e.updated_at AS UpdatedAt " +
            "FROM t_phone_entry e LEFT JOIN t_phone_category c ON c.id = e.category_id AND c.del_flag = 0 " +
            "LEFT JOIN t_phone_type t ON t.id = e.type_id AND t.del_flag = 0";

        public List<PhoneCategoryDto> ListCategories(IDbConnection connection)
        {
            return connection.Query<PhoneCategoryDto>(
                "SELECT id, name, sort FROM t_phone_category WHERE COALESCE(status, 0) = 0 AND del_flag = 0 ORDER BY sort, id").ToList();
        }

        public int InsertCategory(IDbConnection connection, IDbTransaction transaction, PhoneCategoryDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_phone_category (name, sort, status, del_flag) VALUES (@Name, @Sort, 0, 0); SELECT last_insert_rowid();",
                new { dto.Name, dto.Sort }, transaction);
        }

        public void UpdateCategory(IDbConnection connection, IDbTransaction transaction, PhoneCategoryDto dto)
        {
            connection.Execute(
                "UPDATE t_phone_category SET name = @Name, sort = @Sort, updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.Name, dto.Sort }, transaction);
        }

        public void DeleteCategory(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_phone_category SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                new { id }, transaction);
        }

        public List<PhoneTypeDto> ListTypes(IDbConnection connection)
        {
            return connection.Query<PhoneTypeDto>(
                "SELECT id, name, sort FROM t_phone_type WHERE COALESCE(status, 0) = 0 AND del_flag = 0 ORDER BY sort, id").ToList();
        }

        public int InsertType(IDbConnection connection, IDbTransaction transaction, PhoneTypeDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_phone_type (name, sort, status, del_flag) VALUES (@Name, @Sort, 0, 0); SELECT last_insert_rowid();",
                new { dto.Name, dto.Sort }, transaction);
        }

        public void UpdateType(IDbConnection connection, IDbTransaction transaction, PhoneTypeDto dto)
        {
            connection.Execute(
                "UPDATE t_phone_type SET name = @Name, sort = @Sort, updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.Name, dto.Sort }, transaction);
        }

        public void DeleteType(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_phone_type SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                new { id }, transaction);
        }

        public int CountEnabledEntriesByType(IDbConnection connection, int typeId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_phone_entry WHERE type_id = @typeId AND del_flag = 0 AND status = 0",
                new { typeId });
        }

        public PhoneEntryDto GetEntry(IDbConnection connection, int id)
        {
            return NormalizeEntry(connection.QueryFirstOrDefault<PhoneEntryDto>(
                EntryBaseSql + " WHERE e.id = @id AND e.del_flag = 0", new { id }));
        }

        public PageResult<PhoneEntryDto> QueryEntries(IDbConnection connection, PhoneEntryQueryRequest query, out int total)
        {
            string where = "WHERE e.del_flag = 0";
            var p = new DynamicParameters();
            if (query.CategoryId.HasValue) { where += " AND e.category_id = @categoryId"; p.Add("categoryId", query.CategoryId.Value); }
            if (query.EntryType.HasValue) { where += " AND e.entry_type = @entryType"; p.Add("entryType", (int)query.EntryType.Value); }
            if (query.Status.HasValue) { where += " AND e.status = @status"; p.Add("status", (int)query.Status.Value); }
            if (query.EmployeeId.HasValue) { where += " AND e.employee_id = @employeeId"; p.Add("employeeId", query.EmployeeId.Value); }
            if (query.Source.HasValue)
            {
                // 来源：1 员工通讯录（entry_type=2）/ 0 手工（其余类型）
                where += query.Source.Value == 1 ? " AND e.entry_type = 2" : " AND e.entry_type <> 2";
            }
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                // 关键词限长 50；LIKE 通配符 %/_/[ 按 ESCAPE '[' 转义，防通配注入；
                // 拼音列存小写，SQLite LIKE 对 ASCII 不区分大小写，大写关键词同样命中。
                string kw = query.Keyword.Trim();
                if (kw.Length > 50) kw = kw.Substring(0, 50);
                string escaped = kw.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
                where += " AND (e.name LIKE @kw ESCAPE '[' OR e.phone LIKE @kw ESCAPE '[' OR c.name LIKE @kw ESCAPE '['" +
                         " OR e.name_pinyin LIKE @kw ESCAPE '[' OR e.name_initials LIKE @kw ESCAPE '[')";
                p.Add("kw", "%" + escaped + "%");
            }
            if (query.TopOnly) { where += " AND e.is_top = 1"; }
            string from = "FROM t_phone_entry e LEFT JOIN t_phone_category c ON c.id = e.category_id AND c.del_flag = 0 " +
                          "LEFT JOIN t_phone_type t ON t.id = e.type_id AND t.del_flag = 0";
            total = connection.ExecuteScalar<int>("SELECT COUNT(1) " + from + " " + where, p);
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;
            p.Add("limit", pageSize); p.Add("offset", offset);
            // 排序：置顶优先 → 拼音升序（空拼音排最后）→ id 兜底，翻页无跳动（BR-TEL-02）
            string orderBy = " ORDER BY e.is_top DESC, CASE WHEN COALESCE(e.name_pinyin,'') = '' THEN 1 ELSE 0 END, e.name_pinyin, e.id ";
            var items = connection.Query<PhoneEntryDto>(
                EntryBaseSql + " " + where + orderBy + " LIMIT @limit OFFSET @offset", p)
                .Select(NormalizeEntry).ToList();
            return new PageResult<PhoneEntryDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }

        public int InsertEntry(IDbConnection connection, IDbTransaction transaction, PhoneEntryDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_phone_entry (category_id, type_id, entry_type, employee_id, name, phone, note, is_top, status, " +
                "disable_source, name_pinyin, name_initials, del_flag) " +
                "VALUES (@CategoryId, @TypeId, @EntryType, @EmployeeId, @Name, @Phone, @Note, @IsTop, @Status, @DisableSource, " +
                "@NamePinyin, @NameInitials, 0); SELECT last_insert_rowid();",
                new
                {
                    dto.CategoryId,
                    dto.TypeId,
                    EntryType = (int)dto.EntryType,
                    dto.EmployeeId,
                    dto.Name,
                    dto.Phone,
                    Note = dto.Note ?? string.Empty,
                    IsTop = dto.IsTop ? 1 : 0,
                    Status = (int)dto.Status,
                    dto.DisableSource,
                    NamePinyin = dto.NamePinyin ?? string.Empty,
                    NameInitials = dto.NameInitials ?? string.Empty
                }, transaction);
        }

        public void UpdateEntry(IDbConnection connection, IDbTransaction transaction, PhoneEntryDto dto)
        {
            connection.Execute(
                "UPDATE t_phone_entry SET category_id = @CategoryId, type_id = @TypeId, entry_type = @EntryType, employee_id = @EmployeeId, " +
                "name = @Name, phone = @Phone, note = @Note, is_top = @IsTop, status = @Status, disable_source = @DisableSource, " +
                "name_pinyin = @NamePinyin, name_initials = @NameInitials, updated_at = datetime('now','localtime') " +
                "WHERE id = @Id AND del_flag = 0",
                new
                {
                    dto.Id,
                    dto.CategoryId,
                    dto.TypeId,
                    EntryType = (int)dto.EntryType,
                    dto.EmployeeId,
                    dto.Name,
                    dto.Phone,
                    Note = dto.Note ?? string.Empty,
                    IsTop = dto.IsTop ? 1 : 0,
                    Status = (int)dto.Status,
                    dto.DisableSource,
                    NamePinyin = dto.NamePinyin ?? string.Empty,
                    NameInitials = dto.NameInitials ?? string.Empty
                }, transaction);
        }

        public void SetEntryStatus(IDbConnection connection, IDbTransaction transaction, int id, PhoneEntryStatus status, int disableSource)
        {
            connection.Execute(
                "UPDATE t_phone_entry SET status = @status, disable_source = @disableSource, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND del_flag = 0",
                new { id, status = (int)status, disableSource }, transaction);
        }

        public void SetEntryTop(IDbConnection connection, IDbTransaction transaction, int id, bool isTop)
        {
            connection.Execute(
                "UPDATE t_phone_entry SET is_top = @isTop, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                new { id, isTop = isTop ? 1 : 0 }, transaction);
        }

        public PhoneEntryDto GetEntryByEmployeeId(IDbConnection connection, int employeeId)
        {
            return NormalizeEntry(connection.QueryFirstOrDefault<PhoneEntryDto>(
                EntryBaseSql + " WHERE e.del_flag = 0 AND e.employee_id = @employeeId ORDER BY e.id",
                new { employeeId }));
        }

        public PhoneEntryDto FindLegacyEmployeeEntryByName(IDbConnection connection, string name)
        {
            return NormalizeEntry(connection.QueryFirstOrDefault<PhoneEntryDto>(
                EntryBaseSql + " WHERE e.del_flag = 0 AND e.entry_type = 2 AND e.employee_id IS NULL AND e.name = @name ORDER BY e.id",
                new { name }));
        }

        public PhoneEntryDto GetEntryByPhone(IDbConnection connection, string phone, int excludeId)
        {
            // 归一化（去空格/-）后比对，仅匹配启用条目（停用=删除口径，已停用号码不再判重）；返回 id/name/status 供冲突提示
            string normalized = NormalizePhone(phone);
            return NormalizeEntry(connection.QueryFirstOrDefault<PhoneEntryDto>(
                "SELECT id, name, phone, entry_type AS EntryType, status, disable_source AS DisableSource " +
                "FROM t_phone_entry WHERE REPLACE(REPLACE(phone, ' ', ''), '-', '') = @normalized AND del_flag = 0 AND status = 0 AND id <> @excludeId",
                new { normalized, excludeId }));
        }

        public int CountEnabledEntriesByCategory(IDbConnection connection, int categoryId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_phone_entry WHERE category_id = @categoryId AND del_flag = 0 AND status = 0",
                new { categoryId });
        }

        private static PhoneEntryDto NormalizeEntry(PhoneEntryDto e)
        {
            if (e == null) return null;
            e.EntryTypeText = e.EntryType == PhoneEntryType.Emergency ? "紧急"
                : (e.EntryType == PhoneEntryType.Employee ? "员工通讯录" : "普通");
            // 三态：启用 / 停用（手工）/ 自动停用（离职联动 disable_source=1）
            e.StatusText = e.Status == PhoneEntryStatus.Enabled ? "启用"
                : (e.DisableSource == 1 ? "自动停用" : "停用");
            e.Source = e.EntryType == PhoneEntryType.Employee ? 1 : 0;
            e.SourceText = e.Source == 1 ? "员工通讯录" : "手工";
            e.Note = e.Note ?? string.Empty;
            e.CategoryName = e.CategoryName ?? string.Empty;
            // 类型名：优先取类型表；未回填时按枚举兜底
            if (string.IsNullOrWhiteSpace(e.TypeName))
                e.TypeName = e.EntryType == PhoneEntryType.Emergency ? "紧急" : (e.EntryType == PhoneEntryType.Employee ? "员工通讯录" : "普通");
            return e;
        }

        private static string NormalizePhone(string phone)
        {
            return (phone ?? string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).Trim();
        }
    }
}
