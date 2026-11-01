using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 顶栏全局搜索（R17，PG-SHELL）：跨模块只读检索房产/业主/设备/电话条目/员工/纠纷案件，
    /// 按模块分组返回，命中项携带「目标模块 + 目标页 + 关键词」，供前端跳转并自动过滤列表。
    /// 只读、无副作用；关键词为空直接返回空结果。
    /// </summary>
    public class GlobalSearchService
    {
        /// <summary>
        /// CHG-v2.2.0-05：全局搜索是跨模块预览，但不是固定只看 5 条。
        /// 每个来源最多返回 50 条，浮层内部滚动查看；避免楼栋/姓氏命中被静默截断。
        /// </summary>
        private const int LimitPerSource = 50;

        private readonly IDbConnectionFactory _connectionFactory;

        public GlobalSearchService()
            : this(new SqliteConnectionFactory())
        {
        }

        public GlobalSearchService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public GlobalSearchResultDto Search(string keyword)
        {
            var result = new GlobalSearchResultDto
            {
                Keyword = keyword == null ? string.Empty : keyword.Trim(),
                Groups = new List<GlobalSearchGroupDto>()
            };
            if (string.IsNullOrWhiteSpace(result.Keyword))
            {
                return result;
            }

            if (result.Keyword.Length > 50)
            {
                result.Keyword = result.Keyword.Substring(0, 50);
            }

            string like = LikePattern(result.Keyword);
            string normalizedKeyword = SqlAddress.NormalizeKeyword(result.Keyword);
            string normalizedLike = normalizedKeyword.Length == 0 ? string.Empty : LikePattern(normalizedKeyword);

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                AddGroup(result, "baseinfo", "基础信息",
                    PropertyItems(connection, like, normalizedLike), OwnerItems(connection, like));
                AddGroup(result, "equipment", "设备台账", DeviceItems(connection, like));
                AddGroup(result, "phonebook", "便民电话簿", PhoneItems(connection, like));
                AddGroup(result, "org", "人员组织", EmployeeItems(connection, like));
                AddGroup(result, "dispute", "纠纷调解", DisputeItems(connection, like));
            }

            result.Total = result.Groups.Sum(g => g.Items.Count);
            // CHG-v2.2.0-03：组间按最佳相关性排序；同等级保持模块固定顺序，保证每次查询结果稳定。
            var moduleOrder = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "baseinfo", 0 }, { "equipment", 1 }, { "phonebook", 2 }, { "org", 3 }, { "dispute", 4 }
            };
            result.Groups = result.Groups
                .OrderBy(g => g.Items.Min(i => MatchRank(i, result.Keyword)))
                .ThenBy(g => moduleOrder.ContainsKey(g.Module) ? moduleOrder[g.Module] : 99)
                .ToList();
            return result;
        }

        private static string LikePattern(string value)
        {
            return "%" + (value ?? string.Empty).Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
        }

        private static void AddGroup(GlobalSearchResultDto result, string module, string moduleName,
            params List<GlobalSearchItemDto>[] batches)
        {
            var items = new List<GlobalSearchItemDto>();
            foreach (List<GlobalSearchItemDto> batch in batches)
            {
                items.AddRange(batch);
            }
            if (items.Count == 0)
            {
                return;
            }

            items = items
                .OrderBy(i => MatchRank(i, result.Keyword))
                .ThenBy(i => i.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(i => i.Subtitle ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(i => i.TargetId)
                .ToList();

            result.Groups.Add(new GlobalSearchGroupDto { Module = module, ModuleName = moduleName, Items = items });
        }

        /// <summary>
        /// CHG-v2.2.0-03：全局搜索统一相关性口径。
        /// 标题精确命中优先，其次标题前缀／包含，最后才是副标题命中；同等级由调用方以稳定业务键收尾。
        /// </summary>
        private static int MatchRank(GlobalSearchItemDto item, string keyword)
        {
            string raw = (keyword ?? string.Empty).Trim();
            string title = (item == null ? null : item.Title ?? string.Empty).Trim();
            string subtitle = (item == null ? null : item.Subtitle ?? string.Empty).Trim();
            if (raw.Length == 0) { return 5; }
            if (string.Equals(title, raw, StringComparison.CurrentCultureIgnoreCase)) { return 0; }
            if (title.StartsWith(raw, StringComparison.CurrentCultureIgnoreCase)) { return 1; }
            if (title.IndexOf(raw, StringComparison.CurrentCultureIgnoreCase) >= 0) { return 2; }
            if (subtitle.StartsWith(raw, StringComparison.CurrentCultureIgnoreCase)) { return 3; }
            if (subtitle.IndexOf(raw, StringComparison.CurrentCultureIgnoreCase) >= 0) { return 4; }
            return 5;
        }

        private static List<GlobalSearchItemDto> PropertyItems(IDbConnection connection, string like, string normalizedLike)
        {
            string normalizedFull = SqlAddress.NormalizedConcat("bl.building_no", "u.unit_no", "p.room_no");
            string normalizedSkipUnit = SqlAddress.NormalizedConcat("bl.building_no", "p.room_no");
            string normalizedCriteria = string.IsNullOrEmpty(normalizedLike)
                ? string.Empty
                : " OR " + normalizedFull + " LIKE @normalizedLike ESCAPE '[' " +
                  " OR " + normalizedSkipUnit + " LIKE @normalizedLike ESCAPE '['";
            return connection.Query<GlobalSearchItemDto>(
                "SELECT " + SqlAddress.BuildingUnitRoom("bl.building_no", "u.unit_no", "p.room_no") + " AS Title, " +
                "COALESCE(cm.name,'') || '  ·  建筑面积 ' || p.area || ' ㎡' AS Subtitle, " +
                "'baseinfo' AS TargetModule, '房产列表' AS TargetPage, p.room_no AS Keyword, p.id AS TargetId " +
                "FROM t_property p " +
                "LEFT JOIN t_building bl ON bl.id = p.building_id " +
                "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                "LEFT JOIN t_community cm ON cm.id = bl.community_id " +
                "WHERE p.del_flag = 0 AND (p.room_no LIKE @like ESCAPE '[' OR COALESCE(bl.building_no,'') LIKE @like ESCAPE '[' " +
                "      OR COALESCE(u.unit_no,'') LIKE @like ESCAPE '[' " +
                "      OR COALESCE(cm.name,'') LIKE @like ESCAPE '['" + normalizedCriteria + ") " +
                "ORDER BY bl.building_no, p.room_no LIMIT " + LimitPerSource, new { like, normalizedLike }).ToList();
        }

        private static List<GlobalSearchItemDto> OwnerItems(IDbConnection connection, string like)
        {
            return connection.Query<GlobalSearchItemDto>(
                "SELECT o.name AS Title, COALESCE(o.phone,'') || '  ·  ' || COALESCE(o.resident_address,'') AS Subtitle, " +
                "'baseinfo' AS TargetModule, '业主档案' AS TargetPage, o.name AS Keyword, o.id AS TargetId " +
                "FROM t_owner o WHERE o.del_flag = 0 AND (o.name LIKE @like ESCAPE '[' OR COALESCE(o.phone,'') LIKE @like ESCAPE '[') " +
                "ORDER BY o.name, COALESCE(o.phone,''), o.id LIMIT " + LimitPerSource, new { like }).ToList();
        }

        private static List<GlobalSearchItemDto> DeviceItems(IDbConnection connection, string like)
        {
            return connection.Query<GlobalSearchItemDto>(
                "SELECT d.name AS Title, 'EQP-' || printf('%04d', d.id) || '  ·  ' || COALESCE(t.name,'') || " +
                "       CASE WHEN COALESCE(d.location,'') = '' THEN '' ELSE '  ·  ' || d.location END AS Subtitle, " +
                "'equipment' AS TargetModule, '设备列表' AS TargetPage, d.name AS Keyword, d.id AS TargetId " +
                "FROM t_device d LEFT JOIN t_device_type t ON t.id = d.type_id " +
                "WHERE d.del_flag = 0 AND (d.name LIKE @like ESCAPE '[' OR COALESCE(d.location,'') LIKE @like ESCAPE '[') " +
                "ORDER BY d.id LIMIT " + LimitPerSource, new { like }).ToList();
        }

        private static List<GlobalSearchItemDto> PhoneItems(IDbConnection connection, string like)
        {
            return connection.Query<GlobalSearchItemDto>(
                "SELECT e.name AS Title, e.phone || '  ·  ' || COALESCE(c.name,'') AS Subtitle, " +
                "'phonebook' AS TargetModule, '电话查询' AS TargetPage, e.name AS Keyword, e.id AS TargetId " +
                "FROM t_phone_entry e LEFT JOIN t_phone_category c ON c.id = e.category_id " +
                "WHERE e.del_flag = 0 AND (e.name LIKE @like ESCAPE '[' OR e.phone LIKE @like ESCAPE '[') " +
                "ORDER BY e.is_top DESC, e.id LIMIT " + LimitPerSource, new { like }).ToList();
        }

        private static List<GlobalSearchItemDto> EmployeeItems(IDbConnection connection, string like)
        {
            return connection.Query<GlobalSearchItemDto>(
                "SELECT e.name AS Title, COALESCE(d.name,'') || '  ·  ' || COALESCE(p.name,'') || '  ·  ' || " +
                "       COALESCE(e.emp_no, '') AS Subtitle, " +
                "'org' AS TargetModule, '员工列表' AS TargetPage, e.name AS Keyword, e.id AS TargetId " +
                "FROM t_employee e LEFT JOIN t_department d ON d.id = e.dept_id LEFT JOIN t_position p ON p.id = e.position_id " +
                "WHERE e.del_flag = 0 AND (e.name LIKE @like ESCAPE '[' OR COALESCE(e.phone,'') LIKE @like ESCAPE '[' " +
                "      OR COALESCE(e.emp_no,'') LIKE @like ESCAPE '[') " +
                "ORDER BY e.id LIMIT " + LimitPerSource, new { like }).ToList();
        }

        private static List<GlobalSearchItemDto> DisputeItems(IDbConnection connection, string like)
        {
            return connection.Query<GlobalSearchItemDto>(
                "SELECT 'JF-' || strftime('%y%m', c.occur_time) || '-' || c.id || '  ' || COALESCE(c.location,'') AS Title, " +
                "COALESCE(t.name,'') || '  ·  ' || COALESCE(c.detail,'') AS Subtitle, " +
                "'dispute' AS TargetModule, '纠纷列表' AS TargetPage, COALESCE(NULLIF(c.location,''), c.detail) AS Keyword, c.id AS TargetId " +
                "FROM t_dispute_case c LEFT JOIN t_dispute_type t ON t.id = c.type_id " +
                "WHERE c.del_flag = 0 AND (COALESCE(c.location,'') LIKE @like ESCAPE '[' " +
                "      OR COALESCE(c.detail,'') LIKE @like ESCAPE '[' " +
                "      OR ('JF-' || strftime('%y%m', c.occur_time) || '-' || c.id) LIKE @like ESCAPE '[') " +
                "ORDER BY c.id DESC LIMIT " + LimitPerSource, new { like }).ToList();
        }
    }
}
