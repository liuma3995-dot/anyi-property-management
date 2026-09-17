using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>财务仓储 SQLite 实现（D4-1~D4-6，Dapper）。</summary>
    public class SqlFinanceRepository : IFinanceRepository
    {
        // ---------- 收费项目 ----------
        public List<ChargeItemDto> ListChargeItems(IDbConnection connection, string keyword, string category)
        {
            string sql = "SELECT id, name, pay_mode AS PayMode, unit_price AS UnitPrice, " +
                         "cycle_type AS CycleType, object_type AS ObjectType, status, del_flag AS DelFlag, " +
                         "category, method_code AS MethodCode, method_name AS MethodName, " +
                         "price_unit AS PriceUnit, cycle_name AS CycleName, object_code AS ObjectCode, " +
                         // CHG-v1.1.0-17：缴费对象显示名取自 charge_object 字典（自定义项改名后项目列表同步刷新）
                         "(SELECT di.item_name FROM t_dict_item di WHERE di.type_code = 'charge_object' " +
                         " AND di.item_code = t_charge_item.object_code AND di.del_flag = 0 LIMIT 1) AS ObjectName " +
                         "FROM t_charge_item WHERE del_flag = 0";
            var parameters = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                sql += " AND name LIKE @kw";
                parameters["kw"] = "%" + keyword.Trim() + "%";
            }
            if (!string.IsNullOrWhiteSpace(category))
            {
                sql += " AND category = @category";
                parameters["category"] = category;
            }
            return connection.Query<ChargeItemDto>(sql, parameters).ToList();
        }

        public ChargeItemDto GetChargeItem(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<ChargeItemDto>(
                "SELECT id, name, pay_mode AS PayMode, unit_price AS UnitPrice, " +
                "cycle_type AS CycleType, object_type AS ObjectType, status, del_flag AS DelFlag, " +
                "category, method_code AS MethodCode, method_name AS MethodName, " +
                "price_unit AS PriceUnit, cycle_name AS CycleName, object_code AS ObjectCode, " +
                "(SELECT di.item_name FROM t_dict_item di WHERE di.type_code = 'charge_object' " +
                " AND di.item_code = t_charge_item.object_code AND di.del_flag = 0 LIMIT 1) AS ObjectName " +
                "FROM t_charge_item WHERE id = @id AND del_flag = 0", new { id });
        }

        public ChargeItemDto GetChargeItemByName(IDbConnection connection, string name)
        {
            return connection.QueryFirstOrDefault<ChargeItemDto>(
                "SELECT id, name, pay_mode AS PayMode, unit_price AS UnitPrice, cycle_type AS CycleType, " +
                "object_type AS ObjectType, status, del_flag AS DelFlag, category, method_code AS MethodCode, " +
                "method_name AS MethodName, price_unit AS PriceUnit, cycle_name AS CycleName, " +
                "object_code AS ObjectCode, " +
                "(SELECT di.item_name FROM t_dict_item di WHERE di.type_code = 'charge_object' " +
                " AND di.item_code = t_charge_item.object_code AND di.del_flag = 0 LIMIT 1) AS ObjectName " +
                "FROM t_charge_item WHERE name = @name AND del_flag = 0 LIMIT 1", new { name });
        }

        public int InsertChargeItem(IDbConnection connection, IDbTransaction transaction, ChargeItemDto item)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_charge_item (name, pay_mode, unit_price, cycle_type, object_type, status, del_flag, " +
                "category, method_code, method_name, price_unit, cycle_name, object_code) " +
                "VALUES (@Name, @PayMode, @UnitPrice, @CycleType, @ObjectType, @Status, 0, " +
                "@Category, @MethodCode, @MethodName, @PriceUnit, @CycleName, @ObjectCode); SELECT last_insert_rowid();",
                new { item.Name, item.PayMode, item.UnitPrice, item.CycleType, item.ObjectType, item.Status, item.Category, item.MethodCode, item.MethodName, item.PriceUnit, item.CycleName, item.ObjectCode }, transaction);
        }

        public void UpdateChargeItem(IDbConnection connection, IDbTransaction transaction, ChargeItemDto item)
        {
            connection.Execute(
                "UPDATE t_charge_item SET name = @Name, pay_mode = @PayMode, unit_price = @UnitPrice, " +
                "cycle_type = @CycleType, object_type = @ObjectType, status = @Status, " +
                "category = @Category, method_code = @MethodCode, method_name = @MethodName, " +
                "price_unit = @PriceUnit, cycle_name = @CycleName, object_code = @ObjectCode, " +
                "updated_at = datetime('now','localtime') " +
                "WHERE id = @Id AND del_flag = 0",
                new { item.Id, item.Name, item.PayMode, item.UnitPrice, item.CycleType, item.ObjectType, item.Status, item.Category, item.MethodCode, item.MethodName, item.PriceUnit, item.CycleName, item.ObjectCode }, transaction);
        }

        public void SoftDeleteChargeItem(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_charge_item SET del_flag = 1, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        // ---------- 计费周期 ----------
        public List<BillingCycleDto> ListCycles(IDbConnection connection)
        {
            return connection.Query<BillingCycleDto>(
                "SELECT id, cycle_type AS CycleType, start_date AS StartDate, end_date AS EndDate " +
                "FROM t_billing_cycle ORDER BY start_date DESC, id DESC").ToList();
        }

        public BillingCycleDto GetCycle(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<BillingCycleDto>(
                "SELECT id, cycle_type AS CycleType, start_date AS StartDate, end_date AS EndDate " +
                "FROM t_billing_cycle WHERE id = @id", new { id });
        }

        public int InsertCycle(IDbConnection connection, IDbTransaction transaction, BillingCycleDto cycle)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_billing_cycle (cycle_type, start_date, end_date) " +
                "VALUES (@CycleType, @StartDate, @EndDate); SELECT last_insert_rowid();",
                new { cycle.CycleType, cycle.StartDate, cycle.EndDate }, transaction);
        }

        public void UpdateCycle(IDbConnection connection, IDbTransaction transaction, BillingCycleDto cycle)
        {
            connection.Execute(
                "UPDATE t_billing_cycle SET cycle_type = @CycleType, start_date = @StartDate, " +
                "end_date = @EndDate WHERE id = @Id",
                new { cycle.Id, cycle.CycleType, cycle.StartDate, cycle.EndDate }, transaction);
        }

        public void DeleteCycle(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute("DELETE FROM t_billing_cycle WHERE id = @id", new { id }, transaction);
        }

        // ---------- 账单生成/发布 ----------
        public IEnumerable<BillObjectCandidate> ListPropertyCandidates(IDbConnection connection)
        {
            return connection.Query<BillObjectCandidate>(
                "SELECT id, room_no AS No, area AS Area FROM t_property WHERE del_flag = 0 ORDER BY room_no");
        }

        public IEnumerable<BillObjectCandidate> ListParkingCandidates(IDbConnection connection)
        {
            return connection.Query<BillObjectCandidate>(
                "SELECT id, space_no AS No, NULL AS Area FROM t_parking_space WHERE del_flag = 0 ORDER BY space_no");
        }

        /// <summary>CHG-v1.1.0-11：业主缴费对象候选（业主直缴）。</summary>
        public IEnumerable<BillObjectCandidate> ListOwnerCandidates(IDbConnection connection)
        {
            return connection.Query<BillObjectCandidate>(
                "SELECT id, name AS No, NULL AS Area FROM t_owner WHERE del_flag = 0 ORDER BY id");
        }

        /// <summary>
        /// CHG-v1.1.0-10／11：生成账单「缴费对象」候选查询（只读）。
        /// 房产：隐藏未绑定有效业主（rel.del_flag=0 且 rel_status≠2）的房产，回传隐藏数量；
        /// 车位：全部返回（外地车／无房只有车位是真实业务）；
        /// 业主：全部返回（业主直缴办卡费/清理费/维修费等）；排序按 楼栋 → 单元 → 房号 自然序。
        /// </summary>
        public BillObjectQueryResult QueryBillObjects(IDbConnection connection, string kind, string keyword, int limit)
        {
            bool parking = string.Equals(kind, "parking", StringComparison.OrdinalIgnoreCase);
            bool owner = string.Equals(kind, "owner", StringComparison.OrdinalIgnoreCase);
            string kw = (keyword ?? string.Empty).Trim();
            string norm = NormalizeObjectKey(kw);
            int max = limit <= 0 ? 2000 : limit;
            var parameters = new DynamicParameters();
            parameters.Add("limit", max + 1);   // 多取 1 行用于判断截断
            parameters.Add("kwLike", "%" + kw + "%");
            parameters.Add("normLike", "%" + norm + "%");
            bool hasKeyword = kw.Length > 0;

            var result = new BillObjectQueryResult { Items = new List<BillObjectCandidateDto>(), HiddenCount = 0, Truncated = false };

            if (owner)
            {
                string ownerWhere = "WHERE o.del_flag = 0";
                if (hasKeyword)
                {
                    ownerWhere += " AND ( COALESCE(o.name,'') LIKE @kwLike OR COALESCE(o.phone,'') LIKE @kwLike )";
                }
                string ownerSql =
                    "SELECT o.id AS Id, 'owner' AS Kind, o.name AS No, " +
                    "  '手机 ' || COALESCE(NULLIF(o.phone,''), '未登记') || ' · 名下房产 ' || " +
                    "  (SELECT COUNT(1) FROM t_owner_property_rel r " +
                    "   WHERE r.owner_id = o.id AND r.del_flag = 0 AND r.rel_status <> 2) || ' 套' AS SubText, " +
                    "  o.name AS OwnerName, 0 AS NoOwner, NULL AS Area " +
                    "FROM t_owner o " + ownerWhere + " ORDER BY o.id LIMIT @limit";
                var ownerRows = connection.Query<BillObjectCandidateDto>(ownerSql, parameters).ToList();
                if (ownerRows.Count > max)
                {
                    result.Truncated = true;
                    ownerRows.RemoveAt(ownerRows.Count - 1);
                }
                result.Items = ownerRows;
                return result;
            }

            if (parking)
            {
                string where = "WHERE ps.del_flag = 0";
                if (hasKeyword)
                {
                    where += " AND ( (COALESCE(ps.space_no,'') || COALESCE(p.room_no,'')) LIKE @kwLike" +
                             " OR " + NormalizedSql("COALESCE(ps.space_no,'') || COALESCE(p.room_no,'')") + " LIKE @normLike )";
                }
                string sql =
                    "SELECT ps.id AS Id, 'parking' AS Kind, ps.space_no AS No, " +
                    "  (CASE WHEN ps.space_type = 1 THEN '临时车位' ELSE '固定车位' END) || " +
                    "  (CASE WHEN p.id IS NULL THEN ' · 未绑定房产' " +
                    "        ELSE ' · 绑定 ' || TRIM(COALESCE(b.building_no,'') || ' ' || COALESCE(u.unit_no,'') || ' ' || COALESCE(p.room_no,'')) END) AS SubText, " +
                    "  COALESCE(o.name,'') AS OwnerName, " +
                    "  (CASE WHEN ps.owner_id IS NULL THEN 1 ELSE 0 END) AS NoOwner, NULL AS Area " +
                    "FROM t_parking_space ps " +
                    "LEFT JOIN t_owner o ON o.id = ps.owner_id " +
                    "LEFT JOIN t_property p ON p.id = ps.property_id AND p.del_flag = 0 " +
                    "LEFT JOIN t_building b ON b.id = p.building_id " +
                    "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                    where + " " +
                    "ORDER BY CAST(COALESCE(ps.space_no,'') AS INTEGER), ps.space_no LIMIT @limit";
                var rows = connection.Query<BillObjectCandidateDto>(sql, parameters).ToList();
                if (rows.Count > max)
                {
                    result.Truncated = true;
                    rows.RemoveAt(rows.Count - 1);
                }
                result.Items = rows;
                return result;
            }

            string propWhere = "WHERE p.del_flag = 0 AND o.id IS NOT NULL";
            if (hasKeyword)
            {
                string target = "COALESCE(b.building_no,'') || COALESCE(u.unit_no,'') || COALESCE(p.room_no,'')";
                propWhere += " AND ( (" + target + ") LIKE @kwLike OR " + NormalizedSql(target) + " LIKE @normLike )";
            }
            string propSql =
                "SELECT p.id AS Id, 'property' AS Kind, " +
                "  TRIM(COALESCE(b.building_no,'') || ' ' || COALESCE(u.unit_no,'') || ' ' || p.room_no) AS No, " +
                "  (CASE WHEN p.area IS NULL OR p.area <= 0 THEN '建筑面积未填写' ELSE '建筑面积 ' || printf('%.2f', p.area) || ' ㎡' END) || " +
                "  (CASE p.usage WHEN 1 THEN ' · 商铺' ELSE ' · 住宅' END) AS SubText, " +
                "  COALESCE(o.name,'') AS OwnerName, 0 AS NoOwner, p.area AS Area " +
                "FROM t_property p " +
                "LEFT JOIN t_building b ON b.id = p.building_id " +
                "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                "LEFT JOIN t_owner o ON o.id = (" +
                "  SELECT rel.owner_id FROM t_owner_property_rel rel " +
                "  WHERE rel.property_id = p.id AND rel.del_flag = 0 AND rel.rel_status <> 2 " +
                "  ORDER BY rel.id DESC LIMIT 1) " +
                propWhere + " " +
                "ORDER BY CAST(COALESCE(b.building_no,'') AS INTEGER), CAST(COALESCE(u.unit_no,'') AS INTEGER), " +
                "         CAST(p.room_no AS INTEGER), p.room_no LIMIT @limit";
            result.Items = connection.Query<BillObjectCandidateDto>(propSql, parameters).ToList();
            if (result.Items.Count > max)
            {
                result.Truncated = true;
                result.Items.RemoveAt(result.Items.Count - 1);
            }

            // 隐藏数量：与列表同口径（同关键字范围内未绑定有效业主的房产）
            string hiddenSql =
                "SELECT COUNT(1) FROM t_property p " +
                "LEFT JOIN t_building b ON b.id = p.building_id " +
                "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                "WHERE p.del_flag = 0 AND NOT EXISTS (" +
                "  SELECT 1 FROM t_owner_property_rel rel WHERE rel.property_id = p.id " +
                "  AND rel.del_flag = 0 AND rel.rel_status <> 2)";
            if (hasKeyword)
            {
                string target = "COALESCE(b.building_no,'') || COALESCE(u.unit_no,'') || COALESCE(p.room_no,'')";
                hiddenSql += " AND ( (" + target + ") LIKE @kwLike OR " + NormalizedSql(target) + " LIKE @normLike )";
            }
            result.HiddenCount = connection.ExecuteScalar<int>(hiddenSql, parameters);
            return result;
        }

        /// <summary>CHG-v1.1.0-10：草稿批次既有缴费对象（批次编辑回填）。</summary>
        public BillObjectSelectionDto GetBatchBillObjects(IDbConnection connection, int batchId)
        {
            var rows = connection.Query<BatchBillObjectRow>(
                "SELECT property_id AS PropertyId, parking_id AS ParkingId, owner_id AS OwnerId FROM t_bill " +
                "WHERE generate_batch_id = @batchId AND del_flag = 0",
                new { batchId }).ToList();
            var selection = new BillObjectSelectionDto
            {
                PropertyIds = new List<int>(),
                ParkingIds = new List<int>(),
                OwnerIds = new List<int>()
            };
            foreach (BatchBillObjectRow row in rows)
            {
                if (row.PropertyId.HasValue) { selection.PropertyIds.Add(row.PropertyId.Value); }
                if (row.ParkingId.HasValue) { selection.ParkingIds.Add(row.ParkingId.Value); }
                if (row.OwnerId.HasValue) { selection.OwnerIds.Add(row.OwnerId.Value); }
            }
            return selection;
        }

        private class BatchBillObjectRow
        {
            public int? PropertyId { get; set; }
            public int? ParkingId { get; set; }
            public int? OwnerId { get; set; }
        }

        /// <summary>关键字归一化：去掉楼栋/单元量词，使「1栋」可命中「1号楼」。</summary>
        private static string NormalizeObjectKey(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) { return string.Empty; }
            return keyword
                .Replace("号楼", string.Empty)
                .Replace("栋", string.Empty)
                .Replace("幢", string.Empty)
                .Replace("座", string.Empty)
                .Replace("单元", string.Empty)
                .Replace("号", string.Empty)
                .Replace(" ", string.Empty)
                .Replace("#", string.Empty);
        }

        /// <summary>与 <see cref="NormalizeObjectKey"/> 同口径的 SQL 表达式。</summary>
        private static string NormalizedSql(string target)
        {
            return "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(" + target + ",'号楼',''),'栋',''),'幢',''),'座',''),'单元',''),'#','')";
        }

        /// <summary>BR-INF-02（M7 BUG-002）：缴费对象有效业主关系判定（口径同 GetOwnerIdByBill）。</summary>
        public bool HasValidOwnerRelation(IDbConnection connection, int? propertyId, int? parkingId, int? ownerId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COALESCE(" +
                "  (SELECT rel.owner_id FROM t_owner_property_rel rel " +
                "    WHERE rel.property_id = @propertyId AND rel.del_flag = 0 AND rel.rel_status <> 2 " +
                "    ORDER BY rel.id DESC LIMIT 1), " +
                "  (SELECT ps.owner_id FROM t_parking_space ps WHERE ps.id = @parkingId AND ps.del_flag = 0), " +
                "  (SELECT o.id FROM t_owner o WHERE o.id = @ownerId AND o.del_flag = 0), 0)",
                new { propertyId, parkingId, ownerId }) > 0;
        }
        public int InsertBill(IDbConnection connection, IDbTransaction transaction, BillDto bill)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_bill (charge_item_id, property_id, parking_id, owner_id, payer_name, cycle_id, amount, paid_amount, " +
                "status, due_at, generate_batch_id, del_flag) " +
                "VALUES (@ChargeItemId, @PropertyId, @ParkingId, @OwnerId, @PayerName, @CycleId, @Amount, 0, @Status, @DueAt, @GenerateBatchId, @DelFlag); " +
                "SELECT last_insert_rowid();",
                new { bill.ChargeItemId, bill.PropertyId, bill.ParkingId, bill.OwnerId, bill.PayerName, bill.CycleId, bill.Amount, bill.Status, bill.DueAt, bill.GenerateBatchId, bill.DelFlag },
                transaction);
        }

        public void UpdateBillPaidAmount(IDbConnection connection, IDbTransaction transaction, BillDto bill)
        {
            connection.Execute(
                "UPDATE t_bill SET paid_amount = @PaidAmount, status = @Status, " +
                "updated_at = datetime('now','localtime') WHERE id = @Id",
                new { bill.Id, bill.PaidAmount, bill.Status }, transaction);
        }

        /// <summary>
        /// 逾期判定（CHG-v1.1.0-17 修订）：只有**超过到期日一整天**才置为逾期。
        /// 原口径 due_at &lt; now（含时分秒）导致「账单期间＝当天」的账单在当天即被标逾期；
        /// 现口径按时间差 &gt; 1 天判定（到期当天与次日不逾期），并把此前被误标逾期的账单回正。
        /// </summary>
        public void MarkOverdue(IDbConnection connection, IDbTransaction transaction, DateTime now)
        {
            var overdue = connection.Query<int>(
                "SELECT id FROM t_bill WHERE del_flag = 0 AND status IN (0,1) " +
                "AND amount > paid_amount AND julianday(@now) - julianday(due_at) > 1",
                new { now }, transaction).ToList();

            foreach (int id in overdue)
            {
                connection.Execute(
                    "UPDATE t_bill SET status = 2, updated_at = datetime('now','localtime') WHERE id = @id",
                    new { id }, transaction);
                connection.Execute(
                    "INSERT INTO t_bill_status_log (bill_id, old_status, new_status, changed_at, reason) " +
                    "VALUES (@billId, 0, 2, datetime('now','localtime'), '系统自动：超过到期日未缴')",
                    new { billId = id }, transaction);
            }

            // 口径回正：曾被误标「逾期」但到期日未超过一整天的账单，退回未缴/部分缴（保留状态流水）
            var corrected = connection.Query<int>(
                "SELECT id FROM t_bill WHERE del_flag = 0 AND status = 2 AND amount > paid_amount " +
                "AND julianday(@now) - julianday(due_at) <= 1",
                new { now }, transaction).ToList();

            foreach (int id in corrected)
            {
                int newStatus = connection.ExecuteScalar<int>(
                    "SELECT CASE WHEN paid_amount > 0 THEN 1 ELSE 0 END FROM t_bill WHERE id = @id",
                    new { id }, transaction);
                connection.Execute(
                    "UPDATE t_bill SET status = @newStatus, updated_at = datetime('now','localtime') WHERE id = @id",
                    new { id, newStatus }, transaction);
                connection.Execute(
                    "INSERT INTO t_bill_status_log (bill_id, old_status, new_status, changed_at, reason) " +
                    "VALUES (@billId, 2, @newStatus, datetime('now','localtime'), '系统自动：逾期口径回正（到期未超过一天）')",
                    new { billId = id, newStatus }, transaction);
            }
        }

        public void InsertBillStatusLog(IDbConnection connection, IDbTransaction transaction, BillStatusLogDto log)
        {
            connection.Execute(
                "INSERT INTO t_bill_status_log (bill_id, old_status, new_status, changed_at, reason) " +
                "VALUES (@BillId, @OldStatus, @NewStatus, datetime('now','localtime'), @Reason)",
                new { log.BillId, log.OldStatus, log.NewStatus, log.Reason }, transaction);
        }


        public BillDto GetBill(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<BillDto>(
                "SELECT id, charge_item_id AS ChargeItemId, property_id AS PropertyId, parking_id AS ParkingId, " +
                "owner_id AS OwnerId, payer_name AS PayerName, cycle_id AS CycleId, amount, paid_amount AS PaidAmount, status, due_at AS DueAt, " +
                "generate_batch_id AS GenerateBatchId, del_flag AS DelFlag " +
                "FROM t_bill WHERE id = @id AND del_flag = 0", new { id });
        }

        public List<BillListItemDto> ListDraftBillsByBatch(IDbConnection connection, int batchId)
        {
            return connection.Query<BillListItemDto>(BillListSql + " AND b.generate_batch_id = @batchId AND b.status = 5",
                new { batchId }).ToList();
        }

        public List<BillListItemDto> ListPublishedBillsByBatch(IDbConnection connection, int batchId)
        {
            return connection.Query<BillListItemDto>(BillListSql + " AND b.generate_batch_id = @batchId AND b.status <> 5",
                new { batchId }).ToList();
        }

        public void PublishBatchBills(IDbConnection connection, IDbTransaction transaction, int batchId)
        {
            var drafts = connection.Query<int>(
                "SELECT id FROM t_bill WHERE del_flag = 0 AND generate_batch_id = @batchId AND status = 5",
                new { batchId }, transaction).ToList();

            foreach (int id in drafts)
            {
                connection.Execute(
                    "UPDATE t_bill SET status = 0, updated_at = datetime('now','localtime') WHERE id = @id",
                    new { id }, transaction);
                connection.Execute(
                    "INSERT INTO t_bill_status_log (bill_id, old_status, new_status, changed_at, reason) " +
                    "VALUES (@billId, 5, 0, datetime('now','localtime'), '账单发布')",
                    new { billId = id }, transaction);
            }
        }

        public int InsertBillGenerateLog(IDbConnection connection, IDbTransaction transaction, BillGenerateLogDto log)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_bill_generate_log (generate_at, total, success, fail, fail_detail, scope_summary) " +
                "VALUES (datetime('now','localtime'), @Total, @Success, @Fail, @FailDetail, @ScopeSummary); SELECT last_insert_rowid();",
                new { log.Total, log.Success, log.Fail, log.FailDetail, log.ScopeSummary }, transaction);
        }

        public BillGenerateLogDto GetBillGenerateLog(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<BillGenerateLogDto>(
                "SELECT id, generate_at AS GenerateAt, total, success, fail, fail_detail AS FailDetail, " +
                "       scope_summary AS ScopeSummary, retried_at AS RetriedAt, retried_count AS RetriedCount " +
                "FROM t_bill_generate_log WHERE id = @id", new { id });
        }

        /// <summary>
        /// CHG-v1.1.0-21：失败对象重推闭环 —— 收敛源批次失败清单并记录重推时间/成功户数。
        /// 重推后 fail＝仍失败对象数；全部成功时 fail=0 且 retried_at 非空 → 批次状态转为「已重推」。
        /// </summary>
        public void MarkBatchRetried(IDbConnection connection, IDbTransaction transaction,
            int id, int remainingFail, string remainingFailDetail, int retriedCount)
        {
            connection.Execute(
                "UPDATE t_bill_generate_log SET fail = @remainingFail, fail_detail = @remainingFailDetail, " +
                "       retried_at = datetime('now','localtime'), retried_count = @retriedCount " +
                "WHERE id = @id",
                new { id, remainingFail, remainingFailDetail, retriedCount }, transaction);
        }

        public void UpdateGenerateLogResult(IDbConnection connection, IDbTransaction transaction,
            int id, int success, int fail, string failDetail)
        {
            connection.Execute(
                "UPDATE t_bill_generate_log SET success = @success, fail = @fail, fail_detail = @failDetail WHERE id = @id",
                new { id, success, fail, failDetail }, transaction);
        }

        /// <summary>
        /// 账单列表 SELECT 段（CHG-v1.1.0-13：拆出 FROM 段，供 COUNT 复用 ——
        /// 缴费人过滤条件引用了 ps/rel，COUNT 若只用 t_bill 会报「no such column」）。
        /// </summary>
        private const string BillListSelectSql =
            "SELECT b.id, b.charge_item_id AS ChargeItemId, ci.name AS ChargeItemName, " +
            "b.property_id AS PropertyId, b.parking_id AS ParkingId, b.owner_id AS OwnerId, " +
            "CASE WHEN COALESCE(b.payer_name, '') <> '' THEN b.payer_name " +
            "     WHEN b.owner_id IS NOT NULL THEN '业主：' || COALESCE(bo.name, '') " +
            "     ELSE COALESCE(p.room_no, ps.space_no, '') END AS PropertyNo, " +
            // CHG-v1.1.0-17：业主直缴账单（办卡费/清理费/维修费等）无房产行，楼栋/房号回落到业主名下主房产，
            // 保证收款登记下拉「姓名 · 楼栋 · 房号 · 欠费 N 笔」四段口径在任何缴费对象下都完整。
            "COALESCE(bd.building_no, " +
            "  (SELECT bd2.building_no FROM t_owner_property_rel r2 " +
            "   JOIN t_property p2 ON p2.id = r2.property_id AND p2.del_flag = 0 " +
            "   JOIN t_building bd2 ON bd2.id = p2.building_id AND bd2.del_flag = 0 " +
            "   WHERE b.owner_id IS NOT NULL AND r2.owner_id = b.owner_id AND r2.del_flag = 0 AND r2.rel_status <> 2 " +
            "   ORDER BY r2.id DESC LIMIT 1), '') AS BuildingNo, " +
            "COALESCE(p.room_no, " +
            "  (SELECT p2.room_no FROM t_owner_property_rel r2 " +
            "   JOIN t_property p2 ON p2.id = r2.property_id AND p2.del_flag = 0 " +
            "   WHERE b.owner_id IS NOT NULL AND r2.owner_id = b.owner_id AND r2.del_flag = 0 AND r2.rel_status <> 2 " +
            "   ORDER BY r2.id DESC LIMIT 1), '') AS RoomNo, COALESCE(ps.space_no, '') AS SpaceNo, " +
            // CHG-v1.1.0-18：缴费人取已 JOIN 的业主主键（而非 COALESCE 表达式）——
            // 表达式列在 System.Data.SQLite 中按「首行值类型」推断类型，若首行为 NULL（如自定义缴费对象账单）
            // 会把该列判为字符串，第二行出现 INTEGER 时 Dapper 反序列化抛 InvalidCastException
            // （表现为「服务器内部错误」）。取 o.id 有明确 INTEGER 声明类型，结果集混合 NULL/整数不再出错。
            "o.id AS PayerOwnerId, " +
            // CHG-v1.1.0-18：自定义缴费对象账单的缴费人名称（收款登记/欠费台账按名称展示与聚合）
            // 注意：本 SELECT 列顺序须与 BillListItemDto 属性声明顺序一致（Dapper 按序号映射）
            "COALESCE(b.payer_name, '') AS PayerName, " +
            "COALESCE(o.name, '') AS OwnerName, b.cycle_id AS CycleId, " +
            // CHG-v1.1.0-16：周期日期截断到日（原样含 00:00:00 时分秒，导致各处「账单期间」过长/被裁切）
            "COALESCE(substr(cy.start_date, 1, 10), '') || ' ~ ' || COALESCE(substr(cy.end_date, 1, 10), '') AS CyclePeriod, " +
            "b.amount, b.paid_amount AS PaidAmount, b.status, b.due_at AS DueAt, " +
            "b.generate_batch_id AS GenerateBatchId, b.del_flag AS DelFlag ";

        private const string BillListFromSql =
            "FROM t_bill b " +
            "JOIN t_charge_item ci ON ci.id = b.charge_item_id " +
            "LEFT JOIN t_billing_cycle cy ON cy.id = b.cycle_id " +
            "LEFT JOIN t_property p ON p.id = b.property_id " +
            "LEFT JOIN t_building bd ON bd.id = p.building_id " +
            "LEFT JOIN t_parking_space ps ON ps.id = b.parking_id " +
            "LEFT JOIN t_owner bo ON bo.id = b.owner_id " +
            "LEFT JOIN t_owner o ON o.id = COALESCE(" +
            "  b.owner_id, " +
            "  (SELECT owner_id FROM t_owner_property_rel rel WHERE rel.property_id = b.property_id AND rel.del_flag = 0 " +
            "   AND rel.rel_status <> 2 ORDER BY rel.id DESC LIMIT 1), " +
            "  ps.owner_id) " +
            "WHERE b.del_flag = 0";

        private const string BillListSql = BillListSelectSql + BillListFromSql;

        public PageResult<BillListItemDto> QueryBills(IDbConnection connection, BillQueryRequest query)
        {
            string where = " AND b.del_flag = 0";
            var parameters = new DynamicParameters();

            if (query.Status.HasValue)
            {
                where += " AND b.status = @status";
                parameters.Add("status", (int)query.Status.Value);
            }
            if (query.PropertyId.HasValue)
            {
                where += " AND (b.property_id = @propertyId OR b.parking_id = @propertyId)";
                parameters.Add("propertyId", query.PropertyId.Value);
            }
            // CHG-v1.1.0-11：业主直缴账单按业主取（收款登记「缴费对象」维度）
            if (query.OwnerId.HasValue)
            {
                where += " AND b.owner_id = @ownerId";
                parameters.Add("ownerId", query.OwnerId.Value);
            }
            // CHG-v1.1.0-13：按缴费人（业主）取全部欠费 —— 覆盖其名下房产 + 车位 + 业主直缴
            if (query.PayerOwnerId.HasValue)
            {
                where += " AND COALESCE(b.owner_id, " +
                    "  (SELECT rel.owner_id FROM t_owner_property_rel rel WHERE rel.property_id = b.property_id " +
                    "   AND rel.del_flag = 0 AND rel.rel_status <> 2 ORDER BY rel.id DESC LIMIT 1), ps.owner_id) = @payerOwnerId";
                parameters.Add("payerOwnerId", query.PayerOwnerId.Value);
            }
            // CHG-v1.1.0-18：按自定义缴费对象名称取全部欠费（收款登记「一个自定义缴费对象一行」）
            if (!string.IsNullOrWhiteSpace(query.PayerName))
            {
                where += " AND b.payer_name = @payerName";
                parameters.Add("payerName", query.PayerName.Trim());
            }
            if (query.ChargeItemId.HasValue)
            {
                where += " AND b.charge_item_id = @chargeItemId";
                parameters.Add("chargeItemId", query.ChargeItemId.Value);
            }
            if (query.DueFrom.HasValue)
            {
                where += " AND b.due_at >= @dueFrom";
                parameters.Add("dueFrom", query.DueFrom.Value);
            }
            if (query.DueTo.HasValue)
            {
                where += " AND b.due_at <= @dueTo";
                parameters.Add("dueTo", query.DueTo.Value);
            }
            if (query.ArrearsOnly)
            {
                where += " AND b.amount > b.paid_amount AND b.status IN (0,1,2)";
            }

            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;

            int total = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) " + BillListFromSql + where.Substring(" AND b.del_flag = 0".Length),
                parameters);

            string sql = BillListSql + where + " ORDER BY b.due_at DESC, b.id DESC LIMIT @limit OFFSET @offset";
            parameters.Add("limit", pageSize);
            parameters.Add("offset", offset);

            var items = connection.Query<BillListItemDto>(sql, parameters).ToList();
            return new PageResult<BillListItemDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }
        public List<BillBatchDto> QueryGenerateLogs(IDbConnection connection)
        {
            string sql = "SELECT gl.id, 'BILL-' || gl.id AS BatchNo, " +
                "COALESCE((SELECT ci.name FROM t_bill b1 JOIN t_charge_item ci ON ci.id = b1.charge_item_id " +
                "  WHERE b1.generate_batch_id = gl.id AND b1.del_flag = 0 LIMIT 1), '') AS ChargeItemName, " +
                "(SELECT b1.charge_item_id FROM t_bill b1 WHERE b1.generate_batch_id = gl.id AND b1.del_flag = 0 LIMIT 1) AS ChargeItemId, " +
                "(SELECT b1.cycle_id FROM t_bill b1 WHERE b1.generate_batch_id = gl.id AND b1.del_flag = 0 LIMIT 1) AS CycleId, " +
                // CHG-v1.1.0-16：同账单列表口径，周期显示截断到日
                "COALESCE((SELECT COALESCE(substr(cy.start_date, 1, 10), '') || ' ~ ' || COALESCE(substr(cy.end_date, 1, 10), '') " +
                "  FROM t_bill b2 JOIN t_billing_cycle cy ON cy.id = b2.cycle_id " +
                "  WHERE b2.generate_batch_id = gl.id AND b2.del_flag = 0 LIMIT 1), '') AS CyclePeriod, " +
                "(SELECT COUNT(1) FROM t_bill b3 WHERE b3.generate_batch_id = gl.id AND b3.del_flag = 0) AS HouseCount, " +
                "(SELECT CAST(COALESCE(SUM(b4.amount), 0) AS REAL) FROM t_bill b4 WHERE b4.generate_batch_id = gl.id AND b4.del_flag = 0) AS TotalAmount, " +
                "gl.success AS SuccessCount, gl.fail AS FailCount, " +
                "(SELECT COUNT(1) FROM t_bill b5 WHERE b5.generate_batch_id = gl.id AND b5.del_flag = 0 AND b5.status = 5) AS DraftCount, " +
                "(SELECT COUNT(1) FROM t_bill b6 WHERE b6.generate_batch_id = gl.id AND b6.del_flag = 0 AND b6.status IN (0,2)) AS PendingCount, " +
                "(SELECT COUNT(1) FROM t_bill b7 WHERE b7.generate_batch_id = gl.id AND b7.del_flag = 0 AND b7.status = 1) AS PartialCount, " +
                "(SELECT COUNT(1) FROM t_bill b8 WHERE b8.generate_batch_id = gl.id AND b8.del_flag = 0 AND b8.status = 3) AS PaidCount, " +
                "gl.generate_at AS GenerateAt, gl.scope_summary AS ScopeSummary, " +
                "(SELECT MAX(b9.updated_at) FROM t_bill b9 WHERE b9.generate_batch_id = gl.id AND b9.del_flag = 0 AND b9.status <> 5) AS PublishedAtRaw, " +
                // CHG-v1.1.0-21：重推闭环标记（批次状态据此由「发布失败」转为「已重推」）
                "gl.retried_at AS RetriedAtRaw, gl.retried_count AS RetriedCount " +
                "FROM t_bill_generate_log gl WHERE gl.del_flag = 0 ORDER BY gl.id DESC";
            return connection.Query<BillBatchDto>(sql).ToList();
        }


        public void SoftDeleteBillBatch(IDbConnection connection, IDbTransaction transaction, int batchId)
        {
            // 状态日志留痕（审计轨迹：批次内账单被批次删除）
            connection.Execute(
                "INSERT INTO t_bill_status_log (bill_id, old_status, new_status, changed_at, reason) " +
                "SELECT id, status, status, datetime('now','localtime'), '批次删除（CHG-M4-16）' FROM t_bill " +
                "WHERE del_flag = 0 AND generate_batch_id = @batchId",
                new { batchId }, transaction);

            connection.Execute(
                "UPDATE t_bill SET del_flag = 1, updated_at = datetime('now','localtime') " +
                "WHERE del_flag = 0 AND generate_batch_id = @batchId",
                new { batchId }, transaction);

            connection.Execute(
                "UPDATE t_bill_generate_log SET del_flag = 1 WHERE id = @batchId AND del_flag = 0",
                new { batchId }, transaction);
        }
        public List<BillListItemDto> ListPendingBillsByOwner(IDbConnection connection, int ownerId)
        {
            return connection.Query<BillListItemDto>(
                BillListSql + " AND b.status IN (0,1,2) " +
                "AND (b.owner_id = @ownerId " +
                "  OR EXISTS (SELECT 1 FROM t_owner_property_rel rel2 WHERE rel2.property_id = b.property_id " +
                "  AND rel2.owner_id = @ownerId AND rel2.del_flag = 0) OR ps.owner_id = @ownerId) " +
                "ORDER BY b.due_at, b.id",
                new { ownerId }).ToList();
        }

        public PaymentStatisticsDto GetPaymentStatistics(IDbConnection connection)
        {
            var dto = new PaymentStatisticsDto();
            using (var multi = connection.QueryMultiple(
                "SELECT COUNT(1) FROM t_bill WHERE del_flag = 0 AND status <> 5; " +
                "SELECT COALESCE(SUM(amount), 0) FROM t_bill WHERE del_flag = 0 AND status <> 5; " +
                "SELECT COUNT(1) FROM t_bill WHERE del_flag = 0 AND status = 3; " +
                "SELECT COALESCE(SUM(paid_amount), 0) FROM t_bill WHERE del_flag = 0 AND status = 3; " +
                "SELECT COUNT(1) FROM t_bill WHERE del_flag = 0 AND status IN (0,1,2); " +
                "SELECT COALESCE(SUM(amount - paid_amount), 0) FROM t_bill WHERE del_flag = 0 AND status IN (0,1,2);"))
            {
                dto.TotalBills = multi.Read<int>().Single();
                dto.TotalAmount = multi.Read<decimal>().Single();
                dto.PaidCount = multi.Read<int>().Single();
                dto.PaidAmount = multi.Read<decimal>().Single();
                dto.UnpaidCount = multi.Read<int>().Single();
                dto.UnpaidAmount = multi.Read<decimal>().Single();
            }
            return dto;
        }

        // ---------- 欠费台账 ----------
        public PageResult<ArrearDto> QueryArrears(IDbConnection connection, BillQueryRequest query)
        {
            string where = "WHERE b.del_flag = 0 AND b.amount > b.paid_amount AND b.status IN (0,1,2)";
            var parameters = new DynamicParameters();

            if (query.PropertyId.HasValue)
            {
                where += " AND (b.property_id = @propertyId OR b.parking_id = @propertyId)";
                parameters.Add("propertyId", query.PropertyId.Value);
            }
            if (query.ChargeItemId.HasValue)
            {
                where += " AND b.charge_item_id = @chargeItemId";
                parameters.Add("chargeItemId", query.ChargeItemId.Value);
            }
            if (query.DueFrom.HasValue)
            {
                where += " AND b.due_at >= @dueFrom";
                parameters.Add("dueFrom", query.DueFrom.Value);
            }
            if (query.DueTo.HasValue)
            {
                where += " AND b.due_at <= @dueTo";
                parameters.Add("dueTo", query.DueTo.Value);
            }

            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;

            string from = "FROM t_bill b " +
                "JOIN t_charge_item ci ON ci.id = b.charge_item_id " +
                "LEFT JOIN t_property p ON p.id = b.property_id " +
                "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                "LEFT JOIN t_building bld ON bld.id = u.building_id " +
                "LEFT JOIN t_parking_space ps ON ps.id = b.parking_id " +
                "LEFT JOIN t_owner o ON o.id = COALESCE(" +
                "  (SELECT owner_id FROM t_owner_property_rel rel WHERE rel.property_id = b.property_id AND rel.del_flag = 0 ORDER BY rel.id DESC LIMIT 1), " +
                "  ps.owner_id)";

            int total = connection.ExecuteScalar<int>("SELECT COUNT(1) " + from + " " + where, parameters);

            string sql = "SELECT b.id AS BillId, COALESCE(o.name, '') AS OwnerName, " +
                "COALESCE(p.room_no, ps.space_no, '') AS PropertyNo, ci.name AS ChargeItemName, " +
                "CAST(b.amount AS REAL) AS Amount, CAST(b.paid_amount AS REAL) AS PaidAmount, " +
                "CAST((b.amount - b.paid_amount) AS REAL) AS ArrearAmount, b.due_at AS DueAt, " +
                "CAST(julianday('now','localtime') - julianday(b.due_at) AS INTEGER) AS AgingDays, " +
                "COALESCE((SELECT r.channel FROM t_arrear_remind_log r WHERE r.bill_id = b.id ORDER BY r.id DESC LIMIT 1), '') AS RemindChannel, " +
                "COALESCE(bld.building_no, '') AS BuildingNo " +
                from + " " + where + " ORDER BY b.due_at, b.id LIMIT @limit OFFSET @offset";
            parameters.Add("limit", pageSize);
            parameters.Add("offset", offset);

            var items = connection.Query<ArrearDto>(sql, parameters).ToList();
            return new PageResult<ArrearDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }

        public void InsertArrearRemind(IDbConnection connection, IDbTransaction transaction,
            int billId, string channel, string note, int? userId)
        {
            connection.Execute(
                "INSERT INTO t_arrear_remind_log (bill_id, channel, note, created_by) " +
                "VALUES (@billId, @channel, @note, @userId)",
                new { billId, channel, note, userId }, transaction);
        }

        public void SoftDeleteBill(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_bill SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                new { id }, transaction);
        }

        // ---------- 收款/收据 ----------
        public int InsertPayment(IDbConnection connection, IDbTransaction transaction, PaymentDto payment)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_payment (bill_id, amount, pay_method, paid_at, status, to_pre_deposit, remark, batch_no) " +
                "VALUES (@BillId, @Amount, @PayMethod, @PaidAt, 0, @ToPreDeposit, @Remark, @BatchNo); SELECT last_insert_rowid();",
                new { payment.BillId, payment.Amount, payment.PayMethod, payment.PaidAt, payment.ToPreDeposit, payment.Remark, payment.BatchNo },
                transaction);
        }

        public PaymentDto GetPayment(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<PaymentDto>(
                "SELECT id, bill_id AS BillId, amount, pay_method AS PayMethod, paid_at AS PaidAt, " +
                "status, to_pre_deposit AS ToPreDeposit, remark, batch_no AS BatchNo FROM t_payment WHERE id = @id", new { id });
        }

        public List<PaymentDto> ListPayments(IDbConnection connection, PageRequest query, out int total)
        {
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;
            total = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_payment");
            return connection.Query<PaymentDto>(
                "SELECT id, bill_id AS BillId, amount, pay_method AS PayMethod, paid_at AS PaidAt, " +
                "status, to_pre_deposit AS ToPreDeposit, remark, batch_no AS BatchNo FROM t_payment ORDER BY paid_at DESC, id DESC LIMIT @limit OFFSET @offset",
                new { limit = pageSize, offset }).ToList();
        }

        public int InsertReceipt(IDbConnection connection, IDbTransaction transaction, ReceiptDto receipt)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_receipt (payment_id, receipt_no, print_count, printed_at) " +
                "VALUES (@PaymentId, @ReceiptNo, 0, NULL); SELECT last_insert_rowid();",
                new { receipt.PaymentId, receipt.ReceiptNo }, transaction);
        }

        public ReceiptDto GetReceiptByPayment(IDbConnection connection, int paymentId)
        {
            return connection.QueryFirstOrDefault<ReceiptDto>(
                "SELECT id, payment_id AS PaymentId, receipt_no AS ReceiptNo, print_count AS PrintCount, " +
                "printed_at AS PrintedAt FROM t_receipt WHERE payment_id = @paymentId", new { paymentId });
        }

        public ReceiptDto GetReceipt(IDbConnection connection, int receiptId)
        {
            return connection.QueryFirstOrDefault<ReceiptDto>(
                "SELECT id, payment_id AS PaymentId, receipt_no AS ReceiptNo, print_count AS PrintCount, " +
                "printed_at AS PrintedAt FROM t_receipt WHERE id = @receiptId", new { receiptId });
        }

        public void MarkReceiptPrinted(IDbConnection connection, IDbTransaction transaction, ReceiptDto receipt)
        {
            connection.Execute(
                "UPDATE t_receipt SET print_count = @PrintCount, printed_at = datetime('now','localtime') WHERE id = @Id",
                new { receipt.Id, receipt.PrintCount }, transaction);
        }

        public void InsertPrintLog(IDbConnection connection, IDbTransaction transaction, string bizType, int bizId)
        {
            connection.Execute(
                "INSERT INTO t_print_log (biz_type, biz_id, print_count) VALUES (@bizType, @bizId, 1)",
                new { bizType, bizId }, transaction);
        }

        public PreDepositDto GetPreDeposit(IDbConnection connection, int ownerId)
        {
            return connection.QueryFirstOrDefault<PreDepositDto>(
                "SELECT id, owner_id AS OwnerId, balance, updated_at AS UpdatedAt " +
                "FROM t_pre_deposit WHERE owner_id = @ownerId", new { ownerId });
        }

        public void IncreasePreDeposit(IDbConnection connection, IDbTransaction transaction, int ownerId, decimal delta)
        {
            connection.Execute(
                "INSERT INTO t_pre_deposit (owner_id, balance, updated_at) VALUES (@ownerId, @delta, datetime('now','localtime')) " +
                "ON CONFLICT(owner_id) DO UPDATE SET balance = balance + @delta, updated_at = datetime('now','localtime')",
                new { ownerId, delta }, transaction);
        }

        public void DecreasePreDeposit(IDbConnection connection, IDbTransaction transaction, int ownerId, decimal delta)
        {
            connection.Execute(
                "UPDATE t_pre_deposit SET balance = balance - @delta, updated_at = datetime('now','localtime') " +
                "WHERE owner_id = @ownerId", new { ownerId, delta }, transaction);
        }

        public decimal GetOwnerPreDeposit(IDbConnection connection, int ownerId)
        {
            return connection.ExecuteScalar<decimal?>(
                "SELECT balance FROM t_pre_deposit WHERE owner_id = @ownerId", new { ownerId }) ?? 0m;
        }

        public int GetOwnerIdByBill(IDbConnection connection, int billId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COALESCE(" +
                "  b.owner_id, " +
                "  (SELECT rel.owner_id FROM t_owner_property_rel rel WHERE rel.property_id = b.property_id AND rel.del_flag = 0 ORDER BY rel.id DESC LIMIT 1), " +
                "  (SELECT ps.owner_id FROM t_parking_space ps WHERE ps.id = b.parking_id), 0) " +
                "FROM t_bill b WHERE b.id = @billId", new { billId });
        }

        // ---------- 退款/减免/调整 ----------
        public int InsertRefund(IDbConnection connection, IDbTransaction transaction, RefundAdjustmentDto refund)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_payment_refund (bill_id, refund_type, amount, reason, ref_no, attachment_name, attachment_path) " +
                "VALUES (@BillId, @RefundType, @Amount, @Reason, @RefNo, @AttachmentName, @AttachmentPath); SELECT last_insert_rowid();",
                new { refund.BillId, refund.RefundType, refund.Amount, refund.Reason, refund.RefNo, refund.AttachmentName, refund.AttachmentPath }, transaction);
        }

        public List<RefundAdjustmentDto> ListRefunds(IDbConnection connection, PageRequest query, out int total)
        {
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;
            total = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_payment_refund");
            return connection.Query<RefundAdjustmentDto>(
                "SELECT pr.id, pr.bill_id AS BillId, pr.refund_type AS RefundType, pr.amount, pr.reason, " +
                "pr.ref_no AS RefNo, pr.created_at AS CreatedAt, pr.attachment_name AS AttachmentName, " +
                "pr.attachment_path AS AttachmentPath, " +
                "COALESCE(p.room_no, ps.space_no, '') AS PropertyNo " +
                "FROM t_payment_refund pr " +
                "LEFT JOIN t_bill b ON b.id = pr.bill_id " +
                "LEFT JOIN t_property p ON p.id = b.property_id " +
                "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                "LEFT JOIN t_building bld ON bld.id = u.building_id " +
                "LEFT JOIN t_parking_space ps ON ps.id = b.parking_id " +
                "ORDER BY pr.id DESC LIMIT @limit OFFSET @offset",
                new { limit = pageSize, offset }).ToList();
        }

        public decimal GetBillPaidAmount(IDbConnection connection, int billId)
        {
            return connection.ExecuteScalar<decimal?>(
                "SELECT paid_amount FROM t_bill WHERE id = @billId", new { billId }) ?? 0m;
        }

        // ---------- 支出 ----------
        public List<ExpenseCategoryDto> ListExpenseCategories(IDbConnection connection)
        {
            return connection.Query<ExpenseCategoryDto>(
                "SELECT id, name, category_type AS CategoryType, status, del_flag AS DelFlag " +
                "FROM t_expense_category WHERE del_flag = 0 ORDER BY id").ToList();
        }

        public ExpenseCategoryDto GetExpenseCategory(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<ExpenseCategoryDto>(
                "SELECT id, name, category_type AS CategoryType, status, del_flag AS DelFlag " +
                "FROM t_expense_category WHERE id = @id AND del_flag = 0", new { id });
        }

        public int InsertExpenseCategory(IDbConnection connection, IDbTransaction transaction, ExpenseCategoryDto category)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_expense_category (name, category_type, status, del_flag) " +
                "VALUES (@Name, @CategoryType, @Status, 0); SELECT last_insert_rowid();",
                new { category.Name, category.CategoryType, category.Status }, transaction);
        }

        public void UpdateExpenseCategory(IDbConnection connection, IDbTransaction transaction, ExpenseCategoryDto category)
        {
            connection.Execute(
                "UPDATE t_expense_category SET name = @Name, category_type = @CategoryType, status = @Status, " +
                "updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { category.Id, category.Name, category.CategoryType, category.Status }, transaction);
        }

        public void SoftDeleteExpenseCategory(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_expense_category SET del_flag = 1, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        public bool ExpenseCategoryReferenced(IDbConnection connection, int categoryId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_expense WHERE category_id = @categoryId AND del_flag = 0",
                new { categoryId }) > 0;
        }

        public int InsertExpense(IDbConnection connection, IDbTransaction transaction, ExpenseDto expense)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_expense (category_id, amount, expense_date, note, payee, status, del_flag) " +
                "VALUES (@CategoryId, @Amount, @ExpenseDate, @Note, @Payee, @Status, 0); SELECT last_insert_rowid();",
                new { expense.CategoryId, expense.Amount, expense.ExpenseDate, expense.Note, expense.Payee, expense.Status }, transaction);
        }

        public void UpdateExpense(IDbConnection connection, IDbTransaction transaction, ExpenseDto expense)
        {
            connection.Execute(
                "UPDATE t_expense SET category_id = @CategoryId, amount = @Amount, expense_date = @ExpenseDate, " +
                "note = @Note, payee = @Payee, status = @Status, updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { expense.Id, expense.CategoryId, expense.Amount, expense.ExpenseDate, expense.Note, expense.Payee, expense.Status }, transaction);
        }

        public void SoftDeleteExpense(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_expense SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                new { id }, transaction);
        }

        /// <summary>支出记录批量删除（v1.1.0-⑤）：软删留痕（BR-FIN-10），只作用于未删除行。</summary>
        public int SoftDeleteExpenses(IDbConnection connection, IDbTransaction transaction, IEnumerable<int> ids)
        {
            var list = (ids ?? Enumerable.Empty<int>()).Distinct().Where(x => x > 0).ToList();
            if (list.Count == 0) { return 0; }
            return connection.Execute(
                "UPDATE t_expense SET del_flag = 1, updated_at = datetime('now','localtime') " +
                "WHERE id IN @ids AND del_flag = 0",
                new { ids = list }, transaction);
        }

        public ExpenseDto GetExpense(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<ExpenseDto>(
                "SELECT e.id, e.category_id AS CategoryId, c.name AS CategoryName, e.amount, " +
                "e.expense_date AS ExpenseDate, e.note, e.payee AS Payee, e.status, e.del_flag AS DelFlag " +
                "FROM t_expense e JOIN t_expense_category c ON c.id = e.category_id " +
                "WHERE e.id = @id AND e.del_flag = 0", new { id });
        }

        public List<ExpenseDto> ListExpenses(IDbConnection connection, PageRequest query, out int total)
        {
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;
            total = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_expense WHERE del_flag = 0");
            return connection.Query<ExpenseDto>(
                "SELECT e.id, e.category_id AS CategoryId, c.name AS CategoryName, e.amount, " +
                "e.expense_date AS ExpenseDate, e.note, e.payee AS Payee, e.status, e.del_flag AS DelFlag " +
                "FROM t_expense e JOIN t_expense_category c ON c.id = e.category_id " +
                "WHERE e.del_flag = 0 ORDER BY e.expense_date DESC, e.id DESC LIMIT @limit OFFSET @offset",
                new { limit = pageSize, offset }).ToList();
        }

        public void InsertExpenseObjectRels(IDbConnection connection, IDbTransaction transaction,
            int expenseId, IEnumerable<ExpenseObjectRelDto> rels)
        {
            foreach (var rel in rels)
            {
                connection.Execute(
                    "INSERT INTO t_expense_object_rel (expense_id, object_type, object_id) " +
                    "VALUES (@expenseId, @ObjectType, @ObjectId)",
                    new { expenseId, rel.ObjectType, rel.ObjectId }, transaction);
            }
        }

        // ---------- 报表/流水 ----------
        public PageResult<LedgerEntryDto> QueryLedger(IDbConnection connection, LedgerQueryRequest query)
        {
            string where = " WHERE 1 = 1";
            var parameters = new DynamicParameters();
            if (query.From.HasValue)
            {
                where += " AND BizTime >= @from";
                parameters.Add("from", query.From.Value);
            }
            if (query.To.HasValue)
            {
                where += " AND BizTime <= @to";
                parameters.Add("to", query.To.Value);
            }
            if (!string.IsNullOrWhiteSpace(query.BizType))
            {
                where += " AND BizType = @bizType";
                parameters.Add("bizType", query.BizType.Trim().ToLowerInvariant());
            }

            if (!string.IsNullOrWhiteSpace(query.Subject))
            {
                where += " AND Subject = @subject";
                parameters.Add("subject", query.Subject.Trim());
            }

            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                string kw = query.Keyword.Trim();
                // CHG-v1.1.0-18：搜索范围扩至「付款人 + 项目（科目）」——原仅支持单据号/流水号
                where += " AND (BizNo LIKE @kw OR printf('LS-%04d', Id) LIKE @kw" +
                         " OR OwnerName LIKE @kw OR Subject LIKE @kw)";
                parameters.Add("kw", "%" + kw + "%");
            }

            // 付款人信息（T4F-7-1 修订 / CHG-v1.1.0-18）：收款/退款/红冲按账单解析房产（或车位）业主；
            // 自定义缴费对象账单（payer_name）直接取手工填写的名称；支出无主体。
            string ownerExpr = "COALESCE(NULLIF(b.payer_name, ''), " +
                "(SELECT o.name FROM t_owner o JOIN t_owner_property_rel rel ON rel.owner_id = o.id AND rel.del_flag = 0 " +
                " WHERE rel.property_id = b.property_id ORDER BY rel.id DESC LIMIT 1), " +
                "(SELECT o.name FROM t_owner o JOIN t_parking_space ps ON ps.owner_id = o.id WHERE ps.id = b.parking_id LIMIT 1), '')";

            string from = "FROM (" +
                "SELECT p.id AS Id, p.paid_at AS BizTime, 'payment' AS BizType, " +
                // CHG-v1.1.0-15：关联单据优先取「收款流水号」（与收据打印模板一致），存量数据回落原收据号
                "COALESCE(NULLIF(p.batch_no, ''), r.receipt_no, '') AS BizNo, p.amount AS InAmount, 0 AS OutAmount, " +
                "COALESCE(ci.name, '收款') AS Subject, " +
                "CASE p.pay_method WHEN 0 THEN '现金' WHEN 1 THEN '转账' WHEN 2 THEN '微信' WHEN 3 THEN '银行转账' WHEN 4 THEN 'POS' ELSE '转账' END AS PayMethod, " +
                "'系统管理员' AS OperatorName, " + ownerExpr + " AS OwnerName " +
                "FROM t_payment p LEFT JOIN t_receipt r ON r.payment_id = p.id " +
                "LEFT JOIN t_bill b ON b.id = p.bill_id " +
                "LEFT JOIN t_charge_item ci ON ci.id = b.charge_item_id WHERE p.status = 0 " +
                "UNION ALL " +
                "SELECT pr.id, pr.created_at, 'refund', COALESCE(pr.ref_no, ''), 0, pr.amount, '退款/冲减', '原路退回', '系统管理员', " + ownerExpr + " AS OwnerName " +
                "FROM t_payment_refund pr LEFT JOIN t_bill b ON b.id = pr.bill_id " +
                "UNION ALL " +
                "SELECT e.id, e.expense_date, 'expense', CAST(e.id AS TEXT), 0, e.amount, COALESCE(c.name, '支出'), '银行转账', '系统管理员', '' AS OwnerName " +
                "FROM t_expense e LEFT JOIN t_expense_category c ON c.id = e.category_id WHERE e.del_flag = 0 " +
                "UNION ALL " +
                "SELECT p.id, p.paid_at, 'reversed', COALESCE(NULLIF(p.batch_no, ''), r.receipt_no, ''), -p.amount, 0, '红冲', '原路退回', '系统管理员', " + ownerExpr + " AS OwnerName " +
                "FROM t_payment p LEFT JOIN t_receipt r ON r.payment_id = p.id LEFT JOIN t_bill b ON b.id = p.bill_id WHERE p.status = 1) x";

            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;

            int total = connection.ExecuteScalar<int>("SELECT COUNT(1) " + from + where, parameters);

            string sql = "SELECT Id, BizTime, BizType, BizNo, PayMethod, OperatorName, Subject, OwnerName, " +
                "CAST(InAmount AS REAL) AS InAmount, CAST(OutAmount AS REAL) AS OutAmount " +
                from + where + " ORDER BY BizTime DESC, Id DESC LIMIT @limit OFFSET @offset";
            parameters.Add("limit", pageSize);
            parameters.Add("offset", offset);

            var items = connection.Query<LedgerEntryDto>(sql, parameters).ToList();
            return new PageResult<LedgerEntryDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }

        public FinancialReportDto BuildFinancialReport(IDbConnection connection, FinancialReportWindow window)
        {
            List<ReportItemDto> current = LoadReportItems(connection, window.From, window.To, window.ChargeItemId, window.IncludeRefund);
            List<ReportItemDto> prev = LoadReportItems(connection, window.PrevFrom, window.PrevTo, window.ChargeItemId, window.IncludeRefund);
            List<ReportItemDto> quarter = LoadReportItems(connection, window.QuarterFrom, window.QuarterTo, window.ChargeItemId, window.IncludeRefund);

            var report = new FinancialReportDto
            {
                Period = window.From.ToString("yyyy-MM") + " ~ " + window.To.ToString("yyyy-MM"),
                Items = current,
                SummaryItems = new List<FinancialSummaryItemDto>()
            };

            report.IncomeTotal = current.Where(x => x.Type == "income").Sum(x => x.Amount);
            report.ExpenseTotal = current.Where(x => x.Type == "expense").Sum(x => x.Amount);
            report.Balance = report.IncomeTotal - report.ExpenseTotal;

            decimal prevIncome = prev.Where(x => x.Type == "income").Sum(x => x.Amount);
            decimal prevExpense = prev.Where(x => x.Type == "expense").Sum(x => x.Amount);
            report.IncomeMomPercent = prevIncome == 0 ? 0m : (report.IncomeTotal - prevIncome) / prevIncome * 100m;
            report.ExpenseMomPercent = prevExpense == 0 ? 0m : (report.ExpenseTotal - prevExpense) / prevExpense * 100m;

            var allSubjects = current.Select(x => x.Category)
                .Concat(prev.Select(x => x.Category))
                .Concat(quarter.Select(x => x.Category))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            Func<string, List<ReportItemDto>, decimal> net = (subject, rows) =>
                rows.Where(x => x.Category == subject).Sum(x => x.Type == "income" ? x.Amount : -x.Amount);

            foreach (string subject in allSubjects)
            {
                decimal cur = net(subject, current);
                decimal pre = net(subject, prev);
                decimal qtr = net(subject, quarter);
                decimal? mom = pre == 0 ? (decimal?)null : (cur - pre) / pre * 100m;
                bool hasIncome = current.Any(x => x.Category == subject && x.Type == "income" && x.Amount > 0);
                bool hasRefund = current.Any(x => x.Category == subject && x.Type == "income" && x.Amount < 0);
                bool hasExpense = current.Any(x => x.Category == subject && x.Type == "expense");
                string remark = hasIncome ? "收入" : (hasRefund ? "冲减" : (hasExpense ? "支出" : "—"));
                report.SummaryItems.Add(new FinancialSummaryItemDto
                {
                    Category = subject,
                    CurrentAmount = cur,
                    PreviousAmount = pre,
                    MoM = mom,
                    QuarterTotal = qtr,
                    Remark = remark
                });
            }

            report.SummaryItems = report.SummaryItems
                .OrderBy(x => current.Any(c => c.Category == x.Category && c.Type == "income") ? 0 : 1)
                .ThenBy(x => x.Category)
                .ToList();

            return report;
        }

        private static List<ReportItemDto> LoadReportItems(IDbConnection connection, DateTime from, DateTime to, int? chargeItemId, bool includeRefund)
        {
            var branches = new List<string>
            {
                "SELECT p.paid_at AS BizTime, 'income' AS BizType, COALESCE(ci.name, '收款') AS Subject, " +
                "p.amount AS Signed, COALESCE(r.receipt_no, '') AS Note " +
                "FROM t_payment p LEFT JOIN t_receipt r ON r.payment_id = p.id " +
                "LEFT JOIN t_bill b ON b.id = p.bill_id LEFT JOIN t_charge_item ci ON ci.id = b.charge_item_id " +
                "WHERE p.status = 0 AND date(p.paid_at) >= date(@from) AND date(p.paid_at) <= date(@to)" +
                (chargeItemId.HasValue ? " AND b.charge_item_id = @cid" : string.Empty)
            };

            if (includeRefund)
            {
                branches.Add(
                    "SELECT pr.created_at AS BizTime, 'income' AS BizType, '退款/冲减' AS Subject, " +
                    "-pr.amount AS Signed, COALESCE(pr.ref_no, '') AS Note " +
                    "FROM t_payment_refund pr LEFT JOIN t_bill rb ON rb.id = pr.bill_id " +
                    "WHERE date(pr.created_at) >= date(@from) AND date(pr.created_at) <= date(@to)" +
                    (chargeItemId.HasValue ? " AND rb.charge_item_id = @cid" : string.Empty));
            }

            branches.Add(
                "SELECT e.expense_date AS BizTime, 'expense' AS BizType, COALESCE(c.name, '支出') AS Subject, " +
                "e.amount AS Signed, COALESCE(e.note, '') AS Note " +
                "FROM t_expense e LEFT JOIN t_expense_category c ON c.id = e.category_id " +
                "WHERE e.del_flag = 0 AND date(e.expense_date) >= date(@from) AND date(e.expense_date) <= date(@to)");

            string sql = "SELECT BizTime AS Date, BizType AS Type, Subject AS Category, CAST(Signed AS REAL) AS Amount, Note FROM (" +
                string.Join(" UNION ALL ", branches) + ") x";

            var parameters = new DynamicParameters();
            parameters.Add("from", from);
            parameters.Add("to", to);
            if (chargeItemId.HasValue)
            {
                parameters.Add("cid", chargeItemId.Value);
            }

            return connection.Query<ReportItemDto>(sql, parameters).ToList();
        }



        public int InsertReportLog(IDbConnection connection, IDbTransaction transaction, ReportLogDto log)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_report_log (report_type, period, format, file_path) " +
                "VALUES (@ReportType, @Period, @Format, @FilePath); SELECT last_insert_rowid();",
                new { log.ReportType, log.Period, log.Format, log.FilePath }, transaction);
        }

        public ReportLogDto GetReportLog(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<ReportLogDto>(
                "SELECT id, report_type AS ReportType, period, format, file_path AS FilePath, created_at AS CreatedAt " +
                "FROM t_report_log WHERE id = @id", new { id });
        }
    }
}
