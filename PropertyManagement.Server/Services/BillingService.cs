using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Newtonsoft.Json;
using PropertyManagement.Contract.Common;
using Dapper;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 收费/账单服务（D4-1 + D4-5，UC-FIN-001/002/007/008，FL-FIN-01）：
    /// 收费项目与计费周期维护；账单草稿→发布→失败清单重推；欠费台账与催缴记录。
    /// 事务边界在本服务层控制；敏感操作经 AuditService 留痕（BR-COM-01）。
    /// </summary>
    public class BillingService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IFinanceRepository _finance;
        private readonly AuditService _audit;

        public BillingService()
            : this(new SqliteConnectionFactory(), new SqlFinanceRepository(), new AuditService())
        {
        }

        public BillingService(IDbConnectionFactory connectionFactory, IFinanceRepository finance, AuditService audit)
        {
            _connectionFactory = connectionFactory;
            _finance = finance;
            _audit = audit;
        }

        // ---------- 收费项目（UC-FIN-001） ----------
        public List<ChargeItemDto> ListChargeItems(string keyword, string category)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _finance.ListChargeItems(connection, keyword, category);
            }
        }

        public ChargeItemDto CreateChargeItem(ChargeItemRequest request, string operatorName = null, string ip = null)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                throw ApiException.BadRequest("收费项目名称不能为空");
            }
            if (string.IsNullOrWhiteSpace(request.Category))
            {
                throw ApiException.ValidationFailed("请选择收费项目类别");
            }
            if (string.IsNullOrWhiteSpace(request.MethodCode))
            {
                throw ApiException.ValidationFailed("请选择计价方式");
            }
            if (request.UnitPrice <= 0)
            {
                throw ApiException.ValidationFailed("单价必须大于 0");
            }
            if (request.CycleType == BillingCycleType.Custom && string.IsNullOrWhiteSpace(request.CycleName))
            {
                throw ApiException.ValidationFailed("自定义计费周期必须填写周期名称");
            }

            var item = new ChargeItemDto
            {
                Name = request.Name.Trim(),
                PayMode = request.PayMode,
                UnitPrice = request.UnitPrice,
                CycleType = request.CycleType,
                Status = request.Status ?? 0,
                Category = request.Category.Trim(),
                MethodCode = request.MethodCode.Trim(),
                MethodName = string.IsNullOrWhiteSpace(request.MethodName) ? request.MethodCode : request.MethodName.Trim(),
                PriceUnit = string.IsNullOrWhiteSpace(request.PriceUnit) ? ResolveDefaultPriceUnit(request.MethodCode) : request.PriceUnit.Trim(),
                CycleName = request.CycleType == BillingCycleType.Custom ? (request.CycleName ?? string.Empty).Trim()
                            : (request.CycleType == BillingCycleType.OneTime ? "一次性" : string.Empty),
                ObjectType = ResolveObjectType(request.MethodCode)
            };

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                // T4F-1-6：同名去重（软删重名允许）
                ChargeItemDto sameName = _finance.GetChargeItemByName(connection, item.Name);
                if (sameName != null)
                {
                    throw ApiException.ValidationFailed("同名收费项目已存在");
                }

                item.Id = _finance.InsertChargeItem(connection, transaction, item);
                transaction.Commit();
            }

            _audit.Write("CHARGE_ITEM_CREATE", "charge_item", item.Id.ToString(),
                "新增收费项目：" + item.Name + "，类别 " + item.Category + "，单价 " + item.UnitPrice.ToString("0.00") + "/" + item.PriceUnit,
                userName: operatorName, ip: ip, result: "Success");
            return item;
        }

        public ChargeItemDto UpdateChargeItem(int id, ChargeItemRequest request, string operatorName = null, string ip = null)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                throw ApiException.BadRequest("收费项目名称不能为空");
            }
            if (string.IsNullOrWhiteSpace(request.Category))
            {
                throw ApiException.ValidationFailed("请选择收费项目类别");
            }
            if (string.IsNullOrWhiteSpace(request.MethodCode))
            {
                throw ApiException.ValidationFailed("请选择计价方式");
            }
            if (request.UnitPrice <= 0)
            {
                throw ApiException.ValidationFailed("单价必须大于 0");
            }
            if (request.CycleType == BillingCycleType.Custom && string.IsNullOrWhiteSpace(request.CycleName))
            {
                throw ApiException.ValidationFailed("自定义计费周期必须填写周期名称");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeItemDto existing = _finance.GetChargeItem(connection, id);
                if (existing == null)
                {
                    throw ApiException.NotFound("收费项目不存在或已删除");
                }

                existing.Name = request.Name.Trim();
                existing.PayMode = request.PayMode;
                existing.UnitPrice = request.UnitPrice;
                existing.CycleType = request.CycleType;
                existing.Status = request.Status ?? existing.Status;
                existing.Category = request.Category.Trim();
                existing.MethodCode = request.MethodCode.Trim();
                existing.MethodName = string.IsNullOrWhiteSpace(request.MethodName) ? request.MethodCode : request.MethodName.Trim();
                existing.PriceUnit = string.IsNullOrWhiteSpace(request.PriceUnit) ? ResolveDefaultPriceUnit(request.MethodCode) : request.PriceUnit.Trim();
                existing.CycleName = request.CycleType == BillingCycleType.Custom ? (request.CycleName ?? string.Empty).Trim()
                                     : (request.CycleType == BillingCycleType.OneTime ? "一次性" : string.Empty);
                existing.ObjectType = ResolveObjectType(request.MethodCode);
                _finance.UpdateChargeItem(connection, transaction, existing);
                transaction.Commit();

                _audit.Write("CHARGE_ITEM_UPDATE", "charge_item", id.ToString(),
                    "修改收费项目：" + existing.Name + "，类别 " + existing.Category + "，单价 " + existing.UnitPrice.ToString("0.00") + "/" + existing.PriceUnit,
                    userName: operatorName, ip: ip, result: "Success");
                return existing;
            }
        }

        public void DeleteChargeItem(int id, string operatorName = null, string ip = null)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeItemDto existing = _finance.GetChargeItem(connection, id);
                if (existing == null)
                {
                    throw ApiException.NotFound("收费项目不存在或已删除");
                }

                _finance.SoftDeleteChargeItem(connection, transaction, id);
                transaction.Commit();

                _audit.Write("CHARGE_ITEM_DELETE", "charge_item", id.ToString(),
                    "停用收费项目：" + existing.Name + "（停用不影响已出账单 BR-FIN-03）",
                    userName: operatorName, ip: ip, result: "Success");
            }
        }

        // ---------- 计费周期（UC-FIN-001） ----------
        public List<BillingCycleDto> ListCycles()
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _finance.ListCycles(connection);
            }
        }

        public BillingCycleDto CreateCycle(BillingCycleRequest request)
        {
            ValidateCycle(request);
            var cycle = new BillingCycleDto
            {
                CycleType = request.CycleType,
                StartDate = request.StartDate,
                EndDate = request.EndDate
            };

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                cycle.Id = _finance.InsertCycle(connection, transaction, cycle);
                transaction.Commit();
            }
            return cycle;
        }

        public BillingCycleDto UpdateCycle(int id, BillingCycleRequest request)
        {
            ValidateCycle(request);
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                BillingCycleDto existing = _finance.GetCycle(connection, id);
                if (existing == null)
                {
                    throw ApiException.NotFound("计费周期不存在");
                }

                existing.CycleType = request.CycleType;
                existing.StartDate = request.StartDate;
                existing.EndDate = request.EndDate;
                _finance.UpdateCycle(connection, transaction, existing);
                transaction.Commit();
                return existing;
            }
        }

        public void DeleteCycle(int id)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                BillingCycleDto existing = _finance.GetCycle(connection, id);
                if (existing == null)
                {
                    throw ApiException.NotFound("计费周期不存在");
                }

                _finance.DeleteCycle(connection, transaction, id);
                transaction.Commit();
            }
        }

        private static void ValidateCycle(BillingCycleRequest request)
        {
            if (request == null)
            {
                throw ApiException.BadRequest("计费周期不能为空");
            }
            if (request.EndDate < request.StartDate)
            {
                throw ApiException.ValidationFailed("周期结束日期不能早于开始日期");
            }
        }
        // ---------- 账单生成/发布/失败重推（UC-FIN-002，FL-FIN-01） ----------
        public BillGenerateLogDto GenerateBill(BillGenerateRequest request, string operatorName = null, string ip = null)
        {
            if (request == null || request.ChargeItemId <= 0 || request.CycleId <= 0)
            {
                throw ApiException.BadRequest("收费项目和计费周期必须选择");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeItemDto chargeItem = _finance.GetChargeItem(connection, request.ChargeItemId);
                if (chargeItem == null)
                {
                    throw ApiException.NotFound("收费项目不存在或已停用");
                }
                if (chargeItem.Status == 1)
                {
                    // BR-FIN-03：停用后不再生成新账单（历史账单不受影响，T4F-1-6 UC6）
                    throw ApiException.BadRequest("收费项目已停用，无法生成新账单（BR-FIN-03）");
                }
                BillingCycleDto cycle = _finance.GetCycle(connection, request.CycleId);
                if (cycle == null)
                {
                    throw ApiException.NotFound("计费周期不存在");
                }

                List<BillObjectCandidate> candidates = BuildCandidates(connection, request);
                if (candidates.Count == 0)
                {
                    throw ApiException.BadRequest("未选择缴费对象，且当前无房产/车位可生成");
                }

                var log = new BillGenerateLogDto { Total = candidates.Count, Success = 0, Fail = 0 };
                int logId = _finance.InsertBillGenerateLog(connection, transaction, log);

                var failures = new List<BillFailureDto>();
                int success = 0;
                DateTime dueAt = ComputeDueAt(cycle);

                foreach (BillObjectCandidate candidate in candidates)
                {
                    int? propertyId = candidate.Kind == BillObjectKind.Property ? (int?)candidate.Id : null;
                    int? parkingId = candidate.Kind == BillObjectKind.Parking ? (int?)candidate.Id : null;

                    // BR-INF-02（M7 BUG-002 裁定补校验）：缴费对象必须存在有效「房产-业主」关系，否则记失败行不入库
                    if (!_finance.HasValidOwnerRelation(connection, propertyId, parkingId))
                    {
                        failures.Add(new BillFailureDto
                        {
                            PropertyId = propertyId,
                            ParkingId = parkingId,
                            No = candidate.No,
                            Reason = "缴费对象不存在有效「房产-业主」关系，请先绑定业主后再出账（BR-INF-02）"
                        });
                        continue;
                    }

                    BillDto duplicate = _finance.FindDuplicateBill(
                        connection, transaction, request.ChargeItemId, propertyId, parkingId, request.CycleId);

                    if (duplicate != null)
                    {
                        // BR-FIN-01：同对象同周期同项目已存在 → 入失败清单（不落账单，fail_detail 留痕，可重推）
                        failures.Add(new BillFailureDto
                        {
                            PropertyId = propertyId,
                            ParkingId = parkingId,
                            No = candidate.No,
                            Reason = "同对象同周期同项目账单已存在（BR-FIN-01）"
                        });
                        continue;
                    }

                    // T4F-1-6：按计价方式计算金额（按建筑面积 = 单价 × 面积；面积缺失入失败清单）
                    decimal amount = ComputeBillAmount(chargeItem, candidate);
                    if (amount <= 0)
                    {
                        failures.Add(new BillFailureDto
                        {
                            PropertyId = propertyId,
                            ParkingId = parkingId,
                            No = candidate.No,
                            Reason = "房产缺少建筑面积，无法按面积计费"
                        });
                        continue;
                    }

                    _finance.InsertBill(connection, transaction, new BillDto
                    {
                        ChargeItemId = request.ChargeItemId,
                        PropertyId = propertyId,
                        ParkingId = parkingId,
                        CycleId = request.CycleId,
                        Amount = amount,
                        Status = BillStatus.Draft,
                        DueAt = dueAt,
                        GenerateBatchId = logId,
                        DelFlag = false
                    });
                    success++;
                }

                string failDetail = JsonConvert.SerializeObject(new
                {
                    chargeItemId = request.ChargeItemId,
                    cycleId = request.CycleId,
                    failures
                });
                _finance.UpdateGenerateLogResult(connection, transaction, logId, success, failures.Count, failDetail);
                transaction.Commit();

                _audit.Write("BILL_GENERATE", "bill_generate_log", logId.ToString(),
                    string.Format("生成账单：项目 {0}，周期 {1}，成功 {2}，失败 {3}", chargeItem.Name, cycle.StartDate.ToString("yyyy-MM-dd"), success, failures.Count),
                    userName: operatorName, ip: ip, result: "Success");

                return _finance.GetBillGenerateLog(connection, logId);
            }
        }

        public BillGenerateLogDto PublishBills(BillPublishRequest request, string operatorName = null, string ip = null)
        {
            if (request == null || request.BatchId <= 0)
            {
                throw ApiException.BadRequest("账单批次不能为空");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                BillGenerateLogDto log = _finance.GetBillGenerateLog(connection, request.BatchId);
                if (log == null)
                {
                    throw ApiException.NotFound("账单批次不存在");
                }

                _finance.PublishBatchBills(connection, transaction, request.BatchId);
                transaction.Commit();

                _audit.Write("BILL_PUBLISH", "bill_generate_log", request.BatchId.ToString(),
                    "发布账单批次，草稿转待缴（FL-FIN-01）",
                    userName: operatorName, ip: ip, result: "Success");
                return _finance.GetBillGenerateLog(connection, request.BatchId);
            }
        }

        public BillGenerateLogDto RetryFailures(BillRetryRequest request)
        {
            if (request == null || request.BatchId <= 0)
            {
                throw ApiException.BadRequest("账单批次不能为空");
            }

            BillGenerateLogDto log;
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                log = _finance.GetBillGenerateLog(connection, request.BatchId);
                if (log == null)
                {
                    throw ApiException.NotFound("账单批次不存在");
                }
            }

            BillFailureDetail detail = ParseFailDetail(log.FailDetail);
            if (detail == null || detail.Failures.Count == 0)
            {
                throw ApiException.BadRequest("该批次无失败记录可重推");
            }

            return GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = detail.ChargeItemId,
                CycleId = detail.CycleId,
                PropertyIds = detail.Failures.Where(f => f.PropertyId.HasValue).Select(f => f.PropertyId.Value).ToList(),
                ParkingIds = detail.Failures.Where(f => f.ParkingId.HasValue).Select(f => f.ParkingId.Value).ToList()
            });
        }
        public BillGenerateLogDto GetGenerateLog(int id)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                BillGenerateLogDto log = _finance.GetBillGenerateLog(connection, id);
                if (log == null)
                {
                    throw ApiException.NotFound("账单批次不存在");
                }
                return log;
            }
        }

        public List<BillFailureDto> ListFailures(int batchId)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                BillGenerateLogDto log = _finance.GetBillGenerateLog(connection, batchId);
                if (log == null)
                {
                    throw ApiException.NotFound("账单批次不存在");
                }

                BillFailureDetail detail = ParseFailDetail(log.FailDetail);
                return detail == null || detail.Failures == null
                    ? new List<BillFailureDto>()
                    : detail.Failures;
            }
        }

        public List<BillListItemDto> ListDrafts(int batchId)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _finance.ListDraftBillsByBatch(connection, batchId);
            }
        }

        public List<BillListItemDto> ListPublished(int batchId)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _finance.ListPublishedBillsByBatch(connection, batchId);
            }
        }

        /// <summary>T4F-1-6：按计价方式派生适用对象（按车位→车位，其余→房产）。</summary>
        private static ChargeObjectType ResolveObjectType(string methodCode)
        {
            return string.Equals(methodCode, "parking", StringComparison.OrdinalIgnoreCase)
                ? ChargeObjectType.Parking
                : ChargeObjectType.Property;
        }

        /// <summary>T4F-1-6：内置计价方式默认单价单位（自定义由前端传入）。</summary>
        private static string ResolveDefaultPriceUnit(string methodCode)
        {
            if (string.Equals(methodCode, "area", StringComparison.OrdinalIgnoreCase)) { return "㎡"; }
            if (string.Equals(methodCode, "parking", StringComparison.OrdinalIgnoreCase)) { return "车位"; }
            if (string.Equals(methodCode, "card", StringComparison.OrdinalIgnoreCase)) { return "张"; }
            return "户";
        }

        /// <summary>T4F-1-6：按计价方式计算账单金额（按建筑面积 = 单价 × 建筑面积；其余按单价/对象）。</summary>
        private static decimal ComputeBillAmount(ChargeItemDto chargeItem, BillObjectCandidate candidate)
        {
            if (string.Equals(chargeItem.MethodCode, "area", StringComparison.OrdinalIgnoreCase))
            {
                decimal area = candidate.Area ?? 0m;
                if (area <= 0) { return 0m; }
                return Math.Round(chargeItem.UnitPrice * area, 2, MidpointRounding.AwayFromZero);
            }
            return chargeItem.UnitPrice;
        }

        private List<BillObjectCandidate> BuildCandidates(
            IDbConnection connection, BillGenerateRequest request)
        {
            BillObjectKind chargeItemObjectType = BillObjectKind.Property;
            ChargeItemDto chargeItem = _finance.GetChargeItem(connection, request.ChargeItemId);
            if (chargeItem != null && chargeItem.ObjectType == PropertyManagement.Contract.Enums.ChargeObjectType.Parking)
            {
                chargeItemObjectType = BillObjectKind.Parking;
            }

            var candidates = new List<BillObjectCandidate>();
            bool hasProperty = request.PropertyIds != null && request.PropertyIds.Count > 0;
            bool hasParking = request.ParkingIds != null && request.ParkingIds.Count > 0;

            if (!hasProperty && !hasParking)
            {
                // "全部对象"按收费项目适用对象类型取候选（CHG-M4-09）：物业费/电梯维护费→房产，停车费→车位
                if (chargeItemObjectType == BillObjectKind.Parking)
                {
                    foreach (BillObjectCandidate ps in _finance.ListParkingCandidates(connection))
                    {
                        ps.Kind = BillObjectKind.Parking;
                        candidates.Add(ps);
                    }
                }
                else
                {
                    foreach (BillObjectCandidate p in _finance.ListPropertyCandidates(connection))
                    {
                        p.Kind = BillObjectKind.Property;
                        candidates.Add(p);
                    }
                }
                return candidates;
            }

            if (hasProperty)
            {
                foreach (BillObjectCandidate p in _finance.ListPropertyCandidates(connection))
                {
                    if (request.PropertyIds.Contains(p.Id))
                    {
                        p.Kind = BillObjectKind.Property;
                        candidates.Add(p);
                    }
                }
            }
            if (hasParking)
            {
                foreach (BillObjectCandidate ps in _finance.ListParkingCandidates(connection))
                {
                    if (request.ParkingIds.Contains(ps.Id))
                    {
                        ps.Kind = BillObjectKind.Parking;
                        candidates.Add(ps);
                    }
                }
            }
            return candidates;
        }

        private static DateTime ComputeDueAt(BillingCycleDto cycle)
        {
            // 宽限期 P-01（arrear.grace.days，默认 0=到期日即逾期）：账单到期日 = 周期结束日 + 宽限天数
            int graceDays = 0;
            using (IDbConnection connection = new SqliteConnectionFactory().OpenConnection())
            {
                string value = connection.ExecuteScalar<string>(
                    "SELECT param_value FROM t_param WHERE param_key = 'arrear.grace.days'");
                int parsed;
                if (int.TryParse(value, out parsed))
                {
                    graceDays = parsed;
                }
            }
            return cycle.EndDate.AddDays(graceDays);
        }

        private static BillFailureDetail ParseFailDetail(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<BillFailureDetail>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------- 账单查询/统计/欠费台账 ----------
        public PageResult<BillListItemDto> QueryBills(BillQueryRequest query)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                _finance.MarkOverdue(connection, transaction, DateTime.Now);
                transaction.Commit();
                return _finance.QueryBills(connection, query ?? new BillQueryRequest());
            }
        }

        public PaymentStatisticsDto GetPaymentStatistics()
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _finance.GetPaymentStatistics(connection);
            }
        }

        public PageResult<ArrearDto> QueryArrears(BillQueryRequest query)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                _finance.MarkOverdue(connection, transaction, DateTime.Now);
                transaction.Commit();
                return _finance.QueryArrears(connection, query ?? new BillQueryRequest());
            }
        }

        /// <summary>账单生成批次列表（CHG-M4-10：PG-FIN-02 批次工作台）。</summary>
        public List<BillBatchDto> QueryGenerateLogs()
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _finance.QueryGenerateLogs(connection);
            }
        }


        /// <summary>删除账单批次（CHG-M4-16 修订：任意状态均可删除，批次及其账单一并软删，保留操作轨迹；已缴/部分缴流水不回退）。</summary>
        public void DeleteBillBatch(int batchId, string operatorName = null, string ip = null)
        {
            if (batchId <= 0)
            {
                throw ApiException.BadRequest("批次不能为空");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                BillGenerateLogDto log = _finance.GetBillGenerateLog(connection, batchId);
                if (log == null)
                {
                    throw ApiException.NotFound("账单批次不存在");
                }
                _finance.SoftDeleteBillBatch(connection, transaction, batchId);
                transaction.Commit();
            }

            _audit.Write("BILL_BATCH_DELETE", "bill_generate_log", batchId.ToString(),
                "删除账单批次（软删并保留轨迹，已缴/部分缴流水不回退）",
                userName: operatorName, ip: ip, result: "Success");
        }
        public void RecordRemind(ArrearRemindRequest request, string operatorName = null, string ip = null)
        {
            if (request == null || request.BillId <= 0)
            {
                throw ApiException.BadRequest("账单不能为空");
            }
            if (string.IsNullOrWhiteSpace(request.Channel))
            {
                throw ApiException.ValidationFailed("催缴渠道不能为空（电话/短信/上门/微信等）");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                BillDto bill = _finance.GetBill(connection, request.BillId);
                if (bill == null)
                {
                    throw ApiException.NotFound("账单不存在或已删除");
                }

                _finance.InsertArrearRemind(connection, transaction, request.BillId, request.Channel.Trim(), request.Note, null);
                transaction.Commit();

                _audit.Write("ARREARS_REMIND", "bill", request.BillId.ToString(),
                    "欠费催缴记录：渠道 " + request.Channel.Trim() + (string.IsNullOrWhiteSpace(request.Note) ? "" : "，备注 " + request.Note.Trim()),
                    userName: operatorName, ip: ip, result: "Success");
            }
        }

        /// <summary>欠费台账删除（UC-FIN-007：软删除账单，保留查账轨迹）。</summary>
        public void DeleteArrearBill(int billId, string operatorName = null, string ip = null)
        {
            if (billId <= 0)
            {
                throw ApiException.BadRequest("账单不能为空");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                BillDto bill = _finance.GetBill(connection, billId);
                if (bill == null)
                {
                    throw ApiException.NotFound("账单不存在或已删除");
                }

                _finance.SoftDeleteBill(connection, transaction, billId);
                transaction.Commit();
            }

            _audit.Write("ARREARS_DELETE", "bill", billId.ToString(), "欠费台账删除（软删除，保留查账轨迹）",
                userName: operatorName, ip: ip, result: "Success");
        }

        /// <summary>批次失败明细（fail_detail JSON 结构）。</summary>
        private class BillFailureDetail
        {
            public int ChargeItemId { get; set; }
            public int CycleId { get; set; }
            public List<BillFailureDto> Failures { get; set; }
        }
    }
}
