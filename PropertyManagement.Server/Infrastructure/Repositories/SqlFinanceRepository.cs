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
                         "price_unit AS PriceUnit, cycle_name AS CycleName " +
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
                "price_unit AS PriceUnit, cycle_name AS CycleName " +
                "FROM t_charge_item WHERE id = @id AND del_flag = 0", new { id });
        }

        public ChargeItemDto GetChargeItemByName(IDbConnection connection, string name)
        {
            return connection.QueryFirstOrDefault<ChargeItemDto>(
                "SELECT id, name, pay_mode AS PayMode, unit_price AS UnitPrice, cycle_type AS CycleType, " +
                "object_type AS ObjectType, status, del_flag AS DelFlag, category, method_code AS MethodCode, " +
                "method_name AS MethodName, price_unit AS PriceUnit, cycle_name AS CycleName " +
                "FROM t_charge_item WHERE name = @name AND del_flag = 0 LIMIT 1", new { name });
        }

        public int InsertChargeItem(IDbConnection connection, IDbTransaction transaction, ChargeItemDto item)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_charge_item (name, pay_mode, unit_price, cycle_type, object_type, status, del_flag, " +
                "category, method_code, method_name, price_unit, cycle_name) " +
                "VALUES (@Name, @PayMode, @UnitPrice, @CycleType, @ObjectType, @Status, 0, " +
                "@Category, @MethodCode, @MethodName, @PriceUnit, @CycleName); SELECT last_insert_rowid();",
                new { item.Name, item.PayMode, item.UnitPrice, item.CycleType, item.ObjectType, item.Status, item.Category, item.MethodCode, item.MethodName, item.PriceUnit, item.CycleName }, transaction);
        }

        public void UpdateChargeItem(IDbConnection connection, IDbTransaction transaction, ChargeItemDto item)
        {
            connection.Execute(
                "UPDATE t_charge_item SET name = @Name, pay_mode = @PayMode, unit_price = @UnitPrice, " +
                "cycle_type = @CycleType, object_type = @ObjectType, status = @Status, " +
                "category = @Category, method_code = @MethodCode, method_name = @MethodName, " +
                "price_unit = @PriceUnit, cycle_name = @CycleName, updated_at = datetime('now','localtime') " +
                "WHERE id = @Id AND del_flag = 0",
                new { item.Id, item.Name, item.PayMode, item.UnitPrice, item.CycleType, item.ObjectType, item.Status, item.Category, item.MethodCode, item.MethodName, item.PriceUnit, item.CycleName }, transaction);
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

        /// <summary>BR-INF-02（M7 BUG-002）：缴费对象有效业主关系判定（口径同 GetOwnerIdByBill）。</summary>
        public bool HasValidOwnerRelation(IDbConnection connection, int? propertyId, int? parkingId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COALESCE(" +
                "  (SELECT rel.owner_id FROM t_owner_property_rel rel " +
                "    WHERE rel.property_id = @propertyId AND rel.del_flag = 0 AND rel.rel_status <> 2 " +
                "    ORDER BY rel.id DESC LIMIT 1), " +
                "  (SELECT ps.owner_id FROM t_parking_space ps WHERE ps.id = @parkingId AND ps.del_flag = 0), 0)",
                new { propertyId, parkingId }) > 0;
        }
        public BillDto FindDuplicateBill(IDbConnection connection, IDbTransaction transaction,
            int chargeItemId, int? propertyId, int? parkingId, int cycleId)
        {
            return connection.QueryFirstOrDefault<BillDto>(
                "SELECT id, charge_item_id AS ChargeItemId, property_id AS PropertyId, parking_id AS ParkingId, " +
                "cycle_id AS CycleId, amount, paid_amount AS PaidAmount, status, due_at AS DueAt, " +
                "generate_batch_id AS GenerateBatchId, del_flag AS DelFlag " +
                "FROM t_bill WHERE del_flag = 0 AND charge_item_id = @chargeItemId AND cycle_id = @cycleId " +
                "AND ((@propertyId IS NOT NULL AND property_id = @propertyId) OR (@parkingId IS NOT NULL AND parking_id = @parkingId)) " +
                "LIMIT 1",
                new { chargeItemId, propertyId, parkingId, cycleId }, transaction);
        }

        public int InsertBill(IDbConnection connection, IDbTransaction transaction, BillDto bill)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_bill (charge_item_id, property_id, parking_id, cycle_id, amount, paid_amount, " +
                "status, due_at, generate_batch_id, del_flag) " +
                "VALUES (@ChargeItemId, @PropertyId, @ParkingId, @CycleId, @Amount, 0, @Status, @DueAt, @GenerateBatchId, @DelFlag); " +
                "SELECT last_insert_rowid();",
                new { bill.ChargeItemId, bill.PropertyId, bill.ParkingId, bill.CycleId, bill.Amount, bill.Status, bill.DueAt, bill.GenerateBatchId, bill.DelFlag },
                transaction);
        }

        public void UpdateBillPaidAmount(IDbConnection connection, IDbTransaction transaction, BillDto bill)
        {
            connection.Execute(
                "UPDATE t_bill SET paid_amount = @PaidAmount, status = @Status, " +
                "updated_at = datetime('now','localtime') WHERE id = @Id",
                new { bill.Id, bill.PaidAmount, bill.Status }, transaction);
        }

        public void MarkOverdue(IDbConnection connection, IDbTransaction transaction, DateTime now)
        {
            var overdue = connection.Query<int>(
                "SELECT id FROM t_bill WHERE del_flag = 0 AND status IN (0,1) " +
                "AND amount > paid_amount AND due_at < @now",
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
                "cycle_id AS CycleId, amount, paid_amount AS PaidAmount, status, due_at AS DueAt, " +
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
                "INSERT INTO t_bill_generate_log (generate_at, total, success, fail, fail_detail) " +
                "VALUES (datetime('now','localtime'), @Total, @Success, @Fail, @FailDetail); SELECT last_insert_rowid();",
                new { log.Total, log.Success, log.Fail, log.FailDetail }, transaction);
        }

        public BillGenerateLogDto GetBillGenerateLog(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<BillGenerateLogDto>(
                "SELECT id, generate_at AS GenerateAt, total, success, fail, fail_detail AS FailDetail " +
                "FROM t_bill_generate_log WHERE id = @id", new { id });
        }

        public void UpdateGenerateLogResult(IDbConnection connection, IDbTransaction transaction,
            int id, int success, int fail, string failDetail)
        {
            connection.Execute(
                "UPDATE t_bill_generate_log SET success = @success, fail = @fail, fail_detail = @failDetail WHERE id = @id",
                new { id, success, fail, failDetail }, transaction);
        }

        private const string BillListSql =
            "SELECT b.id, b.charge_item_id AS ChargeItemId, ci.name AS ChargeItemName, " +
            "b.property_id AS PropertyId, b.parking_id AS ParkingId, " +
            "COALESCE(p.room_no, ps.space_no, '') AS PropertyNo, " +
            "COALESCE(o.name, '') AS OwnerName, b.cycle_id AS CycleId, " +
            "COALESCE(cy.start_date, '') || ' ~ ' || COALESCE(cy.end_date, '') AS CyclePeriod, " +
            "b.amount, b.paid_amount AS PaidAmount, b.status, b.due_at AS DueAt, " +
            "b.generate_batch_id AS GenerateBatchId, b.del_flag AS DelFlag " +
            "FROM t_bill b " +
            "JOIN t_charge_item ci ON ci.id = b.charge_item_id " +
            "LEFT JOIN t_billing_cycle cy ON cy.id = b.cycle_id " +
            "LEFT JOIN t_property p ON p.id = b.property_id " +
            "LEFT JOIN t_parking_space ps ON ps.id = b.parking_id " +
            "LEFT JOIN t_owner o ON o.id = COALESCE(" +
            "  (SELECT owner_id FROM t_owner_property_rel rel WHERE rel.property_id = b.property_id AND rel.del_flag = 0 ORDER BY rel.id DESC LIMIT 1), " +
            "  ps.owner_id) " +
            "WHERE b.del_flag = 0";

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
                "SELECT COUNT(1) FROM t_bill b WHERE b.del_flag = 0" + where.Substring(" AND b.del_flag = 0".Length),
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
                "COALESCE((SELECT COALESCE(cy.start_date, '') || ' ~ ' || COALESCE(cy.end_date, '') " +
                "  FROM t_bill b2 JOIN t_billing_cycle cy ON cy.id = b2.cycle_id " +
                "  WHERE b2.generate_batch_id = gl.id AND b2.del_flag = 0 LIMIT 1), '') AS CyclePeriod, " +
                "(SELECT COUNT(1) FROM t_bill b3 WHERE b3.generate_batch_id = gl.id AND b3.del_flag = 0) AS HouseCount, " +
                "(SELECT CAST(COALESCE(SUM(b4.amount), 0) AS REAL) FROM t_bill b4 WHERE b4.generate_batch_id = gl.id AND b4.del_flag = 0) AS TotalAmount, " +
                "gl.success AS SuccessCount, gl.fail AS FailCount, " +
                "(SELECT COUNT(1) FROM t_bill b5 WHERE b5.generate_batch_id = gl.id AND b5.del_flag = 0 AND b5.status = 5) AS DraftCount, " +
                "(SELECT COUNT(1) FROM t_bill b6 WHERE b6.generate_batch_id = gl.id AND b6.del_flag = 0 AND b6.status IN (0,2)) AS PendingCount, " +
                "(SELECT COUNT(1) FROM t_bill b7 WHERE b7.generate_batch_id = gl.id AND b7.del_flag = 0 AND b7.status = 1) AS PartialCount, " +
                "(SELECT COUNT(1) FROM t_bill b8 WHERE b8.generate_batch_id = gl.id AND b8.del_flag = 0 AND b8.status = 3) AS PaidCount, " +
                "gl.generate_at AS GenerateAt, " +
                "(SELECT MAX(b9.updated_at) FROM t_bill b9 WHERE b9.generate_batch_id = gl.id AND b9.del_flag = 0 AND b9.status <> 5) AS PublishedAtRaw " +
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
                "AND (EXISTS (SELECT 1 FROM t_owner_property_rel rel2 WHERE rel2.property_id = b.property_id " +
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
                "INSERT INTO t_payment (bill_id, amount, pay_method, paid_at, status, to_pre_deposit, remark) " +
                "VALUES (@BillId, @Amount, @PayMethod, @PaidAt, 0, @ToPreDeposit, @Remark); SELECT last_insert_rowid();",
                new { payment.BillId, payment.Amount, payment.PayMethod, payment.PaidAt, payment.ToPreDeposit, payment.Remark },
                transaction);
        }

        public PaymentDto GetPayment(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<PaymentDto>(
                "SELECT id, bill_id AS BillId, amount, pay_method AS PayMethod, paid_at AS PaidAt, " +
                "status, to_pre_deposit AS ToPreDeposit, remark FROM t_payment WHERE id = @id", new { id });
        }

        public List<PaymentDto> ListPayments(IDbConnection connection, PageRequest query, out int total)
        {
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;
            total = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_payment");
            return connection.Query<PaymentDto>(
                "SELECT id, bill_id AS BillId, amount, pay_method AS PayMethod, paid_at AS PaidAt, " +
                "status, to_pre_deposit AS ToPreDeposit, remark FROM t_payment ORDER BY paid_at DESC, id DESC LIMIT @limit OFFSET @offset",
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
                where += " AND (BizNo LIKE @kw OR printf('LS-%04d', Id) LIKE @kw)";
                parameters.Add("kw", "%" + kw + "%");
            }

            // 付款户主信息（T4F-7-1 修订）：收款/退款/红冲按账单解析房产（或车位）业主，支出无房产主体。
            string ownerExpr = "COALESCE(" +
                "(SELECT o.name FROM t_owner o JOIN t_owner_property_rel rel ON rel.owner_id = o.id AND rel.del_flag = 0 " +
                " WHERE rel.property_id = b.property_id ORDER BY rel.id DESC LIMIT 1), " +
                "(SELECT o.name FROM t_owner o JOIN t_parking_space ps ON ps.owner_id = o.id WHERE ps.id = b.parking_id LIMIT 1), '')";

            string from = "FROM (" +
                "SELECT p.id AS Id, p.paid_at AS BizTime, 'payment' AS BizType, " +
                "COALESCE(r.receipt_no, '') AS BizNo, p.amount AS InAmount, 0 AS OutAmount, " +
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
                "SELECT p.id, p.paid_at, 'reversed', COALESCE(r.receipt_no, ''), -p.amount, 0, '红冲', '原路退回', '系统管理员', " + ownerExpr + " AS OwnerName " +
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
