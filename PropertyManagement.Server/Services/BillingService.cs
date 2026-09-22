using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Newtonsoft.Json;
using PropertyManagement.Contract.Common;
using Dapper;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Domain;
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
        private readonly IChargeStandardRepository _standards;
        private readonly AuditService _audit;

        public BillingService()
            : this(new SqliteConnectionFactory(), new SqlFinanceRepository(), new AuditService(), new SqlChargeStandardRepository())
        {
        }

        public BillingService(IDbConnectionFactory connectionFactory, IFinanceRepository finance, AuditService audit,
            IChargeStandardRepository standards = null)
        {
            _connectionFactory = connectionFactory;
            _finance = finance;
            _audit = audit;
            _standards = standards ?? new SqlChargeStandardRepository();
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
            if (request == null)
            {
                throw ApiException.BadRequest("收费项目不能为空");
            }

            // CHG-v1.1.2-26：绑定收费标准的项目 —— 名称 / 类别 / 单价 / 单位 / 公式全部由价目表带出，
            // 表单只需「选收费标准 + 选缴费对象」，不再逐项维护价格。
            if (request.StandardId.HasValue && request.StandardId.Value > 0)
            {
                return CreateChargeItemByStandard(request, operatorName, ip);
            }

            if (string.IsNullOrWhiteSpace(request.Name))
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
            string formula = NormalizeFormula(request.Formula);

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
                Formula = formula,
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

        /// <summary>
        /// CHG-v1.1.2-26：按收费标准新增收费项目（3 步表单口径）。
        /// 价格 / 单位 / 公式 / 周期取自收费标准下第一条启用规格（列表与旧接口按此冗余展示）。
        /// </summary>
        private ChargeItemDto CreateChargeItemByStandard(ChargeItemRequest request, string operatorName, string ip)
        {
            var item = new ChargeItemDto();
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeStandardDto standard = _standards.GetStandard(connection, request.StandardId.Value);
                if (standard == null) { throw ApiException.NotFound("收费标准不存在或已删除"); }
                if (standard.Status == 1) { throw ApiException.ValidationFailed("收费标准已停用，不能新增收费项目"); }

                ChargeStandardSpecDto spec = (standard.Specs ?? new List<ChargeStandardSpecDto>())
                    .FirstOrDefault(x => x.Status == 0);
                if (spec == null)
                {
                    throw ApiException.ValidationFailed("收费标准「" + standard.Name + "」尚未维护启用中的规格，请先在价目表补充");
                }

                ResolveChargeObject(connection, null, request.ObjectType, request.ObjectCode,
                    out ChargeObjectType objectType, out string objectCode, out string objectName);
                ValidatePriceOverride(request.AllowPriceOverride, objectType, spec);

                item.Name = standard.Name.Trim();
                item.Category = standard.Category.Trim();
                item.PayMode = request.PayMode;
                item.UnitPrice = spec.UnitPrice;
                item.CycleType = request.CycleType;
                item.Status = request.Status ?? 0;
                item.MethodCode = string.IsNullOrWhiteSpace(request.MethodCode) ? "custom" : request.MethodCode.Trim();
                item.MethodName = string.IsNullOrWhiteSpace(request.MethodName) ? "价目表" : request.MethodName.Trim();
                item.PriceUnit = string.IsNullOrWhiteSpace(spec.PriceUnit)
                    ? ChargeSpecMatcher.DerivePriceUnit(standard.Variables, spec.Formula)
                    : spec.PriceUnit;
                item.CycleName = spec.CycleName;
                item.Formula = ChargeSpecMatcher.RewriteForEvaluation(spec.Formula, standard.Variables);
                item.StandardId = standard.Id;
                item.StandardName = standard.Name.Trim();
                item.AllowPriceOverride = request.AllowPriceOverride;
                item.ObjectType = objectType;
                item.ObjectCode = objectCode;
                item.ObjectName = objectName;
                // 列表展示口径（规格数 / 启用规格价格概览）与 /charge-items 查询保持一致
                item.SpecCount = (standard.Specs ?? new List<ChargeStandardSpecDto>()).Count;
                // FIX-v1.1.2-03：默认单价列只给一条（非兜底优先、id 升序），另带启用规格数供「起」标注
                List<ChargeStandardSpecDto> enabledSpecs = (standard.Specs ?? new List<ChargeStandardSpecDto>())
                    .Where(x => x.Status == 0)
                    .OrderBy(x => x.IsFallback)
                    .ThenBy(x => x.Id)
                    .ToList();
                item.EnabledSpecCount = enabledSpecs.Count;
                item.SpecPriceText = enabledSpecs.Count == 0
                    ? null
                    : enabledSpecs[0].SpecName + " " + enabledSpecs[0].UnitPrice.ToString("0.00");

                ChargeItemDto sameName = _finance.GetChargeItemByName(connection, item.Name);
                if (sameName != null)
                {
                    throw ApiException.ValidationFailed("同名收费项目已存在（同一收费标准无需重复新增）");
                }

                item.Id = _finance.InsertChargeItem(connection, transaction, item);
                transaction.Commit();
            }

            _audit.Write("CHARGE_ITEM_CREATE", "charge_item", item.Id.ToString(),
                "新增收费项目：" + item.Name + "（收费标准 " + item.StandardId + "，缴费对象 " + ChargeObjectName(item) +
                "，出账可改价 " + (item.AllowPriceOverride ? "是" : "否") + "）",
                userName: operatorName, ip: ip, result: "Success");
            return item;
        }

        /// <summary>出账可改价仅对「一次性」收费标准或「自定义缴费对象」项目开放（负责人裁定 ⑤）。</summary>
        private static void ValidatePriceOverride(bool allowPriceOverride, ChargeObjectType objectType, ChargeStandardSpecDto spec)
        {
            if (!allowPriceOverride) { return; }
            bool oneTime = spec != null && !string.IsNullOrWhiteSpace(spec.CycleName) &&
                           spec.CycleName.Trim().Contains("一次性");
            if (!oneTime && objectType != ChargeObjectType.Custom)
            {
                throw ApiException.ValidationFailed(
                    "「出账时可改价」仅对一次性收费标准或自定义缴费对象开放；周期性费用必须按价目表定价");
            }
        }

        public ChargeItemDto UpdateChargeItem(int id, ChargeItemRequest request, string operatorName = null, string ip = null)
        {
            if (request == null)
            {
                throw ApiException.BadRequest("收费项目不能为空");
            }

            // CHG-v1.1.2-26：已绑定收费标准的项目 —— 只允许维护「缴费对象 / 启用状态 / 出账可改价」，
            // 价格一律回到「价目表」页签（停用旧规格 + 新增新规格）修改，避免两处维护。
            if (request.StandardId.HasValue && request.StandardId.Value > 0)
            {
                return UpdateChargeItemByStandard(id, request, operatorName, ip);
            }

            if (string.IsNullOrWhiteSpace(request.Name))
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
            string formula = NormalizeFormula(request.Formula);

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
                existing.Formula = formula;
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

        /// <summary>CHG-v1.1.2-26：编辑绑定收费标准的收费项目（价格只读，随价目表）。</summary>
        private ChargeItemDto UpdateChargeItemByStandard(int id, ChargeItemRequest request, string operatorName, string ip)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeItemDto existing = _finance.GetChargeItem(connection, id);
                if (existing == null) { throw ApiException.NotFound("收费项目不存在或已删除"); }

                ChargeStandardDto standard = _standards.GetStandard(connection, request.StandardId.Value);
                if (standard == null) { throw ApiException.NotFound("收费标准不存在或已删除"); }
                ChargeStandardSpecDto spec = (standard.Specs ?? new List<ChargeStandardSpecDto>())
                    .FirstOrDefault(x => x.Status == 0);
                if (spec == null)
                {
                    throw ApiException.ValidationFailed("收费标准「" + standard.Name + "」尚未维护启用中的规格，请先在价目表补充");
                }

                ResolveChargeObject(connection, null, request.ObjectType, request.ObjectCode,
                    out ChargeObjectType objectType, out string objectCode, out string objectName);
                ValidatePriceOverride(request.AllowPriceOverride, objectType, spec);

                existing.StandardId = standard.Id;
                existing.Name = standard.Name.Trim();
                existing.Category = standard.Category.Trim();
                existing.UnitPrice = spec.UnitPrice;
                existing.PriceUnit = string.IsNullOrWhiteSpace(spec.PriceUnit)
                    ? ChargeSpecMatcher.DerivePriceUnit(standard.Variables, spec.Formula)
                    : spec.PriceUnit;
                existing.CycleName = spec.CycleName;
                existing.Formula = ChargeSpecMatcher.RewriteForEvaluation(spec.Formula, standard.Variables);
                existing.AllowPriceOverride = request.AllowPriceOverride;
                existing.Status = request.Status ?? existing.Status;
                existing.ObjectType = objectType;
                existing.ObjectCode = objectCode;
                existing.ObjectName = objectName;
                _finance.UpdateChargeItem(connection, transaction, existing);
                transaction.Commit();

                _audit.Write("CHARGE_ITEM_UPDATE", "charge_item", id.ToString(),
                    "修改收费项目：" + existing.Name + "（收费标准 " + standard.Id + "，缴费对象 " + ChargeObjectName(existing) + "）",
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

                // CHG-v1.1.2-35：已被账单引用的收费项目不得删除（否则账单 / 收款 / 欠费台账失去收费项目，
                // 历史账目无法追溯）。与「规格 / 收费标准」同口径：能拦就拦，并提示改用「停用」。
                int bills = _finance.CountChargeItemBills(connection, id);
                if (bills > 0)
                {
                    throw ApiException.ValidationFailed(
                        "该收费项目已被 " + bills + " 张账单引用，不能删除；如需停止使用请改用「停用」");
                }

                _finance.SoftDeleteChargeItem(connection, transaction, id);
                transaction.Commit();

                _audit.Write("CHARGE_ITEM_DELETE", "charge_item", id.ToString(),
                    "删除收费项目：" + existing.Name + "（软删留痕；已出账单与流水不受影响）",
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
            // CHG-v1.1.2-26：自定义缴费对象的范围判定同时兼容新口径 CustomPayers（含规格与计量参数）
            bool hasCustomScope = CustomNamesOf(request).Count > 0;

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

                // CHG-v1.1.2-26：收费项目绑定的收费标准（价目表）。为空 = 未绑定标准的历史项目，走旧口径。
                ChargeStandardDto standard = chargeItem.StandardId.HasValue
                    ? _standards.GetStandard(connection, chargeItem.StandardId.Value)
                    : null;

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

                    // CHG-v1.1.2-50：先把「本行出账输入」（手选规格 / 手填计量 / 改价单价）落到失败行，
                    // 使失败行重推（FL-FIN-01）按用户原输入重出，而不是回到价目表默认价。
                    BillCustomPayerRequest payerInput = PayerInputOf(request, candidate.No);
                    IDictionary<int, decimal> measures = payerInput != null
                        ? payerInput.Measures
                        : ObjectMeasuresOf(request, candidate);
                    int? explicitSpecId = payerInput == null ? null : payerInput.SpecId;
                    // CHG-v1.2.0-12：档案对象（房产/车位/业主）也支持逐行手选规格；未选＝自动匹配（原口径）
                    if (payerInput == null) { explicitSpecId = ObjectSpecIdOf(request, candidate); }
                    // CHG-v1.1.2-50：出账时改价 —— 只取「本行」提交的单价，未提交＝按价目表规格单价计价
                    decimal? priceOverride = payerInput != null
                        ? payerInput.UnitPriceOverride
                        : ObjectPriceOverrideOf(request, candidate);
                    failedRow.SpecId = explicitSpecId;
                    failedRow.UnitPriceOverride = priceOverride;
                    failedRow.Measures = measures == null ? null : new Dictionary<int, decimal>(measures);

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

                    // T4F-1-6 / CHG-v1.1.2-06：计算金额 —— 配置了自定义公式按公式；否则按内置计价方式。
                    // 变量缺失（面积未维护）或公式非法 → 入失败清单并点名原因，不静默出错。
                    // CHG-v1.1.2-26：绑定收费标准时按「规格匹配 + 计量变量」计价，并落账单快照。
                    decimal amount;
                    string amountReason;
                    int? specId;
                    string measureSnapshot;
                    decimal? unitPriceSnapshot;
                    string formulaSnapshot;
                    // 本行输入（计量参数 / 改价单价）已在上方统一解析，此处直接计价
                    ValidateUnitPriceOverride(chargeItem, priceOverride);
                    if (!TryComputeAmount(standard, chargeItem, candidate, cycle, measures, explicitSpecId, priceOverride,
                            out amount, out amountReason, out specId, out measureSnapshot,
                            out unitPriceSnapshot, out formulaSnapshot))
                    {
                        failedRow.Reason = amountReason;
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
                        DelFlag = false,
                        ChargeSpecId = specId,
                        MeasureSnapshot = measureSnapshot,
                        UnitPriceSnapshot = unitPriceSnapshot,
                        FormulaSnapshot = formulaSnapshot
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
                    "发布账单批次，草稿转待缴",
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

            // CHG-v1.1.2-50：重推按原失败行留痕的输入（手选规格 / 手填计量 / 改价单价）重出，
            // 不再只带对象 ID 与名称 —— 否则重推会丢掉用户填的规格与改后单价，按价目表默认价入账。
            List<BillObjectMeasureRequest> retryObjectMeasures = detail.Failures
                .Where(f => f.PropertyId.HasValue || f.ParkingId.HasValue || f.OwnerId.HasValue)
                .Where(f => (f.Measures != null && f.Measures.Count > 0) || f.UnitPriceOverride.HasValue)
                .Select(f => new BillObjectMeasureRequest
                {
                    Kind = f.ParkingId.HasValue ? "parking" : (f.OwnerId.HasValue ? "owner" : "property"),
                    ObjectId = f.ParkingId ?? f.OwnerId ?? f.PropertyId ?? 0,
                    Measures = f.Measures,
                    UnitPriceOverride = f.UnitPriceOverride
                })
                .Where(x => x.ObjectId > 0)
                .ToList();
            List<BillCustomPayerRequest> retryCustomPayers = detail.Failures
                .Where(f => !string.IsNullOrWhiteSpace(f.PayerName))
                .Select(f => new BillCustomPayerRequest
                {
                    PayerName = f.PayerName,
                    SpecId = f.SpecId,
                    Measures = f.Measures,
                    UnitPriceOverride = f.UnitPriceOverride
                })
                .ToList();

            BillGenerateLogDto retryLog = GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = detail.ChargeItemId,
                CycleId = detail.CycleId,
                PropertyIds = detail.Failures.Where(f => f.PropertyId.HasValue).Select(f => f.PropertyId.Value).ToList(),
                ParkingIds = detail.Failures.Where(f => f.ParkingId.HasValue).Select(f => f.ParkingId.Value).ToList(),
                OwnerIds = detail.Failures.Where(f => f.OwnerId.HasValue).Select(f => f.OwnerId.Value).ToList(),
                // CHG-v1.1.0-21：自定义缴费对象失败行按原名称重推（否则重推会因「未填写缴费对象名称」被拒）
                CustomPayers = retryCustomPayers.Count > 0 ? retryCustomPayers : null,
                CustomPayerNames = retryCustomPayers.Count > 0
                    ? null
                    : detail.Failures.Where(f => !string.IsNullOrWhiteSpace(f.PayerName)).Select(f => f.PayerName).ToList(),
                ObjectMeasures = retryObjectMeasures.Count > 0 ? retryObjectMeasures : null
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

            // CHG-v1.1.2-06（负责人 2026-09-19 裁定 ④）：原先「按车位 → 必须车位」「按建筑面积 → 必须房产」
            // 的硬拦已取消 —— 计价口径改由**自定义公式**决定，计价方式名词不再有权否决缴费对象。
            // 兼容性：出账时仍按「缴费对象类型须与收费项目一致」校验（ValidateScopeMatchesChargeItem），
            // 因此「办卡费（业主）对房产出账」这类真正的错配依旧被拦下；
            // 客户端改为在表单内给出提示建议（按建筑面积建议选房产；对车位/业主要按面积计费请自写公式）。
        }

        /// <summary>T4F-1-6：内置计价方式默认单价单位（自定义由前端传入）。</summary>
        private static string ResolveDefaultPriceUnit(string methodCode)
        {
            if (string.Equals(methodCode, "area", StringComparison.OrdinalIgnoreCase)) { return "㎡"; }
            if (string.Equals(methodCode, "parking", StringComparison.OrdinalIgnoreCase)) { return "车位"; }
            if (string.Equals(methodCode, "card", StringComparison.OrdinalIgnoreCase)) { return "张"; }
            return "户";
        }

        /// <summary>
        /// CHG-v1.1.2-06：规范化并校验自定义计价公式。
        /// 为空 → 返回 null（沿用内置计价方式口径）；非空 → 语法与变量名必须合法，否则拒绝保存。
        /// </summary>
        private static string NormalizeFormula(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula)) { return null; }
            string trimmed = formula.Trim();
            string error;
            if (!ChargeFormula.TryValidate(trimmed, out error))
            {
                throw ApiException.ValidationFailed("计价公式不合法：" + error);
            }
            return trimmed;
        }

        /// <summary>
        /// CHG-v1.1.2-26：按「价目表规格 + 计量变量」计价；未绑定收费标准时回落旧口径（TryComputeBillAmount）。
        /// 同时产出账单快照（规格 / 计量取值 / 单价 / 公式），使金额可事后复算。
        /// </summary>
        private static bool TryComputeAmount(ChargeStandardDto standard, ChargeItemDto chargeItem,
            BillObjectCandidate candidate, BillingCycleDto cycle, IDictionary<int, decimal> measures,
            int? explicitSpecId, decimal? unitPriceOverride, out decimal amount, out string reason, out int? specId,
            out string measureSnapshot, out decimal? unitPriceSnapshot, out string formulaSnapshot)
        {
            amount = 0m;
            reason = null;
            specId = null;
            measureSnapshot = null;
            unitPriceSnapshot = null;
            formulaSnapshot = null;

            if (standard == null || standard.Specs == null || standard.Specs.Count == 0)
            {
                return TryComputeBillAmount(chargeItem, candidate, cycle, out amount, out reason);
            }

            ChargeStandardSpecDto spec = ChargeSpecMatcher.Match(standard.Specs, candidate, explicitSpecId, out reason);
            if (spec == null) { return false; }

            // CHG-v1.1.2-50：出账时改价 —— 本次出账的「单价」用改后价，规格本身的价目表单价不动。
            decimal effectivePrice = unitPriceOverride ?? spec.UnitPrice;

            // 口径：计量变量停用后，引用它的收费标准不得继续出新账单（参数字典停用提示语承诺的行为）。
            // 逐行给出可读原因，不静默沿用旧值。
            List<int> referenced = ChargeSpecMatcher.ParseTokens(spec.Formula);
            if (referenced.Count > 0)
            {
                foreach (int variableId in referenced)
                {
                    ChargeVariableDto variable = (standard.Variables ?? new List<ChargeVariableDto>())
                        .FirstOrDefault(x => x.Id == variableId);
                    // CHG-v1.1.2-49：公式引用了「未绑定到本收费标准」的变量时，给出可读原因与解法 ——
                    // 原实现会走到公式求值报「存在无法识别的字符「{」」，用户看不懂也不知道怎么修。
                    if (variable == null)
                    {
                        reason = "计算规则引用了未绑定到本收费标准的计量变量（变量 ID " + variableId +
                                 "），请到「收费项目维护 → 价目表 → 变量区」勾选该变量并保存后再出账";
                        return false;
                    }
                    if (variable != null && variable.Status != 0)
                    {
                        reason = "计价变量「" + variable.VarName + "」已停用，请先在参数字典中启用该变量，或调整该收费标准的计算规则";
                        return false;
                    }
                }
            }

            IList<string> extraNames;
            IDictionary<string, decimal> context = ChargeSpecMatcher.BuildContext(
                spec, standard.Variables, candidate, cycle, measures, out extraNames);
            context[ChargeSpecMatcher.PriceVariable] = effectivePrice;
            string formula = ChargeSpecMatcher.RewriteForEvaluation(spec.Formula, standard.Variables);

            if (string.IsNullOrWhiteSpace(formula))
            {
                amount = Math.Round(effectivePrice, 2, MidpointRounding.AwayFromZero);
            }
            else
            {
                decimal computed;
                string error;
                if (!ChargeFormula.TryEvaluate(formula, context, extraNames, out computed, out error))
                {
                    reason = "按价目表公式计价失败：" + error;
                    return false;
                }
                if (computed <= 0m)
                {
                    reason = "按价目表公式计价结果为 0，请检查单价与公式设置";
                    return false;
                }
                amount = computed;
            }

            specId = spec.Id;
            unitPriceSnapshot = effectivePrice;
            formulaSnapshot = string.IsNullOrWhiteSpace(formula) ? null : formula;
            measureSnapshot = JsonConvert.SerializeObject(context);
            return true;
        }

        /// <summary>
        /// CHG-v1.1.2-26：出账试算（只读，不落库）—— 账单工作台「出账预演」按同口径给出规格 / 单价 / 金额。
        /// </summary>
        public BillPreviewResult PreviewBills(BillPreviewRequest request)
        {
            if (request == null || request.ChargeItemId <= 0)
            {
                throw ApiException.BadRequest("收费项目必须选择");
            }
            var result = new BillPreviewResult { Rows = new List<BillPreviewRowDto>() };

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                ChargeItemDto chargeItem = _finance.GetChargeItem(connection, request.ChargeItemId);
                if (chargeItem == null) { throw ApiException.NotFound("收费项目不存在或已停用"); }
                BillingCycleDto cycle = request.CycleId > 0 ? _finance.GetCycle(connection, request.CycleId) : null;
                ChargeStandardDto standard = chargeItem.StandardId.HasValue
                    ? _standards.GetStandard(connection, chargeItem.StandardId.Value)
                    : null;

                var generate = new BillGenerateRequest
                {
                    ChargeItemId = request.ChargeItemId,
                    CycleId = request.CycleId,
                    PropertyIds = request.PropertyIds,
                    ParkingIds = request.ParkingIds,
                    OwnerIds = request.OwnerIds,
                    CustomPayers = request.CustomPayers,
                    ObjectMeasures = request.ObjectMeasures
                };
                List<BillObjectCandidate> candidates = BuildCandidates(connection, generate);

                foreach (BillObjectCandidate candidate in candidates)
                {
                    BillCustomPayerRequest payerInput = PayerInputOf(generate, candidate.No);
                    IDictionary<int, decimal> measures = payerInput == null ? null : payerInput.Measures;
                    int? explicitSpecId = payerInput == null ? null : payerInput.SpecId;
                    if (payerInput == null)
                    {
                        // CHG-v1.1.2-34：档案对象的「手填」计量参数（与生成账单同口径，保证试算=结果）
                        measures = ObjectMeasuresOf(generate, candidate);
                    }
                    // CHG-v1.2.0-12：档案对象手选规格（试算与生成同口径，保证「所见即所出」）
                    if (payerInput == null) { explicitSpecId = ObjectSpecIdOf(generate, candidate); }
                    // CHG-v1.1.2-50：出账时改价（试算与生成同口径，保证「所见即所出」）
                    decimal? priceOverride = payerInput != null
                        ? payerInput.UnitPriceOverride
                        : ObjectPriceOverrideOf(generate, candidate);
                    ValidateUnitPriceOverride(chargeItem, priceOverride);

                    decimal amount;
                    string reason;
                    int? specId;
                    string measureSnapshot;
                    decimal? unitPriceSnapshot;
                    string formulaSnapshot;
                    bool ok = TryComputeAmount(standard, chargeItem, candidate, cycle, measures, explicitSpecId, priceOverride,
                        out amount, out reason, out specId, out measureSnapshot, out unitPriceSnapshot, out formulaSnapshot);

                    ChargeStandardSpecDto spec = specId.HasValue && standard != null
                        ? (standard.Specs ?? new List<ChargeStandardSpecDto>()).FirstOrDefault(x => x.Id == specId.Value)
                        : null;
                    var row = new BillPreviewRowDto
                    {
                        ObjectKey = candidate.Kind + ":" + candidate.Id + ":" + (candidate.No ?? string.Empty),
                        ObjectText = DescribeCandidateText(candidate),
                        SpecName = spec == null ? null : spec.SpecName,
                        UnitPrice = spec == null ? 0m : (unitPriceSnapshot ?? spec.UnitPrice),
                        UnitPriceOverridden = spec != null && priceOverride.HasValue,
                        PriceUnit = spec == null ? null
                            : (string.IsNullOrWhiteSpace(spec.PriceUnit)
                                ? ChargeSpecMatcher.DerivePriceUnit(standard.Variables, spec.Formula)
                                : spec.PriceUnit),
                        Formula = formulaSnapshot,
                        MeasureText = BuildMeasureText(measureSnapshot, spec),
                        Amount = ok ? amount : 0m,
                        Matched = ok,
                        IsFallback = spec != null && spec.IsFallback,
                        Reason = ok ? null : reason
                    };
                    if (ok)
                    {
                        result.MatchedCount++;
                        result.TotalAmount += amount;
                        if (row.IsFallback) { result.FallbackCount++; }
                    }
                    else
                    {
                        result.FailedCount++;
                    }
                    result.Rows.Add(row);
                }
            }
            return result;
        }

        private static string DescribeCandidateText(BillObjectCandidate candidate)
        {
            switch (candidate.Kind)
            {
                case BillObjectKind.Property:
                    return string.IsNullOrWhiteSpace(candidate.BuildingNo)
                        ? candidate.No
                        : candidate.BuildingNo + " " + candidate.No;
                case BillObjectKind.Parking:
                    return "车位 " + candidate.No;
                case BillObjectKind.Owner:
                    // CHG-v1.1.2-49：业主口径补「楼栋 单元 房号」（名下主房产），同名业主可区分
                    return "业主 " + candidate.No +
                           (string.IsNullOrWhiteSpace(candidate.Address) ? "（未绑定房产）" : " · " + candidate.Address.Trim());
                default:
                    return candidate.No;
            }
        }

        /// <summary>
        /// CHG-v1.1.2-47：计量取值摘要（如「数量 1」「面积 24 · 月数 3」），供出账预演/生成账单表格的「计量规则」列展示。
        /// 口径：**只显示所选规格计算规则真正引用的计量变量**（按公式引用顺序）——
        /// 计算上下文里为求值准备的周期派生项（月数 / 年数 / 天数）与默认数量不再一并显示，
        /// 避免「单价 × 数量」这类项目被显示成「数量 · 月数 · 年数 · 天数」（计算无误但显示与语义失真）。
        /// 空公式按既有口径视为「单价 × 数量」；无规格信息时（老数据）回落旧口径（快照顺序，排除单价）。
        /// </summary>
        private static string BuildMeasureText(string measureSnapshot, ChargeStandardSpecDto spec)
        {
            if (string.IsNullOrWhiteSpace(measureSnapshot)) { return null; }
            try
            {
                var map = JsonConvert.DeserializeObject<Dictionary<string, decimal>>(measureSnapshot);
                if (map == null) { return null; }
                var parts = new List<string>();

                var usedNames = new List<string>();
                if (spec != null)
                {
                    if (spec.FormulaVars != null && spec.FormulaVars.Count > 0)
                    {
                        foreach (ChargeFormulaVarDto variable in spec.FormulaVars)
                        {
                            string name = (variable.Name ?? string.Empty).Trim();
                            if (name.Length > 0 && !usedNames.Contains(name)) { usedNames.Add(name); }
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(spec.Formula))
                    {
                        // 空公式 = 单价 × 数量（ChargeStandardSpecRequest.Formula 约定）
                        usedNames.Add("数量");
                    }
                }

                if (usedNames.Count > 0)
                {
                    foreach (string name in usedNames)
                    {
                        if (string.Equals(name, ChargeSpecMatcher.PriceVariable, StringComparison.Ordinal)) { continue; }
                        decimal value;
                        if (!map.TryGetValue(name, out value)) { continue; }
                        parts.Add(name + " " + value.ToString("0.##"));
                    }
                    return parts.Count == 0 ? null : string.Join(" · ", parts);
                }

                // 兜底（无规格 / 老数据）：按快照顺序展示，仍排除单价
                foreach (KeyValuePair<string, decimal> pair in map)
                {
                    if (string.Equals(pair.Key, ChargeSpecMatcher.PriceVariable, StringComparison.Ordinal)) { continue; }
                    parts.Add(pair.Key + " " + pair.Value.ToString("0.##"));
                }
                return string.Join(" · ", parts);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// T4F-1-6 / CHG-v1.1.2-06：计算账单金额。
        /// ① 收费项目配置了自定义公式 → 按公式计算（变量：单价 / 面积 / 月数 / 天数 / 数量）；
        /// ② 未配置公式 → 沿用内置口径（按建筑面积 = 单价 × 建筑面积；其余 = 单价）。
        /// 返回 false 时 reason 可直接作为失败明细文案。
        /// </summary>
        private static bool TryComputeBillAmount(ChargeItemDto chargeItem, BillObjectCandidate candidate,
            BillingCycleDto cycle, out decimal amount, out string reason)
        {
            amount = 0m;
            reason = null;

            if (!string.IsNullOrWhiteSpace(chargeItem.Formula))
            {
                IDictionary<string, decimal> context = BuildFormulaContext(chargeItem, candidate, cycle);
                decimal computed;
                string error;
                if (!ChargeFormula.TryEvaluate(chargeItem.Formula, context, out computed, out error))
                {
                    reason = "按自定义公式计价失败：" + error;
                    return false;
                }
                if (computed <= 0m)
                {
                    reason = "按自定义公式计价结果为 0，请检查公式与单价设置";
                    return false;
                }
                amount = computed;
                return true;
            }

            if (string.Equals(chargeItem.MethodCode, "area", StringComparison.OrdinalIgnoreCase))
            {
                decimal area = candidate.Area ?? 0m;
                if (area <= 0)
                {
                    reason = DescribeMissingAreaReason(candidate.Kind);
                    return false;
                }
                amount = Math.Round(chargeItem.UnitPrice * area, 2, MidpointRounding.AwayFromZero);
                return true;
            }

            amount = chargeItem.UnitPrice;
            return true;
        }

        /// <summary>
        /// CHG-v1.1.2-06：构建公式变量上下文。
        /// 面积仅在该缴费对象确有建筑面积时注入（缺失 → 公式引用「面积」将得到明确的中文失败原因）；
        /// 月数按「含首尾自然月」计数（裁定 ⑦：1/1–12/31 = 12），天数按「含首尾」计数。
        /// </summary>
        private static IDictionary<string, decimal> BuildFormulaContext(ChargeItemDto chargeItem,
            BillObjectCandidate candidate, BillingCycleDto cycle)
        {
            var context = new Dictionary<string, decimal> { { "数量", 1m }, { "单价", chargeItem.UnitPrice } };
            if (candidate.Area.HasValue && candidate.Area.Value > 0m)
            {
                context["面积"] = candidate.Area.Value;
            }
            if (cycle != null && cycle.EndDate >= cycle.StartDate)
            {
                context["月数"] = CountInclusiveMonths(cycle.StartDate, cycle.EndDate);
                context["天数"] = (cycle.EndDate.Date - cycle.StartDate.Date).Days + 1;
            }
            return context;
        }

        /// <summary>含首尾的自然月数（2026-01-01 ~ 2026-12-31 = 12；跨月不足一月按 1 计）。</summary>
        private static int CountInclusiveMonths(DateTime start, DateTime end)
        {
            int months = (end.Year - start.Year) * 12 + (end.Month - start.Month) + 1;
            return months < 1 ? 1 : months;
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
            List<string> customNames = CustomNamesOf(request);
            if (customNames.Count > 0)
            {
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in customNames)
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
            bool hasCustom = CustomNamesOf(request).Count > 0;

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

        /// <summary>
        /// CHG-v1.1.2-26：本次出账的自定义缴费对象名称集合（新口径 CustomPayers 优先，兼容旧口径 CustomPayerNames）。
        /// </summary>
        private static List<string> CustomNamesOf(BillGenerateRequest request)
        {
            if (request == null) { return new List<string>(); }
            if (request.CustomPayers != null && request.CustomPayers.Count > 0)
            {
                return request.CustomPayers
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.PayerName))
                    .Select(x => x.PayerName.Trim())
                    .ToList();
            }
            return request.CustomPayerNames == null
                ? new List<string>()
                : request.CustomPayerNames.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        }

        /// <summary>CHG-v1.1.2-26：按名称取该自定义缴费对象行的计量取值 / 手选规格。</summary>
        private static BillCustomPayerRequest PayerInputOf(BillGenerateRequest request, string payerName)
        {
            if (request == null || request.CustomPayers == null || string.IsNullOrWhiteSpace(payerName)) { return null; }
            return request.CustomPayers.FirstOrDefault(x => x != null &&
                string.Equals((x.PayerName ?? string.Empty).Trim(), payerName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// CHG-v1.1.2-34：档案对象的出账计量参数（价目表公式中的「手填」变量在房产 / 车位 / 业主下无取值来源）。
        /// 键为「对象类型 + 对象 ID」，避免同名对象（同楼栋同名房号 / 同名业主）串味。
        /// </summary>
        private static IDictionary<int, decimal> ObjectMeasuresOf(BillGenerateRequest request, BillObjectCandidate candidate)
        {
            BillObjectMeasureRequest hit = FindObjectInput(request, candidate);
            if (hit == null || hit.Measures == null || hit.Measures.Count == 0) { return null; }
            return hit.Measures;
        }

        /// <summary>
        /// CHG-v1.1.2-50：档案对象（房产 / 车位 / 业主）的出账改价 —— 与计量参数同键（对象类型 + 对象 ID）。
        /// </summary>
        private static int? ObjectSpecIdOf(BillGenerateRequest request, BillObjectCandidate candidate)
        {
            BillObjectMeasureRequest hit = FindObjectInput(request, candidate);
            return hit == null ? (int?)null : (hit.SpecId.HasValue && hit.SpecId.Value > 0 ? hit.SpecId : null);
        }

        /// <summary>按「对象类型 + 对象 ID」取本次出账提交的行内输入（手选规格 / 手填计量 / 改价共用）。</summary>
        private static BillObjectMeasureRequest FindObjectInput(BillGenerateRequest request, BillObjectCandidate candidate)
        {
            if (request == null || request.ObjectMeasures == null || request.ObjectMeasures.Count == 0 || candidate == null)
            {
                return null;
            }
            string kind;
            switch (candidate.Kind)
            {
                case BillObjectKind.Parking: kind = "parking"; break;
                case BillObjectKind.Owner: kind = "owner"; break;
                case BillObjectKind.Property: kind = "property"; break;
                default: return null;
            }
            return request.ObjectMeasures.FirstOrDefault(x => x != null &&
                x.ObjectId == candidate.Id && string.Equals((x.Kind ?? string.Empty).Trim(), kind, StringComparison.OrdinalIgnoreCase));
        }

        private static decimal? ObjectPriceOverrideOf(BillGenerateRequest request, BillObjectCandidate candidate)
        {
            BillObjectMeasureRequest hit = FindObjectInput(request, candidate);
            return hit == null ? (decimal?)null : hit.UnitPriceOverride;
        }

        /// <summary>
        /// CHG-v1.1.2-50：出账改价校验 —— 未开启「出账时可改价」的收费项目不得改价；改后单价必须大于 0。
        /// </summary>
        private static void ValidateUnitPriceOverride(ChargeItemDto chargeItem, decimal? unitPriceOverride)
        {
            if (!unitPriceOverride.HasValue) { return; }
            if (chargeItem == null || !chargeItem.AllowPriceOverride)
            {
                throw ApiException.ValidationFailed(
                    "收费项目「" + (chargeItem == null ? string.Empty : chargeItem.Name) +
                    "」未开启「出账时可改价」，请先到「收费项目维护」开启（周期性费用必须按价目表定价）");
            }
            if (unitPriceOverride.Value <= 0m)
            {
                throw ApiException.ValidationFailed("出账改价必须大于 0");
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
                    return "车位不存在有效「车位-业主」关系，请先在车位维护中绑定业主后再出账";
                case BillObjectKind.Owner:
                    return "业主档案不存在或已下线，请先在业主档案中确认后再出账";
                default:
                    return "房产不存在有效「房产-业主」关系，请先在业主-房产关系中绑定业主后再出账";
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

        /// <summary>
        /// 单张账单删除（UC-FIN-007）。
        /// CHG-v1.1.2-02：删除后其收款/退款记录在收款登记、财务报表、收支流水、欠费台账中同步不再显示
        /// （底层流水行保留留痕）；欠费台账界面已不再调用本方法（改用「移出台账」）。
        /// </summary>
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

        // ---------- 欠费台账「移出台账」（CHG-v1.1.2-03，负责人 2026-09-19 裁定 A） ----------
        /// <summary>
        /// 移出台账（单条/批量）：只写剔除记录，账单本身与其它模块数据**完全不变**。
        /// 背景：原口径「台账删除」直接软删 t_bill，导致账单工作台账单、收款登记、退款记录、业主档案缴费概况一并消失。
        /// </summary>
        public int DismissArrears(ArrearDismissRequest request, string operatorName = null, string ip = null)
        {
            if (request == null || request.BillIds == null || request.BillIds.Count == 0)
            {
                throw ApiException.BadRequest("请先勾选要移出台账的记录");
            }

            int affected;
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                affected = _finance.DismissArrearBills(connection, transaction, request.BillIds, request.Reason, operatorName);
                transaction.Commit();
            }

            _audit.Write("ARREARS_DISMISS", "bill", string.Join(",", request.BillIds),
                "移出台账 " + affected + " 条（仅影响欠费台账可见性，账单与其它模块数据不变，可恢复）",
                userName: operatorName, ip: ip, result: "Success");
            return affected;
        }

        /// <summary>已移出台账的记录（供「恢复台账」列表）。</summary>
        public List<ArrearDismissDto> QueryDismissedArrears()
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _finance.QueryDismissedArrears(connection);
            }
        }

        /// <summary>恢复台账：删除剔除记录，账单重新出现在欠费台账。</summary>
        public int RestoreArrears(ArrearDismissRequest request, string operatorName = null, string ip = null)
        {
            if (request == null || request.DismissIds == null || request.DismissIds.Count == 0)
            {
                throw ApiException.BadRequest("请先选择要恢复的记录");
            }

            int affected;
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                affected = _finance.RestoreArrearBills(connection, transaction, request.DismissIds);
                transaction.Commit();
            }

            _audit.Write("ARREARS_RESTORE", "bill", string.Join(",", request.DismissIds),
                "恢复台账 " + affected + " 条", userName: operatorName, ip: ip, result: "Success");
            return affected;
        }

        // ---------- 收款登记「应缴明细·批量删除已结清记录」（CHG-v1.2.0-31） ----------
        /// <summary>
        /// 收款登记「应缴明细」记录管理：把**已结清**账单从应缴明细列表移除（归档语义）。
        /// 与「欠费台账移出台账」（CHG-v1.1.2-03）同一口径 ——
        /// **只写归档标记**，账单行本身、收款/退款记录、财务报表、收支明细流水与业主档案缴费概况完全不变。
        /// 逐条校验：未结清（净实缴 &lt; 应收）、已删除、不存在的记录一律拒绝并回报原因；
        /// 已归档的重复提交幂等跳过。审计写 BILL_ARCHIVE_SETTLED。
        /// </summary>
        public BillArchiveResultDto ArchiveSettledBills(SettledBillArchiveRequest request,
            string operatorName = null, string ip = null)
        {
            var ids = (request == null || request.BillIds == null ? new List<int>() : request.BillIds)
                .Distinct().Where(x => x > 0).ToList();
            if (ids.Count == 0)
            {
                throw ApiException.BadRequest("请先选择要清理的已结清记录");
            }

            int archived;
            var skipped = new List<string>();
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                Dictionary<int, BillArchiveCandidate> candidates =
                    _finance.QueryBillArchiveCandidates(connection, ids).ToDictionary(x => x.Id);

                var accepted = new List<int>();
                foreach (int id in ids)
                {
                    BillArchiveCandidate candidate;
                    string no = "BILL-" + id.ToString("D4");
                    if (!candidates.TryGetValue(id, out candidate))
                    {
                        skipped.Add(no + " 不存在或已被删除");
                    }
                    else if (candidate.Deleted)
                    {
                        skipped.Add(no + " 已删除");
                    }
                    else if (!candidate.Settled)
                    {
                        skipped.Add(no + " 未结清（仅已结清记录可清理）");
                    }
                    else if (!candidate.Archived)
                    {
                        accepted.Add(id);
                    }
                }

                archived = _finance.ArchiveSettledBills(connection, transaction, accepted,
                    "收款登记·应缴明细·已结清记录清理", operatorName);
                transaction.Commit();
            }

            string message = "已清理 " + archived + " 条已结清记录（仅从应缴明细移除，账单与收款、财务、流水记录均保留）";
            if (skipped.Count > 0)
            {
                message += "；未清理 " + skipped.Count + " 条：" + string.Join("、", skipped);
            }

            _audit.Write("BILL_ARCHIVE_SETTLED", "bill", string.Join(",", ids), message,
                userName: operatorName, ip: ip, result: "Success");

            return new BillArchiveResultDto
            {
                ArchivedCount = archived,
                SkippedCount = skipped.Count,
                SkippedItems = skipped,
                Message = message
            };
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
