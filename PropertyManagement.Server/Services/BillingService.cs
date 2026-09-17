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
        /// <summary>CHG-v1.1.0-10：缴费对象候选单次返回上限（超出请用关键字缩小范围）。</summary>
        private const int BillObjectQueryLimit = 2000;

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
                // CHG-v1.1.0-16/17：缴费对象由表单选择；CHG-v1.1.0-17 起以 charge_object 字典为准（含自定义）
                ObjectType = ChargeObjectType.Property,
                ObjectCode = "property"
            };

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ResolveChargeObject(connection, request.MethodCode, request.ObjectType, request.ObjectCode,
                    out ChargeObjectType objectType, out string objectCode, out string objectName);
                item.ObjectType = objectType;
                item.ObjectCode = objectCode;
                item.ObjectName = objectName;

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
                ResolveChargeObject(connection, request.MethodCode, request.ObjectType, request.ObjectCode,
                    out ChargeObjectType objectType, out string objectCode, out string objectName);
                existing.ObjectType = objectType;
                existing.ObjectCode = objectCode;
                existing.ObjectName = objectName;
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
                // CHG-v1.1.0-17：仅自定义周期可按需删除；已被账单引用的周期一律拦截（软删账单也算引用）
                if (existing.CycleType != BillingCycleType.Custom)
                {
                    throw ApiException.ValidationFailed("系统内置周期不可删除（仅自定义周期支持删除）");
                }
                // CHG-v1.1.0-18：引用计数**不区分软删** —— t_bill.cycle_id 是物理外键（Foreign Keys=True），
                // 已软删的账单仍引用该周期；若只统计在用账单，DELETE 会触发 FOREIGN KEY constraint failed
                // （表现为「数据服务暂不可用，请稍后重试」）。此处按物理引用判定并给出可读提示。
                int used = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_bill WHERE cycle_id = @id", new { id }, transaction);
                if (used > 0)
                {
                    int active = connection.ExecuteScalar<int>(
                        "SELECT COUNT(1) FROM t_bill WHERE cycle_id = @id AND del_flag = 0", new { id }, transaction);
                    string scope = active > 0
                        ? "（其中在用账单 " + active + " 张）"
                        : "（均为已删除账单留痕）";
                    throw ApiException.ValidationFailed(
                        "该周期已被 " + used + " 张账单引用" + scope + "，不能删除；如需清理请保留周期或改用其他周期出账");
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
            // CHG-v1.1.0-10：缴费对象必须由用户显式选择；空集合＝校验失败，
            // 彻底移除旧口径「两个都空 ⇒ 全部对象」的隐式全量出账。
            bool hasPropertyScope = request.PropertyIds != null && request.PropertyIds.Count > 0;
            bool hasParkingScope = request.ParkingIds != null && request.ParkingIds.Count > 0;
            bool hasOwnerScope = request.OwnerIds != null && request.OwnerIds.Count > 0;
            bool hasCustomScope = request.CustomPayerNames != null &&
                                  request.CustomPayerNames.Any(x => !string.IsNullOrWhiteSpace(x));

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeItemDto chargeItem = _finance.GetChargeItem(connection, request.ChargeItemId);
                if (chargeItem == null)
                {
                    throw ApiException.NotFound("收费项目不存在或已停用");
                }
                // CHG-v1.1.0-18：空范围提示按缴费对象口径区分（自定义项目提示「填写名称」）
                if (!hasPropertyScope && !hasParkingScope && !hasOwnerScope && !hasCustomScope)
                {
                    throw ApiException.ValidationFailed(chargeItem.ObjectType == ChargeObjectType.Custom
                        ? "请至少填写一个缴费对象名称"
                        : "请至少选择一个缴费对象");
                }
                if (chargeItem.Status == 1)
                {
                    // BR-FIN-03：停用后不再生成新账单（历史账单不受影响，T4F-1-6 UC6）
                    throw ApiException.BadRequest("收费项目已停用，无法生成新账单");
                }
                BillingCycleDto cycle = _finance.GetCycle(connection, request.CycleId);
                if (cycle == null)
                {
                    throw ApiException.NotFound("计费周期不存在");
                }

                // CHG-v1.1.0-16：缴费对象类型必须与收费项目一致（此前办卡费等按次/一次性项目可对房产出账）
                ValidateScopeMatchesChargeItem(chargeItem, request);

                List<BillObjectCandidate> candidates = BuildCandidates(connection, request);
                if (candidates.Count == 0)
                {
                    throw ApiException.BadRequest("所选缴费对象已不存在，请重新选择缴费对象后生成");
                }

                var log = new BillGenerateLogDto
                {
                    Total = candidates.Count,
                    Success = 0,
                    Fail = 0,
                    ScopeSummary = BuildScopeSummary(candidates)
                };
                int logId = _finance.InsertBillGenerateLog(connection, transaction, log);

                var failures = new List<BillFailureDto>();
                int success = 0;
                DateTime dueAt = ComputeDueAt(cycle);

                foreach (BillObjectCandidate candidate in candidates)
                {
                    int? propertyId = candidate.Kind == BillObjectKind.Property ? (int?)candidate.Id : null;
                    int? parkingId = candidate.Kind == BillObjectKind.Parking ? (int?)candidate.Id : null;
                    int? ownerId = candidate.Kind == BillObjectKind.Owner ? (int?)candidate.Id : null;
                    // CHG-v1.1.0-18：自定义缴费对象（租户/广告商/外部单位）无基础信息档案，按手工填写的名称落账单
                    string payerName = candidate.Kind == BillObjectKind.Custom ? candidate.No : null;
                    // CHG-v1.1.0-21：失败行保留缴费对象信息（含自定义缴费对象名称），供「一键重推」按原对象重试
                    var failedRow = new BillFailureDto
                    {
                        PropertyId = propertyId,
                        ParkingId = parkingId,
                        OwnerId = ownerId,
                        PayerName = payerName,
                        No = candidate.No
                    };

                    // BR-INF-02（M7 BUG-002 裁定补校验）：缴费对象必须存在有效缴费人关系，否则记失败行不入库。
                    // CHG-v1.1.0-11：失败文案按对象类型区分（房产-业主 / 车位-业主），不再笼统写「房产-业主」。
                    // CHG-v1.1.0-18：自定义缴费对象无业主关系，跳过该校验（缴费人即用户手填名称）。
                    if (candidate.Kind != BillObjectKind.Custom &&
                        !_finance.HasValidOwnerRelation(connection, propertyId, parkingId, ownerId))
                    {
                        failedRow.Reason = DescribeNoOwnerReason(candidate.Kind);
                        failures.Add(failedRow);
                        continue;
                    }

                    // CHG-v1.1.0-18（负责人裁定）：移除「同对象同周期同项目」重复出账拦截 ——
                    // 同项目同周期同对象允许重复出账（补开/重开场景），不再入失败清单，
                    // 对应唯一索引已在 migration_043 中移除（BR-FIN-01 判重口径随之废止）。

                    // T4F-1-6：按计价方式计算金额（按建筑面积 = 单价 × 面积；面积缺失入失败清单）
                    decimal amount = ComputeBillAmount(chargeItem, candidate);
                    if (amount <= 0)
                    {
                        failedRow.Reason = DescribeMissingAreaReason(candidate.Kind);
                        failures.Add(failedRow);
                        continue;
                    }

                    _finance.InsertBill(connection, transaction, new BillDto
                    {
                        ChargeItemId = request.ChargeItemId,
                        PropertyId = propertyId,
                        ParkingId = parkingId,
                        OwnerId = ownerId,
                        PayerName = payerName,
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

        public BillGenerateLogDto RetryFailures(BillRetryRequest request, string operatorName = null, string ip = null)
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

            BillGenerateLogDto retryLog = GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = detail.ChargeItemId,
                CycleId = detail.CycleId,
                PropertyIds = detail.Failures.Where(f => f.PropertyId.HasValue).Select(f => f.PropertyId.Value).ToList(),
                ParkingIds = detail.Failures.Where(f => f.ParkingId.HasValue).Select(f => f.ParkingId.Value).ToList(),
                OwnerIds = detail.Failures.Where(f => f.OwnerId.HasValue).Select(f => f.OwnerId.Value).ToList(),
                // CHG-v1.1.0-21：自定义缴费对象失败行按原名称重推（否则重推会因「未填写缴费对象名称」被拒）
                CustomPayerNames = detail.Failures
                    .Where(f => !string.IsNullOrWhiteSpace(f.PayerName))
                    .Select(f => f.PayerName)
                    .ToList()
            });

            // CHG-v1.1.0-21：重推闭环 —— 源批次失败清单收敛为「本次仍未成功」的对象，
            // 并记录重推时间与成功户数；全部成功时源批次状态由「发布失败」转为「已重推」（不再计入失败卡片）。
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                BillGenerateLogDto newLog = _finance.GetBillGenerateLog(connection, retryLog.Id);
                BillFailureDetail remaining = ParseFailDetail(newLog == null ? null : newLog.FailDetail);
                List<BillFailureDto> remainingFailures = remaining == null || remaining.Failures == null
                    ? new List<BillFailureDto>()
                    : remaining.Failures;

                string remainingDetail = JsonConvert.SerializeObject(new BillFailureDetail
                {
                    ChargeItemId = detail.ChargeItemId,
                    CycleId = detail.CycleId,
                    RetriedCount = detail.Failures.Count - remainingFailures.Count,
                    RetriedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Failures = remainingFailures
                });
                _finance.MarkBatchRetried(connection, transaction, request.BatchId,
                    remainingFailures.Count, remainingDetail, detail.Failures.Count - remainingFailures.Count);
                transaction.Commit();
            }

            _audit.Write("BILL_BATCH_RETRY", "bill_generate_log", request.BatchId.ToString(),
                "失败对象重推：批次 " + request.BatchId + "，失败对象 " + detail.Failures.Count +
                " 个，本次成功 " + retryLog.Success + " 户、仍失败 " + retryLog.Fail + " 户",
                userName: operatorName, ip: ip, result: "成功");
            return retryLog;
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

        /// <summary>
        /// CHG-v1.1.0-17：确定收费项目的缴费对象（以 charge_object 字典为准）。
        /// 规则：①「按车位」计价 → 强制车位；②「按建筑面积」计价 → 强制房产（无面积无法计价）；
        /// ③字典编码 property/parking/owner → 系统固定三项（房产/车位/业主）；
        /// ④其他字典编码 → 自定义缴费对象（租户/广告商/外部单位等，ChargeObjectType.Custom）；
        /// ⑤未传字典编码时沿用旧口径（显式 ObjectType；仍缺省时按计价方式派生：按卡/一次性→业主，其余→房产）。
        /// </summary>
        private static void ResolveChargeObject(IDbConnection connection, string methodCode,
            ChargeObjectType? requested, string objectCode,
            out ChargeObjectType type, out string code, out string name)
        {
            name = null;
            string dictCode = string.IsNullOrWhiteSpace(objectCode) ? null : objectCode.Trim();
            if (dictCode != null)
            {
                string dictName = connection.ExecuteScalar<string>(
                    "SELECT item_name FROM t_dict_item WHERE type_code = 'charge_object' AND item_code = @code AND del_flag = 0",
                    new { code = dictCode });
                if (string.IsNullOrWhiteSpace(dictName))
                {
                    throw ApiException.ValidationFailed("缴费对象「" + dictCode + "」不存在或已删除，请重新选择");
                }

                code = dictCode;
                name = dictName.Trim();
                if (string.Equals(dictCode, "property", StringComparison.OrdinalIgnoreCase)) { type = ChargeObjectType.Property; }
                else if (string.Equals(dictCode, "parking", StringComparison.OrdinalIgnoreCase)) { type = ChargeObjectType.Parking; }
                else if (string.Equals(dictCode, "owner", StringComparison.OrdinalIgnoreCase)) { type = ChargeObjectType.Owner; }
                else { type = ChargeObjectType.Custom; }
            }
            else
            {
                // CHG-v1.1.0-25：未提供字典编码时按**计价方式**派生默认对象（与历史口径一致）：
                // 按车位→车位、按建筑面积→房产、按卡/一次性→业主、其余→房产。
                // 修复：第 17 轮改为「先取通用默认值再校验计价方式」后，「按车位」项目在未显式传对象时
                // 会被默认成房产并直接抛「缴费对象必须为车位」，导致接口调用方（非客户端）无法创建按车位项目。
                if (requested.HasValue)
                {
                    type = requested.Value;
                }
                else if (string.Equals(methodCode, "parking", StringComparison.OrdinalIgnoreCase))
                {
                    type = ChargeObjectType.Parking;
                }
                else if (string.Equals(methodCode, "area", StringComparison.OrdinalIgnoreCase))
                {
                    type = ChargeObjectType.Property;
                }
                else if (string.Equals(methodCode, "card", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(methodCode, "onetime", StringComparison.OrdinalIgnoreCase))
                {
                    type = ChargeObjectType.Owner;
                }
                else
                {
                    type = ChargeObjectType.Property;
                }
                if (type == ChargeObjectType.Custom)
                {
                    throw ApiException.ValidationFailed("自定义缴费对象必须指定具体的缴费对象项（charge_object 字典编码）");
                }
                code = type == ChargeObjectType.Parking ? "parking"
                    : (type == ChargeObjectType.Owner ? "owner" : "property");
                name = type == ChargeObjectType.Parking ? "车位"
                    : (type == ChargeObjectType.Owner ? "业主" : "房产");
            }

            if (string.Equals(methodCode, "parking", StringComparison.OrdinalIgnoreCase) &&
                type != ChargeObjectType.Parking)
            {
                throw ApiException.ValidationFailed("「按车位」计价的收费项目，缴费对象必须为车位");
            }
            if (string.Equals(methodCode, "area", StringComparison.OrdinalIgnoreCase) &&
                type != ChargeObjectType.Property)
            {
                throw ApiException.ValidationFailed("「按建筑面积」计价的收费项目，缴费对象必须为房产");
            }
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
            // CHG-v1.1.0-10：只按用户显式选择的对象 ID 过滤候选。
            // 「未选择 ⇒ 全部对象」的隐式口径已下线（由 GenerateBill 前置校验拦截）。
            var candidates = new List<BillObjectCandidate>();
            bool hasProperty = request.PropertyIds != null && request.PropertyIds.Count > 0;
            bool hasParking = request.ParkingIds != null && request.ParkingIds.Count > 0;
            bool hasOwner = request.OwnerIds != null && request.OwnerIds.Count > 0;

            // CHG-v1.1.0-18：自定义缴费对象 —— 用户手工填写的名称即候选（一行一张账单，无档案关联）
            if (request.CustomPayerNames != null)
            {
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in request.CustomPayerNames)
                {
                    string trimmed = name == null ? null : name.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || !seenNames.Add(trimmed)) { continue; }
                    candidates.Add(new BillObjectCandidate
                    {
                        Id = 0,
                        No = trimmed,
                        Kind = BillObjectKind.Custom
                    });
                }
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
            if (hasOwner)
            {
                foreach (BillObjectCandidate o in _finance.ListOwnerCandidates(connection))
                {
                    if (request.OwnerIds.Contains(o.Id))
                    {
                        o.Kind = BillObjectKind.Owner;
                        candidates.Add(o);
                    }
                }
            }
            return candidates;
        }

        /// <summary>CHG-v1.1.0-10／11：本次生成范围摘要（事后对账 / 失败重推核对）。</summary>
        /// <summary>
        /// CHG-v1.1.0-16：校验本次出账的缴费对象类型与收费项目一致。
        /// 口径：收费项目「缴费对象」＝本次允许出账的对象类型；不一致直接拒绝并提示（不再"先出草稿、发布才失败"）。
        /// </summary>
        private static void ValidateScopeMatchesChargeItem(ChargeItemDto chargeItem, BillGenerateRequest request)
        {
            bool hasProperty = request.PropertyIds != null && request.PropertyIds.Count > 0;
            bool hasParking = request.ParkingIds != null && request.ParkingIds.Count > 0;
            bool hasOwner = request.OwnerIds != null && request.OwnerIds.Count > 0;
            bool hasCustom = request.CustomPayerNames != null &&
                             request.CustomPayerNames.Any(x => !string.IsNullOrWhiteSpace(x));

            // CHG-v1.1.0-18：自定义缴费对象（租户/广告商/外部单位）无基础信息档案 ——
            // 由用户在生成账单表单中手工填写缴费对象名称后出账（不得与房产/车位/业主口径混用）。
            if (chargeItem.ObjectType == ChargeObjectType.Custom)
            {
                if (hasProperty || hasParking || hasOwner)
                {
                    throw ApiException.ValidationFailed(
                        "收费项目「" + chargeItem.Name + "」的缴费对象为自定义（" + ChargeObjectName(chargeItem) +
                        "），请改为手动填写缴费对象名称后生成");
                }
                if (!hasCustom)
                {
                    throw ApiException.ValidationFailed(
                        "收费项目「" + chargeItem.Name + "」的缴费对象为自定义（" + ChargeObjectName(chargeItem) +
                        "），请至少填写一个缴费对象名称");
                }
                return;
            }
            if (hasCustom)
            {
                throw ApiException.ValidationFailed(
                    "收费项目「" + chargeItem.Name + "」的缴费对象为「" + ChargeObjectName(chargeItem) +
                    "」，不支持手工填写缴费对象名称，请重新选择缴费对象");
            }

            string expected = chargeItem.ObjectType == ChargeObjectType.Parking ? "车位"
                : (chargeItem.ObjectType == ChargeObjectType.Owner ? "业主" : "房产");
            bool matched = chargeItem.ObjectType == ChargeObjectType.Parking ? hasParking && !hasProperty && !hasOwner
                : (chargeItem.ObjectType == ChargeObjectType.Owner ? hasOwner && !hasProperty && !hasParking
                    : hasProperty && !hasParking && !hasOwner);
            if (!matched)
            {
                throw ApiException.ValidationFailed(
                    "收费项目「" + chargeItem.Name + "」的缴费对象为「" + expected + "」，本次选择的缴费对象类型不一致，请重新选择");
            }
        }

        /// <summary>CHG-v1.1.0-17：缴费对象显示名（字典名优先，缺失时按对象类型回落）。</summary>
        internal static string ChargeObjectName(ChargeItemDto chargeItem)
        {
            if (chargeItem == null) { return string.Empty; }
            if (!string.IsNullOrWhiteSpace(chargeItem.ObjectName)) { return chargeItem.ObjectName.Trim(); }
            switch (chargeItem.ObjectType)
            {
                case ChargeObjectType.Parking: return "车位";
                case ChargeObjectType.Owner: return "业主";
                case ChargeObjectType.Custom: return "自定义";
                default: return "房产";
            }
        }

        private static string BuildScopeSummary(List<BillObjectCandidate> candidates)
        {
            int propertyCount = candidates.Count(x => x.Kind == BillObjectKind.Property);
            int parkingCount = candidates.Count(x => x.Kind == BillObjectKind.Parking);
            int ownerCount = candidates.Count(x => x.Kind == BillObjectKind.Owner);
            int customCount = candidates.Count(x => x.Kind == BillObjectKind.Custom);
            var parts = new List<string>();
            if (propertyCount > 0) { parts.Add("房产 " + propertyCount); }
            if (parkingCount > 0) { parts.Add("车位 " + parkingCount); }
            if (ownerCount > 0) { parts.Add("业主 " + ownerCount); }
            if (customCount > 0) { parts.Add("自定义缴费对象 " + customCount); }
            return "指定缴费对象 " + candidates.Count + " 个（" + string.Join(" / ", parts) + "）";
        }

        /// <summary>CHG-v1.1.0-11：缴费人缺失的失败原因按对象类型区分（房产-业主 / 车位-业主 / 业主体）。</summary>
        private static string DescribeNoOwnerReason(BillObjectKind kind)
        {
            switch (kind)
            {
                case BillObjectKind.Parking:
                    return "车位不存在有效「车位-业主」关系，请先在车位维护中绑定业主后再出账（BR-INF-02）";
                case BillObjectKind.Owner:
                    return "业主档案不存在或已下线，请先在业主档案中确认后再出账（BR-INF-02）";
                default:
                    return "房产不存在有效「房产-业主」关系，请先在业主-房产关系中绑定业主后再出账（BR-INF-02）";
            }
        }

        /// <summary>CHG-v1.1.0-11：按面积计费缺少面积时的失败原因按对象类型区分。</summary>
        private static string DescribeMissingAreaReason(BillObjectKind kind)
        {
            switch (kind)
            {
                case BillObjectKind.Parking:
                    return "车位缴费对象不适用「按建筑面积」计价，请改用按车位/一次性等计价方式";
                case BillObjectKind.Owner:
                    return "业主缴费对象不适用「按建筑面积」计价，请改选房产或改用按户/一次性等计价方式";
                default:
                    return "房产缺少建筑面积，无法按建筑面积计费";
            }
        }

        /// <summary>
        /// CHG-v1.1.0-10：生成账单「缴费对象」候选查询（只读）。
        /// 与收费项目、计费周期无耦合：仅按类型 + 关键字返回候选与隐藏数量。
        /// </summary>
        public BillObjectQueryResult QueryBillObjects(BillObjectQueryRequest request)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _finance.QueryBillObjects(connection,
                    request == null ? null : request.Kind,
                    request == null ? null : request.Keyword,
                    BillObjectQueryLimit);
            }
        }

        /// <summary>CHG-v1.1.0-10：草稿批次既有缴费对象（批次编辑回填）。</summary>
        public BillObjectSelectionDto GetBatchBillObjects(int batchId)
        {
            if (batchId <= 0)
            {
                throw ApiException.BadRequest("账单批次不能为空");
            }
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                BillObjectSelectionDto selection = _finance.GetBatchBillObjects(connection, batchId);
                return selection ?? new BillObjectSelectionDto
                {
                    PropertyIds = new List<int>(),
                    ParkingIds = new List<int>(),
                    OwnerIds = new List<int>()
                };
            }
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
            /// <summary>CHG-v1.1.0-21：最近一次重推成功户数与重推时间（重推闭环留痕）。</summary>
            public int RetriedCount { get; set; }
            public string RetriedAt { get; set; }
            public List<BillFailureDto> Failures { get; set; }
        }
    }
}
