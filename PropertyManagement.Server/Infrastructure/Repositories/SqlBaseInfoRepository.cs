using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>基础信息仓储 SQLite 实现（M5 D5-1~D5-4，Dapper）。</summary>
    public class SqlBaseInfoRepository : IBaseInfoRepository
    {
        // ===================== 小区 =====================
        public List<CommunityDto> ListCommunities(IDbConnection connection, string keyword)
        {
            string sql = "SELECT id, name, address, del_flag AS DelFlag, created_at AS CreatedAt, updated_at AS UpdatedAt " +
                         "FROM t_community WHERE del_flag = 0";
            var p = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                sql += " AND (name LIKE @kw OR address LIKE @kw)";
                p.Add("kw", "%" + keyword.Trim() + "%");
            }
            sql += " ORDER BY id";
            return connection.Query<CommunityDto>(sql, p).ToList();
        }

        public CommunityDto GetCommunity(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<CommunityDto>(
                "SELECT id, name, address, del_flag AS DelFlag, created_at AS CreatedAt, updated_at AS UpdatedAt " +
                "FROM t_community WHERE id = @id AND del_flag = 0", new { id });
        }

        public int InsertCommunity(IDbConnection connection, IDbTransaction transaction, CommunityDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_community (name, address, del_flag) VALUES (@Name, @Address, 0); SELECT last_insert_rowid();",
                new { dto.Name, dto.Address }, transaction);
        }

        public void UpdateCommunity(IDbConnection connection, IDbTransaction transaction, CommunityDto dto)
        {
            connection.Execute(
                "UPDATE t_community SET name = @Name, address = @Address, updated_at = datetime('now','localtime') " +
                "WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.Name, dto.Address }, transaction);
        }

        public void SoftDeleteCommunity(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_community SET del_flag = 1, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        // ===================== 楼栋 =====================
        public List<BuildingDto> ListBuildings(IDbConnection connection, int? communityId, string keyword)
        {
            string sql = "SELECT b.id, b.community_id AS CommunityId, c.name AS CommunityName, " +
                         "b.building_no AS BuildingNo, b.floors, b.del_flag AS DelFlag, " +
                         "b.created_at AS CreatedAt, b.updated_at AS UpdatedAt " +
                         "FROM t_building b LEFT JOIN t_community c ON c.id = b.community_id WHERE b.del_flag = 0";
            var p = new DynamicParameters();
            if (communityId.HasValue)
            {
                sql += " AND b.community_id = @cid";
                p.Add("cid", communityId.Value);
            }
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                sql += " AND b.building_no LIKE @kw";
                p.Add("kw", "%" + keyword.Trim() + "%");
            }
            sql += " ORDER BY b.id";
            return connection.Query<BuildingDto>(sql, p).ToList();
        }

        public BuildingDto GetBuilding(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<BuildingDto>(
                "SELECT b.id, b.community_id AS CommunityId, c.name AS CommunityName, " +
                "b.building_no AS BuildingNo, b.floors, b.del_flag AS DelFlag, " +
                "b.created_at AS CreatedAt, b.updated_at AS UpdatedAt " +
                "FROM t_building b LEFT JOIN t_community c ON c.id = b.community_id " +
                "WHERE b.id = @id AND b.del_flag = 0", new { id });
        }

        public int InsertBuilding(IDbConnection connection, IDbTransaction transaction, BuildingDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_building (community_id, building_no, floors, del_flag) " +
                "VALUES (@CommunityId, @BuildingNo, @Floors, 0); SELECT last_insert_rowid();",
                new { dto.CommunityId, dto.BuildingNo, dto.Floors }, transaction);
        }

        public void UpdateBuilding(IDbConnection connection, IDbTransaction transaction, BuildingDto dto)
        {
            connection.Execute(
                "UPDATE t_building SET community_id = @CommunityId, building_no = @BuildingNo, floors = @Floors, " +
                "updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.CommunityId, dto.BuildingNo, dto.Floors }, transaction);
        }

        public void SoftDeleteBuilding(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_building SET del_flag = 1, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        public int CountChildrenByBuilding(IDbConnection connection, int buildingId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_unit WHERE building_id = @buildingId AND del_flag = 0", new { buildingId });
        }

        // ===================== 单元 =====================
        public List<UnitDto> ListUnits(IDbConnection connection, int? buildingId, string keyword)
        {
            string sql = "SELECT u.id, u.building_id AS BuildingId, b.building_no AS BuildingNo, " +
                         "c.name AS CommunityName, u.unit_no AS UnitNo, u.del_flag AS DelFlag, " +
                         "u.created_at AS CreatedAt, u.updated_at AS UpdatedAt " +
                         "FROM t_unit u LEFT JOIN t_building b ON b.id = u.building_id " +
                         "LEFT JOIN t_community c ON c.id = b.community_id WHERE u.del_flag = 0";
            var p = new DynamicParameters();
            if (buildingId.HasValue)
            {
                sql += " AND u.building_id = @bid";
                p.Add("bid", buildingId.Value);
            }
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                sql += " AND u.unit_no LIKE @kw";
                p.Add("kw", "%" + keyword.Trim() + "%");
            }
            sql += " ORDER BY u.id";
            return connection.Query<UnitDto>(sql, p).ToList();
        }

        public UnitDto GetUnit(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<UnitDto>(
                "SELECT u.id, u.building_id AS BuildingId, b.building_no AS BuildingNo, " +
                "c.name AS CommunityName, u.unit_no AS UnitNo, u.del_flag AS DelFlag, " +
                "u.created_at AS CreatedAt, u.updated_at AS UpdatedAt " +
                "FROM t_unit u LEFT JOIN t_building b ON b.id = u.building_id " +
                "LEFT JOIN t_community c ON c.id = b.community_id WHERE u.id = @id AND u.del_flag = 0", new { id });
        }

        public int InsertUnit(IDbConnection connection, IDbTransaction transaction, UnitDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_unit (building_id, unit_no, del_flag) VALUES (@BuildingId, @UnitNo, 0); SELECT last_insert_rowid();",
                new { dto.BuildingId, dto.UnitNo }, transaction);
        }

        public void UpdateUnit(IDbConnection connection, IDbTransaction transaction, UnitDto dto)
        {
            connection.Execute(
                "UPDATE t_unit SET building_id = @BuildingId, unit_no = @UnitNo, " +
                "updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.BuildingId, dto.UnitNo }, transaction);
        }

        public void SoftDeleteUnit(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_unit SET del_flag = 1, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        public int CountChildrenByUnit(IDbConnection connection, int unitId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_property WHERE unit_id = @unitId AND del_flag = 0", new { unitId });
        }

        // ===================== 房产 =====================
        public PropertyDto GetProperty(IDbConnection connection, int id)
        {
            var row = connection.QueryFirstOrDefault<PropertyRead>(
                PropertySelectSql + " " + PropertyFromSql + " WHERE p.id = @id AND p.del_flag = 0", new { id });
            if (row == null) return null;
            var dto = ToProperty(row);
            FillArrears(connection, new List<PropertyDto> { dto });
            return dto;
        }

        public PageResult<PropertyDto> QueryProperties(IDbConnection connection, BaseInfoQueryRequest query, out int total)
        {
            string where = "WHERE p.del_flag = 0";
            var p = new DynamicParameters();
            if (query.CommunityId.HasValue)
            {
                where += " AND c.id = @communityId";
                p.Add("communityId", query.CommunityId.Value);
            }
            if (query.BuildingId.HasValue)
            {
                where += " AND COALESCE(p.building_id, u.building_id) = @buildingId";
                p.Add("buildingId", query.BuildingId.Value);
            }
            if (query.UnitId.HasValue)
            {
                where += " AND u.id = @unitId";
                p.Add("unitId", query.UnitId.Value);
            }
            if (!string.IsNullOrWhiteSpace(query.RoomNo))
            {
                where += " AND p.room_no LIKE @room";
                p.Add("room", "%" + query.RoomNo.Trim() + "%");
            }
            if (query.Status.HasValue)
            {
                where += " AND p.status = @status";
                p.Add("status", (int)query.Status.Value);
            }
            if (!string.IsNullOrWhiteSpace(query.OwnerName))
            {
                where += " AND EXISTS (SELECT 1 FROM t_owner_property_rel rel JOIN t_owner o ON o.id = rel.owner_id " +
                         "WHERE rel.property_id = p.id AND rel.del_flag = 0 AND o.name LIKE @owner)";
                p.Add("owner", "%" + query.OwnerName.Trim() + "%");
            }
            if (!string.IsNullOrWhiteSpace(query.Phone))
            {
                where += " AND EXISTS (SELECT 1 FROM t_owner_property_rel rel JOIN t_owner o ON o.id = rel.owner_id " +
                         "WHERE rel.property_id = p.id AND rel.del_flag = 0 AND o.phone LIKE @phone)";
                p.Add("phone", "%" + query.Phone.Trim() + "%");
            }
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                string kw = query.Keyword.Trim();
                where += " AND (p.room_no LIKE @kw OR EXISTS (SELECT 1 FROM t_owner_property_rel rel JOIN t_owner o ON o.id = rel.owner_id " +
                         "WHERE rel.property_id = p.id AND rel.del_flag = 0 AND (o.name LIKE @kw OR o.phone LIKE @kw)))";
                p.Add("kw", "%" + kw + "%");
            }
            if (query.OnlyArrear)
            {
                where += " AND EXISTS (SELECT 1 FROM t_bill b " +
                         "WHERE b.property_id = p.id AND b.del_flag = 0 AND b.amount > b.paid_amount AND b.status IN (0,1,2))";
            }

            string from = "FROM t_property p " +
                "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                "LEFT JOIN t_building b ON b.id = u.building_id " +
                "LEFT JOIN t_building pb ON pb.id = p.building_id " +
                "LEFT JOIN t_community c ON c.id = COALESCE(pb.community_id, b.community_id)";

            total = connection.ExecuteScalar<int>("SELECT COUNT(1) " + from + " " + where, p);
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;
            p.Add("limit", pageSize);
            p.Add("offset", offset);

            string sql = PropertySelectSql + " " + from + " " + where +
                " ORDER BY p.id LIMIT @limit OFFSET @offset";
            var items = connection.Query<PropertyRead>(sql, p).Select(ToProperty).ToList();
            FillArrears(connection, items);
            return new PageResult<PropertyDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }

        private const string PropertyFromSql =
            "FROM t_property p " +
            "LEFT JOIN t_unit u ON u.id = p.unit_id " +
            "LEFT JOIN t_building b ON b.id = u.building_id " +
            "LEFT JOIN t_building pb ON pb.id = p.building_id " +
            "LEFT JOIN t_community c ON c.id = COALESCE(pb.community_id, b.community_id)";

        private const string PropertySelectSql =
            "SELECT p.id, p.building_id AS BuildingId, p.unit_id AS UnitId, u.unit_no AS UnitNo, " +
            "COALESCE(pb.building_no, b.building_no) AS BuildingNo, " +
            "c.name AS CommunityName, " +
            "COALESCE(pb.building_no, b.building_no, '') || COALESCE(u.unit_no,'') || COALESCE(p.room_no,'') AS UnitPath, " +
            "p.room_no AS RoomNo, p.area AS Area, p.usage AS Usage, p.status AS Status, " +
            "COALESCE((SELECT o.name FROM t_owner o JOIN t_owner_property_rel r ON r.owner_id = o.id " +
            "  WHERE r.property_id = p.id AND r.del_flag = 0 AND r.rel_status IN (0,1) " +
            "  ORDER BY CASE r.rel_type WHEN 0 THEN 0 ELSE 1 END, r.id DESC LIMIT 1), '') AS OwnerName, " +
            "COALESCE((SELECT o.phone FROM t_owner o JOIN t_owner_property_rel r ON r.owner_id = o.id " +
            "  WHERE r.property_id = p.id AND r.del_flag = 0 AND r.rel_status IN (0,1) " +
            "  ORDER BY CASE r.rel_type WHEN 0 THEN 0 ELSE 1 END, r.id DESC LIMIT 1), '') AS OwnerPhone, " +
            "p.del_flag AS DelFlag, p.created_at AS CreatedAt, p.updated_at AS UpdatedAt";

        private class PropertyRead
        {
            public int Id { get; set; }
            public int? BuildingId { get; set; }
            public int? UnitId { get; set; }
            public string UnitNo { get; set; }
            public string BuildingNo { get; set; }
            public string CommunityName { get; set; }
            public string UnitPath { get; set; }
            public string RoomNo { get; set; }
            public decimal Area { get; set; }
            public PropertyUsage Usage { get; set; }
            public PropertyStatus Status { get; set; }
            public string OwnerName { get; set; }
            public string OwnerPhone { get; set; }
            public bool DelFlag { get; set; }
            public DateTime? CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }
        }

        private static PropertyDto ToProperty(PropertyRead r)
        {
            return new PropertyDto
            {
                Id = r.Id,
                BuildingId = r.BuildingId,
                UnitId = r.UnitId,
                UnitNo = r.UnitNo,
                BuildingNo = r.BuildingNo,
                CommunityName = r.CommunityName,
                UnitPath = r.UnitPath,
                RoomNo = r.RoomNo,
                Area = r.Area,
                Usage = r.Usage,
                Status = r.Status,
                OwnerName = r.OwnerName,
                OwnerPhone = r.OwnerPhone,
                CurrentArrear = 0m,
                DelFlag = r.DelFlag,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            };
        }

        private static void FillArrears(IDbConnection connection, List<PropertyDto> items)
        {
            if (items == null || items.Count == 0) return;
            var ids = items.Select(x => x.Id).Distinct().ToList();
            var map = QueryRawSum(connection, ids,
                "SELECT property_id, SUM(amount - paid_amount) AS v FROM t_bill " +
                "WHERE del_flag = 0 AND amount > paid_amount AND status IN (0,1,2) AND property_id IN ({0}) GROUP BY property_id");
            foreach (var it in items)
                it.CurrentArrear = map.TryGetValue(it.Id, out var v) ? v : 0m;
        }

        public int InsertProperty(IDbConnection connection, IDbTransaction transaction, PropertyDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_property (building_id, unit_id, room_no, area, usage, status, del_flag) " +
                "VALUES (@BuildingId, @UnitId, @RoomNo, @Area, @Usage, @Status, 0); SELECT last_insert_rowid();",
                new { dto.BuildingId, dto.UnitId, dto.RoomNo, dto.Area, dto.Usage, dto.Status }, transaction);
        }

        public void UpdateProperty(IDbConnection connection, IDbTransaction transaction, PropertyDto dto)
        {
            connection.Execute(
                "UPDATE t_property SET building_id = @BuildingId, unit_id = @UnitId, room_no = @RoomNo, area = @Area, usage = @Usage, " +
                "status = @Status, updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.BuildingId, dto.UnitId, dto.RoomNo, dto.Area, dto.Usage, dto.Status }, transaction);
        }

        public void SoftDeleteProperty(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_property SET del_flag = 1, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        public PropertyDto GetPropertyByUnitRoom(IDbConnection connection, IDbTransaction transaction, int unitId, string roomNo, int excludeId)
        {
            return connection.QueryFirstOrDefault<PropertyDto>(
                "SELECT id FROM t_property WHERE unit_id = @unitId AND room_no = @roomNo AND del_flag = 0 AND id <> @excludeId",
                new { unitId, roomNo, excludeId }, transaction);
        }

        /// <summary>无单元房产按「楼栋 + 房号」判重（BR-INF-01，无单元口径）。</summary>
        public PropertyDto GetPropertyByBuildingRoom(IDbConnection connection, IDbTransaction transaction, int buildingId, string roomNo, int excludeId)
        {
            return connection.QueryFirstOrDefault<PropertyDto>(
                "SELECT id FROM t_property WHERE building_id = @buildingId AND room_no = @roomNo AND del_flag = 0 AND id <> @excludeId",
                new { buildingId, roomNo, excludeId }, transaction);
        }

        // ===================== 业主 =====================
        public OwnerDto GetOwner(IDbConnection connection, int id)
        {
            var row = connection.QueryFirstOrDefault<OwnerRead>(
                OwnerSelectSql + " " + OwnerFromSql + " WHERE o.id = @id AND o.del_flag = 0", new { id });
            if (row == null) return null;
            var dto = ToOwner(row);
            FillOwnerStats(connection, new List<OwnerDto> { dto });
            return dto;
        }

        public PageResult<OwnerDto> QueryOwners(IDbConnection connection, BaseInfoQueryRequest query, out int total)
        {
            string where = "WHERE o.del_flag = 0";
            var p = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                where += " AND (o.name LIKE @kw OR o.phone LIKE @kw OR o.id_card LIKE @kw)";
                p.Add("kw", "%" + query.Keyword.Trim() + "%");
            }
            if (!string.IsNullOrWhiteSpace(query.Phone))
            {
                where += " AND o.phone LIKE @phone";
                p.Add("phone", "%" + query.Phone.Trim() + "%");
            }
            if (query.OwnerStatus.HasValue)
            {
                where += " AND o.status = @ostatus";
                p.Add("ostatus", (int)query.OwnerStatus.Value);
            }
            string from = "FROM t_owner o";
            total = connection.ExecuteScalar<int>("SELECT COUNT(1) " + from + " " + where, p);
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;
            p.Add("limit", pageSize);
            p.Add("offset", offset);
            string sql = OwnerSelectSql + " " + from + " " + where + " ORDER BY o.id LIMIT @limit OFFSET @offset";
            var items = connection.Query<OwnerRead>(sql, p).Select(ToOwner).ToList();
            FillOwnerStats(connection, items);
            return new PageResult<OwnerDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }

        /// <summary>单个业主 + 指定年度统计（业主档案 PDF 用；与列表同源同口径）。</summary>
        public OwnerDto GetOwnerWithYearStats(IDbConnection connection, int id, int year)
        {
            OwnerDto owner = GetOwner(connection, id);
            if (owner == null) { return null; }
            var map = QueryOwnerStats(connection, new List<int> { id }, year);
            OwnerStat stat;
            if (map.TryGetValue(id, out stat))
            {
                owner.YearReceivable = stat.YearReceivable;
                owner.YearPaid = stat.YearPaid;
                owner.CurrentArrear = stat.CurrentArrear;
            }
            return owner;
        }

        /// <summary>
        /// 业主列表 + 指定年度统计（CHG-v1.2.0-19）：与 <see cref="QueryOwners"/> 同一套筛选与排序，
        /// 但一次批量统计年度金额 → 「全部业主汇总缴费明细」PDF 不再逐户查询。
        /// </summary>
        public List<OwnerDto> QueryOwnersWithYearStats(IDbConnection connection, BaseInfoQueryRequest query, int year, out int total)
        {
            query = query ?? new BaseInfoQueryRequest();
            List<OwnerDto> items = QueryOwners(connection, query, out total).Items ?? new List<OwnerDto>();
            FillOwnerStats(connection, items, year);
            return items;
        }

        /// <summary>
        /// 业主名下房产路径（CHG-v1.2.0-20，一次批量查询）：
        /// 格式与「收支明细流水」的「楼栋/房号」列同口径（`1号楼/1单元/101`，无单元则 `1号楼/101`），
        /// 一位业主多处房产按关系建档顺序全部返回。
        /// </summary>
        public Dictionary<int, List<string>> QueryOwnerPropertyPaths(IDbConnection connection, IEnumerable<int> ownerIds)
        {
            var map = new Dictionary<int, List<string>>();
            List<int> ids = (ownerIds ?? Enumerable.Empty<int>()).Distinct().Where(x => x > 0).ToList();
            if (ids.Count == 0) { return map; }
            var rows = connection.Query<OwnerPropertyPathRow>(
                "SELECT r.owner_id AS OwnerId, " +
                SqlAddress.BuildingUnitRoom("bld.building_no", "u.unit_no", "p.room_no") + " AS Path " +
                "FROM t_owner_property_rel r " +
                "JOIN t_property p ON p.id = r.property_id AND p.del_flag = 0 " +
                "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                "LEFT JOIN t_building bld ON bld.id = COALESCE(u.building_id, p.building_id) " +
                "WHERE r.del_flag = 0 AND r.owner_id IN @ids " +
                "ORDER BY r.owner_id, r.id", new { ids }).ToList();
            foreach (OwnerPropertyPathRow row in rows)
            {
                string path = (row.Path ?? string.Empty).Trim();
                if (path.Length == 0) { continue; }
                List<string> list;
                if (!map.TryGetValue(row.OwnerId, out list)) { list = new List<string>(); map[row.OwnerId] = list; }
                if (!list.Contains(path)) { list.Add(path); }
            }
            return map;
        }

        private sealed class OwnerPropertyPathRow
        {
            public int OwnerId { get; set; }
            public string Path { get; set; }
        }

        private const string OwnerSelectSql =
            "SELECT o.id, o.name, o.id_card_type AS IdCardType, o.id_card AS IdCard, o.phone, " +
            "o.resident_address AS ResidentAddress, o.emergency_contact_name AS EmergencyContactName, " +
            "o.emergency_contact_phone AS EmergencyContactPhone, o.check_in_date AS CheckInDate, o.status AS Status, " +
            "CASE o.status WHEN 0 THEN '在住' ELSE '搬离' END AS StatusText, " +
            "(SELECT COUNT(1) FROM t_owner_property_rel r WHERE r.owner_id = o.id AND r.del_flag = 0 AND r.rel_status IN (0,1)) AS PropertyCount, " +
            "o.del_flag AS DelFlag, o.created_at AS CreatedAt, o.updated_at AS UpdatedAt";

        private const string OwnerFromSql = "FROM t_owner o";

        private class OwnerRead
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public OwnerIdCardType IdCardType { get; set; }
            public string IdCard { get; set; }
            public string Phone { get; set; }
            public string ResidentAddress { get; set; }
            public string EmergencyContactName { get; set; }
            public string EmergencyContactPhone { get; set; }
            public DateTime? CheckInDate { get; set; }
            public OwnerStatus Status { get; set; }
            public string StatusText { get; set; }
            public int PropertyCount { get; set; }
            public bool DelFlag { get; set; }
            public DateTime? CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }
        }

        private static OwnerDto ToOwner(OwnerRead r)
        {
            return new OwnerDto
            {
                Id = r.Id,
                Name = r.Name,
                IdCardType = r.IdCardType,
                IdCard = r.IdCard,
                Phone = r.Phone,
                ResidentAddress = r.ResidentAddress,
                EmergencyContactName = r.EmergencyContactName,
                EmergencyContactPhone = r.EmergencyContactPhone,
                CheckInDate = r.CheckInDate,
                Status = r.Status,
                StatusText = r.StatusText,
                PropertyCount = r.PropertyCount,
                YearReceivable = 0m,
                YearPaid = 0m,
                CurrentArrear = 0m,
                DelFlag = r.DelFlag,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            };
        }

        private static void FillOwnerStats(IDbConnection connection, List<OwnerDto> items, int? year = null)
        {
            if (items == null || items.Count == 0) return;
            var ids = items.Select(x => x.Id).Distinct().ToList();
            var map = QueryOwnerStats(connection, ids, year);
            foreach (var it in items)
            {
                if (map.TryGetValue(it.Id, out var s))
                {
                    it.YearReceivable = s.YearReceivable;
                    it.YearPaid = s.YearPaid;
                    it.CurrentArrear = s.CurrentArrear;
                }
            }
        }

        /// <summary>用原生态 IDataReader 读取 SUM 聚合（System.Data.SQLite 经 Dapper 强类型读取器对 DOUBLE 聚合值会抛 InvalidCastException）。</summary>
        private static Dictionary<int, decimal> QueryRawSum(IDbConnection connection, List<int> ids, string sqlTemplate)
        {
            var map = new Dictionary<int, decimal>();
            using (IDbCommand cmd = connection.CreateCommand())
            {
                string placeholders = string.Join(",", ids.Select((x, i) => "@id" + i));
                cmd.CommandText = string.Format(sqlTemplate, placeholders);
                for (int i = 0; i < ids.Count; i++)
                {
                    IDbDataParameter par = cmd.CreateParameter();
                    par.ParameterName = "id" + i;
                    par.Value = ids[i];
                    cmd.Parameters.Add(par);
                }
                using (IDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int id = Convert.ToInt32(reader.GetValue(0));
                        decimal v = Convert.ToDecimal(reader.GetValue(1));
                        map[id] = v;
                    }
                }
            }
            return map;
        }

        /// <summary>
        /// 业主年度应缴/已缴/欠费统计（原生读取，规避 Dapper DOUBLE 聚合读取异常）。
        ///
        /// CHG-v1.2.0-11（负责人 2026-09-21 反馈「收款成功后业主档案本年度缴费概况全是 0」）：
        /// 原实现只统计**房产账单**（`t_bill.property_id = 业主-房产关系.property_id`），
        /// 而 `t_bill.property_id` 在「车位账单」「业主直缴账单」下为 NULL ——
        /// 这两类账单的收款自然进不了业主概况（跨模块引用断链）。现按缴费对象三类归属统一汇总：
        ///   ① 房产账单 → 该房产的业主关系（原口径）；
        ///   ② 车位账单 → 车位绑定的业主（车位未绑业主时，回落到车位绑定房产的业主）；
        ///   ③ 业主直缴账单（owner_id 即业主本人）→ 直接归属该业主。
        /// </summary>
        /// <param name="year">统计年度；null = 当前年度。</param>
        /// <remarks>
        /// CHG-v1.2.0-16（负责人 2026-09-21 反馈「收款后概况仍为 0」的第二个根因）：
        /// 年度口径与**仪表盘**统一为「账期起始日所在年度」（`COALESCE(cycle.start_date, due_at)`）——
        /// 原口径按「到期日年度」，而跨年账期（如 2026-06-30 ~ 2027-06-30）的到期日落在次年，
        /// 于是本年度刚收的款在业主概况里显示 0；仪表盘早已按账期起始日统计，两处口径因此还对不上。
        /// </remarks>
        private static Dictionary<int, OwnerStat> QueryOwnerStats(IDbConnection connection, List<int> ids, int? year = null)
        {
            var map = new Dictionary<int, OwnerStat>();
            if (ids == null || ids.Count == 0) { return map; }
            string statYear = (year ?? DateTime.Today.Year).ToString();
            using (IDbCommand cmd = connection.CreateCommand())
            {
                string byProperty = string.Join(",", ids.Select((x, i) => "@p" + i));
                string byParking = string.Join(",", ids.Select((x, i) => "@k" + i));
                string byOwner = string.Join(",", ids.Select((x, i) => "@o" + i));
                cmd.CommandText =
                    "SELECT x.owner_id, " +
                    "SUM(CASE WHEN COALESCE(strftime('%Y', x.cycle_start), strftime('%Y', x.due_at)) = @statYear THEN x.amount ELSE 0 END), " +
                    "SUM(CASE WHEN COALESCE(strftime('%Y', x.cycle_start), strftime('%Y', x.due_at)) = @statYear THEN x.paid_amount ELSE 0 END), " +
                    "SUM(CASE WHEN x.amount > x.paid_amount AND x.status IN (0,1,2) THEN x.amount - x.paid_amount ELSE 0 END) " +
                    "FROM (" +
                    // ① 房产账单：按房产的业主关系归属
                    "SELECT r.owner_id AS owner_id, b.amount, b.paid_amount, b.status, b.due_at, cy.start_date AS cycle_start, " +
                    "       b.created_at AS created_at " +
                    "FROM t_owner_property_rel r JOIN t_bill b ON b.property_id = r.property_id " +
                    "LEFT JOIN t_billing_cycle cy ON cy.id = b.cycle_id " +
                    "WHERE r.del_flag = 0 AND b.del_flag = 0 AND r.owner_id IN (" + byProperty + ") " +
                    "UNION ALL " +
                    // ② 车位账单：按车位绑定业主归属；车位没绑业主时回落到车位绑定房产的业主
                    "SELECT COALESCE(pk.owner_id, r2.owner_id) AS owner_id, b.amount, b.paid_amount, b.status, b.due_at, " +
                    "       cy.start_date AS cycle_start, b.created_at AS created_at " +
                    "FROM t_bill b " +
                    "LEFT JOIN t_parking_space pk ON pk.id = b.parking_id AND pk.del_flag = 0 " +
                    "LEFT JOIN t_owner_property_rel r2 ON r2.property_id = pk.property_id AND r2.del_flag = 0 " +
                    "LEFT JOIN t_billing_cycle cy ON cy.id = b.cycle_id " +
                    "WHERE b.del_flag = 0 AND b.parking_id IS NOT NULL AND COALESCE(pk.owner_id, r2.owner_id) IN (" + byParking + ") " +
                    "UNION ALL " +
                    // ③ 业主直缴账单：账单 owner_id 即业主本人
                    "SELECT b.owner_id AS owner_id, b.amount, b.paid_amount, b.status, b.due_at, " +
                    "       cy.start_date AS cycle_start, b.created_at AS created_at " +
                    "FROM t_bill b " +
                    "LEFT JOIN t_billing_cycle cy ON cy.id = b.cycle_id " +
                    "WHERE b.del_flag = 0 AND b.owner_id IS NOT NULL AND b.property_id IS NULL AND b.parking_id IS NULL " +
                    "AND b.owner_id IN (" + byOwner + ")" +
                    ") x GROUP BY x.owner_id";
                IDbDataParameter yearPar = cmd.CreateParameter();
                yearPar.ParameterName = "statYear";
                yearPar.Value = statYear;
                cmd.Parameters.Add(yearPar);
                for (int i = 0; i < ids.Count; i++)
                {
                    AddParameter(cmd, "p" + i, ids[i]);
                    AddParameter(cmd, "k" + i, ids[i]);
                    AddParameter(cmd, "o" + i, ids[i]);
                }
                using (IDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int id = Convert.ToInt32(reader.GetValue(0));
                        decimal yearRecv = Convert.ToDecimal(reader.GetValue(1));
                        decimal yearPaid = Convert.ToDecimal(reader.GetValue(2));
                        decimal arrear = Convert.ToDecimal(reader.GetValue(3));
                        map[id] = new OwnerStat { YearReceivable = yearRecv, YearPaid = yearPaid, CurrentArrear = arrear };
                    }
                }
            }
            return map;
        }

        private class OwnerStat
        {
            public decimal YearReceivable { get; set; }
            public decimal YearPaid { get; set; }
            public decimal CurrentArrear { get; set; }
        }

        /// <summary>给原生 IDbCommand 追加一个参数（业主统计的三段 UNION 复用同一批 ID）。</summary>
        private static void AddParameter(IDbCommand cmd, string name, object value)
        {
            IDbDataParameter par = cmd.CreateParameter();
            par.ParameterName = name;
            par.Value = value;
            cmd.Parameters.Add(par);
        }

        public int InsertOwner(IDbConnection connection, IDbTransaction transaction, OwnerDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_owner (name, id_card_type, id_card, phone, resident_address, " +
                "emergency_contact_name, emergency_contact_phone, check_in_date, status, del_flag) " +
                "VALUES (@Name, @IdCardType, @IdCard, @Phone, @ResidentAddress, @EmergencyContactName, " +
                "@EmergencyContactPhone, @CheckInDate, @Status, 0); SELECT last_insert_rowid();",
                new { dto.Name, dto.IdCardType, dto.IdCard, dto.Phone, dto.ResidentAddress, dto.EmergencyContactName, dto.EmergencyContactPhone, dto.CheckInDate, dto.Status }, transaction);
        }

        public void UpdateOwner(IDbConnection connection, IDbTransaction transaction, OwnerDto dto)
        {
            connection.Execute(
                "UPDATE t_owner SET name = @Name, id_card_type = @IdCardType, id_card = @IdCard, phone = @Phone, " +
                "resident_address = @ResidentAddress, emergency_contact_name = @EmergencyContactName, " +
                "emergency_contact_phone = @EmergencyContactPhone, check_in_date = @CheckInDate, status = @Status, " +
                "updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.Name, dto.IdCardType, dto.IdCard, dto.Phone, dto.ResidentAddress, dto.EmergencyContactName, dto.EmergencyContactPhone, dto.CheckInDate, dto.Status }, transaction);
        }

        public void SoftDeleteOwner(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_owner SET del_flag = 1, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        // ===================== 业主-房产关系 =====================
        private const string RelationSelectSql =
            "SELECT r.id, r.property_id AS PropertyId, r.owner_id AS OwnerId, " +
            "COALESCE(p.room_no, '') AS PropertyRoomNo, " +
            "COALESCE((SELECT building_no FROM t_building WHERE id = p.building_id), b.building_no, '') AS BuildingNo, COALESCE(u.unit_no, '') AS UnitNo, " +
            "COALESCE((SELECT building_no FROM t_building WHERE id = p.building_id), b.building_no, '') || COALESCE(u.unit_no,'') || COALESCE(p.room_no,'') AS PropertyUnitPath, " +
            "COALESCE(o.name, '') AS OwnerName, COALESCE(o.phone, '') AS OwnerPhone, " +
            "r.rel_type AS RelType, r.share AS Share, r.effective_at AS EffectiveAt, r.expire_at AS ExpireAt, " +
            "r.rel_status AS Status, " +
            "CASE r.rel_status WHEN 0 THEN '有效' WHEN 1 THEN '即将到期' ELSE '已解除' END AS StatusText, " +
            "r.del_flag AS DelFlag, r.created_at AS CreatedAt, r.updated_at AS UpdatedAt";

        public OwnerPropertyRelationDto GetRelation(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<OwnerPropertyRelationDto>(
                RelationSelectSql +
                " FROM t_owner_property_rel r " +
                "LEFT JOIN t_property p ON p.id = r.property_id " +
                "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                "LEFT JOIN t_building b ON b.id = u.building_id " +
                "LEFT JOIN t_owner o ON o.id = r.owner_id " +
                "WHERE r.id = @id AND r.del_flag = 0", new { id });
        }

        public PageResult<OwnerPropertyRelationDto> QueryRelations(IDbConnection connection, BaseInfoQueryRequest query, out int total)
        {
            string where = "WHERE r.del_flag = 0";
            var p = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                where += " AND (p.room_no LIKE @kw OR o.name LIKE @kw OR o.phone LIKE @kw)";
                p.Add("kw", "%" + query.Keyword.Trim() + "%");
            }
            if (!string.IsNullOrWhiteSpace(query.RoomNo))
            {
                where += " AND p.room_no LIKE @room";
                p.Add("room", "%" + query.RoomNo.Trim() + "%");
            }
            if (!string.IsNullOrWhiteSpace(query.OwnerName))
            {
                where += " AND o.name LIKE @owner";
                p.Add("owner", "%" + query.OwnerName.Trim() + "%");
            }
            if (query.RelType.HasValue)
            {
                where += " AND r.rel_type = @relType";
                p.Add("relType", (int)query.RelType.Value);
            }
            if (query.RelStatus.HasValue)
            {
                where += " AND r.rel_status = @relStatus";
                p.Add("relStatus", (int)query.RelStatus.Value);
            }
            string from = "FROM t_owner_property_rel r " +
                "LEFT JOIN t_property p ON p.id = r.property_id " +
                "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                "LEFT JOIN t_building b ON b.id = u.building_id " +
                "LEFT JOIN t_owner o ON o.id = r.owner_id";
            total = connection.ExecuteScalar<int>("SELECT COUNT(1) " + from + " " + where, p);
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;
            p.Add("limit", pageSize);
            p.Add("offset", offset);
            string sql = RelationSelectSql + " " + from + " " + where + " ORDER BY r.id DESC LIMIT @limit OFFSET @offset";
            var items = connection.Query<OwnerPropertyRelationDto>(sql, p).ToList();
            return new PageResult<OwnerPropertyRelationDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }

        public List<OwnerPropertyRelationDto> ListRelationsByOwner(IDbConnection connection, int ownerId)
        {
            return connection.Query<OwnerPropertyRelationDto>(
                RelationSelectSql + " FROM t_owner_property_rel r " +
                "LEFT JOIN t_property p ON p.id = r.property_id " +
                "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                "LEFT JOIN t_building b ON b.id = u.building_id " +
                "LEFT JOIN t_owner o ON o.id = r.owner_id " +
                "WHERE r.owner_id = @ownerId AND r.del_flag = 0 ORDER BY r.id DESC", new { ownerId }).ToList();
        }

        public List<OwnerPropertyRelationDto> ListRelationsByProperty(IDbConnection connection, int propertyId)
        {
            return connection.Query<OwnerPropertyRelationDto>(
                RelationSelectSql + " FROM t_owner_property_rel r " +
                "LEFT JOIN t_property p ON p.id = r.property_id " +
                "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                "LEFT JOIN t_building b ON b.id = u.building_id " +
                "LEFT JOIN t_owner o ON o.id = r.owner_id " +
                "WHERE r.property_id = @propertyId AND r.del_flag = 0 ORDER BY r.id DESC", new { propertyId }).ToList();
        }

        public int InsertRelation(IDbConnection connection, IDbTransaction transaction, OwnerPropertyRelationDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_owner_property_rel (property_id, owner_id, rel_type, share, effective_at, expire_at, rel_status, del_flag) " +
                "VALUES (@PropertyId, @OwnerId, @RelType, @Share, @EffectiveAt, @ExpireAt, @RelStatus, 0); SELECT last_insert_rowid();",
                new { dto.PropertyId, dto.OwnerId, dto.RelType, dto.Share, dto.EffectiveAt, dto.ExpireAt, RelStatus = dto.Status }, transaction);
        }

        public void UpdateRelation(IDbConnection connection, IDbTransaction transaction, OwnerPropertyRelationDto dto)
        {
            connection.Execute(
                "UPDATE t_owner_property_rel SET property_id = @PropertyId, owner_id = @OwnerId, rel_type = @RelType, " +
                "share = @Share, effective_at = @EffectiveAt, expire_at = @ExpireAt, rel_status = @RelStatus, " +
                "updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.PropertyId, dto.OwnerId, dto.RelType, dto.Share, dto.EffectiveAt, dto.ExpireAt, RelStatus = dto.Status }, transaction);
        }

        public void SoftDeleteRelation(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_owner_property_rel SET rel_status = 2, del_flag = 1, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        public int CountActiveOwnerRelationsByProperty(IDbConnection connection, IDbTransaction transaction, int propertyId, int excludeId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_owner_property_rel " +
                "WHERE property_id = @propertyId AND del_flag = 0 AND rel_status IN (0,1) AND rel_type = 0 AND id <> @excludeId",
                new { propertyId, excludeId }, transaction);
        }

        // ===================== 车位 =====================
        private const string ParkingSelectSql =
            "SELECT p.id, p.space_no AS SpaceNo, COALESCE(p.area,'') AS Area, p.space_type AS SpaceType, " +
            "p.status AS Status, p.property_id AS PropertyId, p.owner_id AS OwnerId, " +
            "COALESCE(pr.room_no, '') AS BindingProperty, COALESCE(o.name, '') AS OwnerName, " +
            "p.monthly_rent AS MonthlyRent, p.rent_mode AS RentMode, p.rent_to AS RentTo, " +
            "CASE p.status WHEN 0 THEN '已售' WHEN 1 THEN '已租' WHEN 2 THEN '空置' ELSE '维修中' END AS StatusText, " +
            "CASE p.space_type WHEN 0 THEN '产权' WHEN 1 THEN '普通' ELSE '临时' END AS SpaceTypeText, " +
            "p.del_flag AS DelFlag, p.created_at AS CreatedAt, p.updated_at AS UpdatedAt";

        public ParkingSpaceDto GetParking(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<ParkingSpaceDto>(
                ParkingSelectSql + " FROM t_parking_space p " +
                "LEFT JOIN t_property pr ON pr.id = p.property_id " +
                "LEFT JOIN t_owner o ON o.id = p.owner_id " +
                "WHERE p.id = @id AND p.del_flag = 0", new { id });
        }

        /// <summary>CHG-v1.1.0-12：业主名下有效房产 ID（按楼栋/房号排序，供车位「绑定房产」自动引用）。</summary>
        public List<int> ListActivePropertyIdsByOwner(IDbConnection connection, int ownerId)
        {
            return connection.Query<int>(
                "SELECT p.id FROM t_property p " +
                "JOIN t_owner_property_rel rel ON rel.property_id = p.id " +
                "WHERE rel.owner_id = @ownerId AND rel.del_flag = 0 AND rel.rel_status <> 2 AND p.del_flag = 0 " +
                "ORDER BY p.building_id, p.room_no", new { ownerId }).ToList();
        }

        public PageResult<ParkingSpaceDto> QueryParkings(IDbConnection connection, BaseInfoQueryRequest query, out int total)
        {
            string where = "WHERE p.del_flag = 0";
            var p = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(query.SpaceNo))
            {
                where += " AND p.space_no LIKE @spaceNo";
                p.Add("spaceNo", "%" + query.SpaceNo.Trim() + "%");
            }
            if (query.SpaceType.HasValue)
            {
                where += " AND p.space_type = @spaceType";
                p.Add("spaceType", (int)query.SpaceType.Value);
            }
            if (query.SpaceStatus.HasValue)
            {
                where += " AND p.status = @spaceStatus";
                p.Add("spaceStatus", (int)query.SpaceStatus.Value);
            }
            string from = "FROM t_parking_space p " +
                "LEFT JOIN t_property pr ON pr.id = p.property_id " +
                "LEFT JOIN t_owner o ON o.id = p.owner_id";
            total = connection.ExecuteScalar<int>("SELECT COUNT(1) " + from + " " + where, p);
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;
            p.Add("limit", pageSize);
            p.Add("offset", offset);
            string sql = ParkingSelectSql + " " + from + " " + where + " ORDER BY p.id LIMIT @limit OFFSET @offset";
            var items = connection.Query<ParkingSpaceDto>(sql, p).ToList();
            return new PageResult<ParkingSpaceDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }

        public int InsertParking(IDbConnection connection, IDbTransaction transaction, ParkingSpaceDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_parking_space (space_no, area, space_type, status, property_id, owner_id, monthly_rent, rent_mode, rent_to, del_flag) " +
                "VALUES (@SpaceNo, @Area, @SpaceType, @Status, @PropertyId, @OwnerId, @MonthlyRent, @RentMode, @RentTo, 0); SELECT last_insert_rowid();",
                new { dto.SpaceNo, dto.Area, dto.SpaceType, dto.Status, dto.PropertyId, dto.OwnerId, dto.MonthlyRent, dto.RentMode, dto.RentTo }, transaction);
        }

        public void UpdateParking(IDbConnection connection, IDbTransaction transaction, ParkingSpaceDto dto)
        {
            connection.Execute(
                "UPDATE t_parking_space SET space_no = @SpaceNo, area = @Area, space_type = @SpaceType, status = @Status, " +
                "property_id = @PropertyId, owner_id = @OwnerId, monthly_rent = @MonthlyRent, rent_mode = @RentMode, rent_to = @RentTo, " +
                "updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.SpaceNo, dto.Area, dto.SpaceType, dto.Status, dto.PropertyId, dto.OwnerId, dto.MonthlyRent, dto.RentMode, dto.RentTo }, transaction);
        }

        public void SoftDeleteParking(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_parking_space SET del_flag = 1, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        public ParkingSpaceDto GetParkingByNo(IDbConnection connection, string spaceNo, int excludeId)
        {
            return connection.QueryFirstOrDefault<ParkingSpaceDto>(
                "SELECT id FROM t_parking_space WHERE space_no = @spaceNo AND del_flag = 0 AND id <> @excludeId",
                new { spaceNo, excludeId });
        }

        public int CountParkingsBoundToProperty(IDbConnection connection, IDbTransaction transaction, int propertyId, int excludeId, int propertyRightType)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_parking_space WHERE property_id = @propertyId AND del_flag = 0 AND space_type = @propertyRightType AND id <> @excludeId",
                new { propertyId, excludeId, propertyRightType }, transaction);
        }

        // ===================== 变更记录 =====================
        public List<BaseChangeLogDto> ListChangeLogs(IDbConnection connection, int objectType, int objectId)
        {
            return connection.Query<BaseChangeLogDto>(
                "SELECT id, object_type AS ObjectType, object_id AS ObjectId, " +
                "CASE object_type WHEN 0 THEN '业主' WHEN 1 THEN '房产' WHEN 2 THEN '关系' ELSE '车位' END AS ObjectTypeText, " +
                "change_content AS ChangeContent, field_name AS FieldName, old_value AS OldValue, new_value AS NewValue, " +
                "operator AS Operator, channel AS Channel, changed_at AS ChangedAt " +
                "FROM t_base_change_log WHERE object_type = @objectType AND object_id = @objectId ORDER BY id DESC",
                new { objectType, objectId }).ToList();
        }

        public void InsertChangeLog(IDbConnection connection, IDbTransaction transaction, BaseChangeLogDto dto)
        {
            connection.Execute(
                "INSERT INTO t_base_change_log (object_type, object_id, change_content, field_name, old_value, new_value, operator, channel, changed_at) " +
                "VALUES (@ObjectType, @ObjectId, @ChangeContent, @FieldName, @OldValue, @NewValue, @Operator, @Channel, @ChangedAt)",
                new { dto.ObjectType, dto.ObjectId, dto.ChangeContent, dto.FieldName, dto.OldValue, dto.NewValue, dto.Operator, dto.Channel, dto.ChangedAt }, transaction);
        }

        // ===================== 导入 =====================
        public int InsertImportLog(IDbConnection connection, IDbTransaction transaction, ImportLogDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_import_log (module, file_name, total, success, fail, error_file, status, created_by) " +
                "VALUES (@Module, @FileName, @Total, @Success, @Fail, @ErrorFile, @Status, @CreatedBy); SELECT last_insert_rowid();",
                new { dto.Module, dto.FileName, dto.Total, dto.Success, dto.Fail, dto.ErrorFile, dto.Status, dto.CreatedBy }, transaction);
        }

        public void UpdateImportLog(IDbConnection connection, IDbTransaction transaction, int id, int total, ImportStatus status, int success, int fail, string errorFile)
        {
            // v1.2.0：补写 total（INSERT 时行数还没解析出来，只能先写 0）
            connection.Execute(
                "UPDATE t_import_log SET total = @total, status = @status, success = @success, fail = @fail, " +
                "error_file = @errorFile WHERE id = @id",
                new { id, total, status = (int)status, success, fail, errorFile }, transaction);
        }

        /// <summary>导入批次「覆盖」计数（CHG-v1.1.2-01：重复数据做覆盖处理）。</summary>
        public void UpdateImportLogUpdated(IDbConnection connection, IDbTransaction transaction, int id, int updated)
        {
            connection.Execute(
                "UPDATE t_import_log SET updated_count = @updated WHERE id = @id",
                new { id, updated }, transaction);
        }

        public ImportLogDto GetImportLog(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<ImportLogDto>(ImportLogSelectSql + " WHERE l.id = @id", new { id });
        }

        public List<ImportLogDto> ListImportLogs(IDbConnection connection)
        {
            // v1.1.0-⑤：批次记录支持批量删除（软删留痕 del_flag=1）→ 列表只返回未删除批次
            return connection.Query<ImportLogDto>(
                ImportLogSelectSql + " WHERE l.del_flag = 0 ORDER BY l.id DESC LIMIT 200").ToList();
        }

        /// <summary>导入批次记录批量删除（v1.1.0-⑤）：软删留痕，可被「一键清理残余数据」物理清理。</summary>
        public int SoftDeleteImportLogs(IDbConnection connection, IDbTransaction transaction, IEnumerable<int> ids)
        {
            var list = (ids ?? Enumerable.Empty<int>()).Distinct().Where(x => x > 0).ToList();
            if (list.Count == 0) { return 0; }
            return connection.Execute(
                "UPDATE t_import_log SET del_flag = 1 WHERE id IN @ids AND del_flag = 0",
                new { ids = list }, transaction);
        }

        private const string ImportLogSelectSql =
            "SELECT l.id, l.module AS Module, " +
            "CASE l.module WHEN 0 THEN '房产' WHEN 1 THEN '业主' WHEN 2 THEN '车位' ELSE '业主-房产关系' END AS ModuleText, " +
            "l.file_name AS FileName, l.total AS Total, l.success AS Success, l.updated_count AS Updated, l.fail AS Fail, " +
            "l.error_file AS ErrorFile, l.status AS Status, " +
            "CASE l.status WHEN 0 THEN '校验中' WHEN 1 THEN '成功' WHEN 2 THEN '部分成功' ELSE '失败' END AS StatusText, " +
            "l.created_by AS CreatedBy, l.created_at AS CreatedAt FROM t_import_log l";

        public void InsertImportErrors(IDbConnection connection, IDbTransaction transaction, IEnumerable<ImportErrorItemDto> errors, int importId)
        {
            foreach (ImportErrorItemDto e in errors)
            {
                connection.Execute(
                    "INSERT INTO t_import_error (import_id, row_no, field, content, reason, suggestion) " +
                    "VALUES (@importId, @RowNo, @Field, @Content, @Reason, @Suggestion)",
                    new { importId, e.RowNo, e.Field, e.Content, e.Reason, e.Suggestion }, transaction);
            }
        }

        public List<ImportErrorItemDto> ListImportErrors(IDbConnection connection, int importId)
        {
            return connection.Query<ImportErrorItemDto>(
                "SELECT row_no AS RowNo, field AS Field, content AS Content, reason AS Reason, suggestion AS Suggestion " +
                "FROM t_import_error WHERE import_id = @importId ORDER BY row_no", new { importId }).ToList();
        }

        /// <summary>导入回执逐行结果落库（CHG-v1.2.0-01）。</summary>
        public void InsertImportRows(IDbConnection connection, IDbTransaction transaction, IEnumerable<ImportRowResultDto> rows, int importId)
        {
            foreach (ImportRowResultDto r in rows ?? Enumerable.Empty<ImportRowResultDto>())
            {
                connection.Execute(
                    "INSERT INTO t_import_row (import_id, row_no, result, object_key, change_summary, reason, suggestion) " +
                    "VALUES (@importId, @RowNo, @Result, @ObjectKey, @ChangeSummary, @Reason, @Suggestion)",
                    new
                    {
                        importId, r.RowNo, Result = (int)r.Result, r.ObjectKey, r.ChangeSummary, r.Reason, r.Suggestion
                    }, transaction);
            }
        }

        public List<ImportRowResultDto> ListImportRows(IDbConnection connection, int importId)
        {
            return connection.Query<ImportRowResultDto>(
                "SELECT row_no AS RowNo, result AS Result, object_key AS ObjectKey, change_summary AS ChangeSummary, " +
                "reason AS Reason, suggestion AS Suggestion FROM t_import_row WHERE import_id = @importId ORDER BY row_no",
                new { importId }).ToList();
        }

        // ===================== 导出 =====================
        public int InsertExportLog(IDbConnection connection, IDbTransaction transaction, string module, int format, string filePath)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_export_log (module, format, file_path) VALUES (@module, @format, @filePath); SELECT last_insert_rowid();",
                new { module, format, filePath }, transaction);
        }
    }
}
