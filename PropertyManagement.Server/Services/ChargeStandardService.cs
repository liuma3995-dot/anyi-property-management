using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Domain;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 收费项目「价目表 + 计量变量」服务（CHG-v1.1.2-26，UC-FIN-001）。
    ///
    /// 口径（负责人 2026-09-19 裁定）：
    /// ① 收费标准承载价格（规格 / 单价 / 计算规则 / 计费周期 / 生效日期），收费项目只做「选标准 + 选缴费对象」；
    /// ② 计量单位与计算规则统一由变量库驱动，用户自建变量仅开放「手填 / 固定值」，每个收费标准 ≤ 5 个自建变量；
    /// ③ 改价 = 新增规格 + 旧规格转停用，变更原因必填并写审计；
    /// ④ 内置变量可停用不可删除；被收费标准 / 账单引用的对象只允许停用。
    /// </summary>
    public class ChargeStandardService
    {
        /// <summary>单个收费标准的用户自建变量上限（内置变量不占额度）。</summary>
        public const int MaxCustomVariablesPerStandard = 5;

        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IChargeStandardRepository _repo;
        private readonly AuditService _audit;

        public ChargeStandardService()
            : this(new SqliteConnectionFactory(), new SqlChargeStandardRepository(), new AuditService())
        {
        }

        public ChargeStandardService(IDbConnectionFactory connectionFactory, IChargeStandardRepository repo, AuditService audit)
        {
            _connectionFactory = connectionFactory;
            _repo = repo;
            _audit = audit;
        }

        // ================= 收费标准 =================

        public List<ChargeStandardDto> ListStandards(string keyword, string category, bool includeDisabled = true)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _repo.ListStandards(connection, keyword, category, includeDisabled);
            }
        }

        public ChargeStandardDto GetStandard(int id)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                ChargeStandardDto dto = _repo.GetStandard(connection, id);
                if (dto == null) { throw ApiException.NotFound("收费标准不存在或已删除"); }
                return dto;
            }
        }

        public ChargeStandardDto CreateStandard(ChargeStandardRequest request, string operatorName = null, string ip = null)
        {
            ValidateStandard(request);
            var dto = new ChargeStandardDto
            {
                Name = request.Name.Trim(),
                Category = (request.Category ?? string.Empty).Trim(),
                Remark = request.Remark,
                Status = request.Status ?? 0
            };

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                dto.Id = _repo.InsertStandard(connection, transaction, dto);
                BindVariables(connection, transaction, dto, request.VariableIds);
                transaction.Commit();
            }

            _audit.Write("CHARGE_STANDARD_CREATE", "charge_standard", dto.Id.ToString(),
                "新增收费标准：" + dto.Name + "（类别 " + dto.Category + "）",
                userName: operatorName, ip: ip, result: "Success");
            return GetStandard(dto.Id);
        }

        public ChargeStandardDto UpdateStandard(int id, ChargeStandardRequest request, string operatorName = null, string ip = null)
        {
            ValidateStandard(request);
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeStandardDto existing = _repo.GetStandard(connection, id);
                if (existing == null) { throw ApiException.NotFound("收费标准不存在或已删除"); }

                existing.Name = request.Name.Trim();
                existing.Category = (request.Category ?? string.Empty).Trim();
                existing.Remark = request.Remark;
                existing.Status = request.Status ?? existing.Status;
                _repo.UpdateStandard(connection, transaction, existing);
                if (request.VariableIds != null)
                {
                    // CHG-v1.1.2-52：绑定集合不得把「启用中规格仍在引用的变量」解绑 ——
                    // 否则配置自相矛盾（规格公式引用未绑定变量），事后只在出账时以「未命中规格」报错，用户查不到根因。
                    ValidateVariableBinding(connection, existing, request.VariableIds);
                    BindVariables(connection, transaction, new ChargeStandardDto { Id = id }, request.VariableIds);
                }
                transaction.Commit();
            }

            _audit.Write("CHARGE_STANDARD_UPDATE", "charge_standard", id.ToString(),
                "修改收费标准：" + request.Name.Trim(), userName: operatorName, ip: ip, result: "Success");
            return GetStandard(id);
        }

        public void DeleteStandard(int id, string operatorName = null, string ip = null)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                ChargeStandardDto existing = _repo.GetStandard(connection, id);
                if (existing == null) { throw ApiException.NotFound("收费标准不存在或已删除"); }
                int items = _repo.CountStandardItems(connection, id);
                if (items > 0)
                {
                    throw ApiException.ValidationFailed(
                        "该收费标准已被 " + items + " 个收费项目引用，不能删除；如需停用请改用「停用」");
                }
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                _repo.SoftDeleteStandard(connection, transaction, id);
                transaction.Commit();
            }
            _audit.Write("CHARGE_STANDARD_DELETE", "charge_standard", id.ToString(),
                "删除收费标准（软删留痕）", userName: operatorName, ip: ip, result: "Success");
        }

        public ChargeStandardDto ToggleStandardStatus(int id, int status, string operatorName = null, string ip = null)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeStandardDto existing = _repo.GetStandard(connection, id);
                if (existing == null) { throw ApiException.NotFound("收费标准不存在或已删除"); }
                existing.Status = status == 0 ? 0 : 1;
                _repo.UpdateStandard(connection, transaction, existing);
                transaction.Commit();
            }
            _audit.Write("CHARGE_STANDARD_STATUS", "charge_standard", id.ToString(),
                status == 0 ? "启用收费标准" : "停用收费标准（停用后不再生成新账单，历史账单不受影响）",
                userName: operatorName, ip: ip, result: "Success");
            return GetStandard(id);
        }

        // ================= 规格明细（价目表） =================

        public List<ChargeStandardSpecDto> ListSpecs(int standardId, bool includeDisabled = true)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _repo.ListSpecs(connection, standardId, includeDisabled);
            }
        }

        public ChargeStandardSpecDto CreateSpec(int standardId, ChargeStandardSpecRequest request,
            string operatorName = null, string ip = null)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.SpecName))
            {
                throw ApiException.ValidationFailed("规格名称不能为空");
            }
            if (request.UnitPrice <= 0m)
            {
                throw ApiException.ValidationFailed("单价必须大于 0");
            }
            if (request.DeprecateSameName && string.IsNullOrWhiteSpace(request.ChangeReason))
            {
                throw ApiException.ValidationFailed("改价必须填写变更原因（用于审计追溯）");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeStandardDto standard = _repo.GetStandard(connection, standardId);
                if (standard == null) { throw ApiException.NotFound("收费标准不存在或已删除"); }

                ChargeStandardSpecDto dto = BuildSpec(standardId, request, standard.Variables);
                dto.Id = _repo.InsertSpec(connection, transaction, dto);
                if (request.DeprecateSameName)
                {
                    _repo.DeprecateSpecsByName(connection, transaction, standardId, dto.SpecName, dto.Id);
                }
                transaction.Commit();

                _audit.Write("CHARGE_SPEC_CREATE", "charge_standard", standardId.ToString(),
                    "新增规格：" + dto.SpecName + "，单价 " + dto.UnitPrice.ToString("0.00") + "/" + (dto.PriceUnit ?? "元") +
                    (request.DeprecateSameName ? "；同名旧规格已转停用；变更原因：" + request.ChangeReason : string.Empty),
                    userName: operatorName, ip: ip, result: "Success");
                return dto;
            }
        }

        public ChargeStandardSpecDto UpdateSpec(int specId, ChargeStandardSpecRequest request,
            string operatorName = null, string ip = null)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.SpecName))
            {
                throw ApiException.ValidationFailed("规格名称不能为空");
            }
            if (request.UnitPrice <= 0m)
            {
                throw ApiException.ValidationFailed("单价必须大于 0");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeStandardSpecDto existing = _repo.GetSpec(connection, specId);
                if (existing == null) { throw ApiException.NotFound("规格不存在或已删除"); }
                ChargeStandardDto standard = _repo.GetStandard(connection, existing.StandardId);

                ChargeStandardSpecDto dto = BuildSpec(existing.StandardId, request,
                    standard == null ? null : standard.Variables);
                dto.Id = specId;
                _repo.UpdateSpec(connection, transaction, dto);
                transaction.Commit();

                _audit.Write("CHARGE_SPEC_UPDATE", "charge_standard", existing.StandardId.ToString(),
                    "修改规格：" + dto.SpecName + "，单价 " + dto.UnitPrice.ToString("0.00") + "/" + (dto.PriceUnit ?? "元"),
                    userName: operatorName, ip: ip, result: "Success");
                return dto;
            }
        }

        public void DeleteSpec(int specId, string operatorName = null, string ip = null)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                ChargeStandardSpecDto existing = _repo.GetSpec(connection, specId);
                if (existing == null) { throw ApiException.NotFound("规格不存在或已删除"); }
                int bills = _repo.CountSpecBills(connection, specId);
                if (bills > 0)
                {
                    throw ApiException.ValidationFailed(
                        "该规格已被 " + bills + " 张账单引用，不能删除；如需停止使用请改用「停用」");
                }
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                _repo.SoftDeleteSpec(connection, transaction, specId);
                transaction.Commit();
            }
            _audit.Write("CHARGE_SPEC_DELETE", "charge_standard_spec", specId.ToString(),
                "删除规格（软删留痕，历史账单不受影响）", userName: operatorName, ip: ip, result: "Success");
        }

        public void ToggleSpecStatus(int specId, int status, string operatorName = null, string ip = null)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeStandardSpecDto existing = _repo.GetSpec(connection, specId);
                if (existing == null) { throw ApiException.NotFound("规格不存在或已删除"); }
                existing.Status = status == 0 ? 0 : 1;
                // CHG-v1.1.2-52：重新启用前先校验「计算规则引用的变量是否仍绑定在本收费标准上」——
                // 否则启用后出账只会给出「未命中规格」，用户看不出根因是变量被解绑。
                if (existing.Status == 0)
                {
                    ValidateSpecVariables(connection, existing);
                }
                _repo.UpdateSpec(connection, transaction, existing);
                transaction.Commit();
            }
            _audit.Write("CHARGE_SPEC_STATUS", "charge_standard_spec", specId.ToString(),
                status == 0 ? "启用规格" : "停用规格（停用只影响后续出账，已出账单金额不变）",
                userName: operatorName, ip: ip, result: "Success");
        }

        // ================= 计量变量 =================

        public List<ChargeVariableDto> ListVariables(string keyword, bool includeDisabled = true)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _repo.ListVariables(connection, keyword, includeDisabled);
            }
        }

        public ChargeVariableDto CreateVariable(ChargeVariableRequest request, string operatorName = null, string ip = null)
        {
            ValidateVariable(request);
            var dto = new ChargeVariableDto
            {
                VarName = request.VarName.Trim(),
                Unit = (request.Unit ?? string.Empty).Trim(),
                ValueType = request.ValueType,
                Source = request.Source,
                DefaultValue = request.DefaultValue,
                ObjectScope = request.ObjectScope,
                IsBuiltin = false,
                Remark = request.Remark,
                Status = request.Status ?? 0
            };

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeVariableDto sameName = _repo.ListVariables(connection, null, true)
                    .FirstOrDefault(x => string.Equals(x.VarName, dto.VarName, StringComparison.OrdinalIgnoreCase));
                if (sameName != null)
                {
                    throw ApiException.ValidationFailed("同名的计量变量已存在：" + dto.VarName);
                }

                dto.VarCode = NextVariableCode(connection);
                dto.Sort = 900;
                dto.Id = _repo.InsertVariable(connection, transaction, dto);
                transaction.Commit();
            }

            _audit.Write("CHARGE_VARIABLE_CREATE", "charge_variable", dto.Id.ToString(),
                "新增计量变量：" + dto.VarName + "（单位 " + dto.Unit + "，" + SourceText(dto.Source) + "）",
                userName: operatorName, ip: ip, result: "Success");
            return dto;
        }

        public ChargeVariableDto UpdateVariable(int id, ChargeVariableRequest request, string operatorName = null, string ip = null)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.VarName))
            {
                throw ApiException.ValidationFailed("变量名称不能为空");
            }
            ValidateVariableName(request.VarName);

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeVariableDto existing = _repo.GetVariable(connection, id);
                if (existing == null) { throw ApiException.NotFound("计量变量不存在或已删除"); }

                if (existing.IsBuiltin && request.Source != existing.Source)
                {
                    throw ApiException.ValidationFailed("内置变量的取值来源不可修改（可修改单位与名称）");
                }
                if (!existing.IsBuiltin && request.Source != ChargeVariableSource.Manual &&
                    request.Source != ChargeVariableSource.Fixed)
                {
                    throw ApiException.ValidationFailed("自建变量只支持「手填」或「固定值」两种取值来源");
                }

                existing.VarName = request.VarName.Trim();
                bool unitChanged = !string.Equals((existing.Unit ?? string.Empty).Trim(),
                    (request.Unit ?? string.Empty).Trim(), StringComparison.Ordinal);
                existing.Unit = (request.Unit ?? string.Empty).Trim();
                existing.ValueType = request.ValueType;
                existing.Source = request.Source;
                existing.DefaultValue = request.DefaultValue;
                existing.ObjectScope = request.ObjectScope;
                existing.Remark = request.Remark;
                existing.Status = request.Status ?? existing.Status;
                _repo.UpdateVariable(connection, transaction, existing);
                // 变量单位变更 → 级联重算受影响规格的「单价单位」（元/桶 → 元/升），避免价目表显示过期单位
                if (unitChanged)
                {
                    foreach (int standardId in _repo.ListStandardIdsByVariable(connection, id))
                    {
                        ChargeStandardDto standard = _repo.GetStandard(connection, standardId);
                        if (standard == null) { continue; }
                        foreach (ChargeStandardSpecDto spec in standard.Specs ?? new List<ChargeStandardSpecDto>())
                        {
                            spec.PriceUnit = ChargeSpecMatcher.DerivePriceUnit(standard.Variables, spec.Formula);
                            _repo.UpdateSpec(connection, transaction, spec);
                        }
                    }
                }
                transaction.Commit();
                return existing;
            }
        }

        public void DeleteVariable(int id, string operatorName = null, string ip = null)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                ChargeVariableDto existing = _repo.GetVariable(connection, id);
                if (existing == null) { throw ApiException.NotFound("计量变量不存在或已删除"); }
                if (existing.IsBuiltin)
                {
                    throw ApiException.ValidationFailed("内置变量不可删除（避免历史账单公式无法解析），如需停用请改用「停用」");
                }
                int used = _repo.CountVariableUsage(connection, id);
                if (used > 0)
                {
                    throw ApiException.ValidationFailed(
                        "该变量已被 " + used + " 个收费标准引用，不能删除；如需停用请改用「停用」");
                }
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                _repo.SoftDeleteVariable(connection, transaction, id);
                transaction.Commit();
            }
            _audit.Write("CHARGE_VARIABLE_DELETE", "charge_variable", id.ToString(),
                "删除计量变量（软删留痕）", userName: operatorName, ip: ip, result: "Success");
        }

        public void ToggleVariableStatus(int id, int status, string operatorName = null, string ip = null)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ChargeVariableDto existing = _repo.GetVariable(connection, id);
                if (existing == null) { throw ApiException.NotFound("计量变量不存在或已删除"); }
                existing.Status = status == 0 ? 0 : 1;
                _repo.UpdateVariable(connection, transaction, existing);
                transaction.Commit();
            }
            _audit.Write("CHARGE_VARIABLE_STATUS", "charge_variable", id.ToString(),
                status == 0 ? "启用计量变量" : "停用计量变量（已引用该变量的收费标准将无法生成新账单）",
                userName: operatorName, ip: ip, result: "Success");
        }

        // ================= 匹配与试算（供出账与预览共用） =================

        /// <summary>命中规格并求解单位 / 公式；matched=false 时 Reason 为业务化原因。</summary>
        public ChargeSpecMatchDto MatchSpec(ChargeStandardDto standard, BillObjectCandidate candidate, int? explicitSpecId)
        {
            var result = new ChargeSpecMatchDto { Matched = false };
            if (standard == null)
            {
                result.Reason = "该收费项目尚未绑定收费标准，请先在收费项目维护中补充";
                return result;
            }

            string reason;
            ChargeStandardSpecDto spec = ChargeSpecMatcher.Match(standard.Specs, candidate, explicitSpecId, out reason);
            if (spec == null)
            {
                result.Reason = reason;
                return result;
            }

            result.SpecId = spec.Id;
            result.SpecName = spec.SpecName;
            result.IsFallback = spec.IsFallback;
            result.UnitPrice = spec.UnitPrice;
            result.Formula = ChargeSpecMatcher.RewriteForEvaluation(spec.Formula, standard.Variables);
            result.PriceUnit = string.IsNullOrWhiteSpace(spec.PriceUnit)
                ? ChargeSpecMatcher.DerivePriceUnit(standard.Variables, spec.Formula)
                : spec.PriceUnit;
            result.Matched = true;
            return result;
        }

        /// <summary>
        /// 出账试算（只读）：按收费标准 + 缴费对象求解规格、计量取值、单价与公式，供账单工作台「出账预演」。
        /// </summary>
        public ChargeSpecMatchDto PreviewAmount(int standardId, BillObjectCandidate candidate, int? explicitSpecId,
            BillingCycleDto cycle, IDictionary<int, decimal> measures, out string formula, out decimal amount,
            out IDictionary<string, decimal> snapshot)
        {
            formula = null;
            amount = 0m;
            snapshot = new Dictionary<string, decimal>();
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                ChargeStandardDto standard = _repo.GetStandard(connection, standardId);
                ChargeSpecMatchDto match = MatchSpec(standard, candidate, explicitSpecId);
                if (!match.Matched) { return match; }

                ChargeStandardSpecDto spec = (standard.Specs ?? new List<ChargeStandardSpecDto>())
                    .FirstOrDefault(x => x.Id == match.SpecId);
                if (spec == null) { return match; }

                IList<string> extraNames;
                IDictionary<string, decimal> context = ChargeSpecMatcher.BuildContext(
                    spec, standard.Variables, candidate, cycle, measures, out extraNames);
                if (context.ContainsKey(ChargeSpecMatcher.PriceVariable)) { context[ChargeSpecMatcher.PriceVariable] = spec.UnitPrice; }

                formula = ChargeSpecMatcher.RewriteForEvaluation(spec.Formula, standard.Variables);
                snapshot = context;
                if (string.IsNullOrWhiteSpace(formula))
                {
                    amount = Math.Round(spec.UnitPrice, 2, MidpointRounding.AwayFromZero);
                    return match;
                }

                decimal computed;
                string error;
                if (!ChargeFormula.TryEvaluate(formula, context, extraNames, out computed, out error))
                {
                    match.Matched = false;
                    match.Reason = "按价目表公式计价失败：" + error;
                    return match;
                }
                if (computed <= 0m)
                {
                    match.Matched = false;
                    match.Reason = "按价目表公式计价结果为 0，请检查单价与公式设置";
                    return match;
                }
                amount = computed;
                return match;
            }
        }

        // ================= 内部工具 =================

        /// <summary>
        /// CHG-v1.1.2-52：启用规格前的自洽校验 —— 该规格的计算规则若引用了「未绑定到本收费标准」的变量，
        /// 直接拒绝启用并给出可执行指引（到变量区勾选后重试），避免启用后出账只报「未命中规格」。
        /// </summary>
        private void ValidateSpecVariables(IDbConnection connection, ChargeStandardSpecDto spec)
        {
            List<int> referenced = ChargeSpecMatcher.ParseTokens(spec.Formula);
            if (referenced.Count == 0) { return; }

            ChargeStandardDto standard = _repo.GetStandard(connection, spec.StandardId);
            List<ChargeVariableDto> current = standard == null
                ? new List<ChargeVariableDto>()
                : standard.Variables ?? new List<ChargeVariableDto>();
            var bound = new HashSet<int>(current.Select(x => x.Id));
            // 变量名取自全量变量表（被解绑/未绑定的变量不在 standard.Variables 里，按 ID 提示用户看不懂）
            var names = new Dictionary<int, string>();
            foreach (ChargeVariableDto variable in _repo.ListVariables(connection, null, true))
            {
                names[variable.Id] = variable.VarName;
            }

            foreach (int variableId in referenced)
            {
                if (bound.Contains(variableId)) { continue; }
                string name;
                if (!names.TryGetValue(variableId, out name) || string.IsNullOrWhiteSpace(name))
                {
                    name = "变量 ID " + variableId;
                }
                throw ApiException.ValidationFailed(
                    "规格「" + spec.SpecName + "」的计算规则引用了未绑定到本收费标准的计量变量「" + name +
                    "」，无法启用。请先在「价目表 → 变量区」勾选该变量并保存，再重新启用该规格。");
            }
        }

        /// <summary>
        /// CHG-v1.1.2-52：绑定集合自洽校验 —— 目标绑定集合必须覆盖该标准下**启用中**规格公式引用的全部变量。
        /// 被解绑的变量若仍被启用规格引用，直接拒绝并点名「规格 + 变量」，
        /// 避免把配置改成自相矛盾的状态（原先允许写入，事后只在出账时以「未命中规格」暴露，用户查不到根因）。
        /// 停用中的规格不参与校验：改价替换、暂不使用的规格不受影响。
        /// </summary>
        private void ValidateVariableBinding(IDbConnection connection, ChargeStandardDto standard, List<int> variableIds)
        {
            List<ChargeStandardSpecDto> enabledSpecs = (standard == null
                    ? new List<ChargeStandardSpecDto>()
                    : (standard.Specs ?? new List<ChargeStandardSpecDto>()))
                .Where(x => x.Status == 0)
                .ToList();
            if (enabledSpecs.Count == 0) { return; }

            var target = new HashSet<int>(variableIds ?? new List<int>());
            var names = new Dictionary<int, string>();
            foreach (ChargeVariableDto variable in _repo.ListVariables(connection, null, true))
            {
                names[variable.Id] = variable.VarName;
            }

            var missing = new List<string>();
            foreach (ChargeStandardSpecDto spec in enabledSpecs)
            {
                foreach (int variableId in ChargeSpecMatcher.ParseTokens(spec.Formula))
                {
                    if (target.Contains(variableId)) { continue; }
                    string name;
                    if (!names.TryGetValue(variableId, out name) || string.IsNullOrWhiteSpace(name))
                    {
                        name = "变量 ID " + variableId;
                    }
                    string text = "规格「" + spec.SpecName + "」引用「" + name + "」";
                    if (!missing.Contains(text)) { missing.Add(text); }
                }
            }
            if (missing.Count > 0)
            {
                throw ApiException.ValidationFailed(
                    "不能解绑仍被启用规格引用的计量变量：" + string.Join("；", missing) +
                    "。请先修改这些规格的计算规则，或停用 / 删除这些规格后再解绑。");
            }
        }

        private void BindVariables(IDbConnection connection, IDbTransaction transaction, ChargeStandardDto standard,
            List<int> variableIds = null)
        {
            List<int> ids = variableIds != null
                ? variableIds.Distinct().ToList()
                : (standard.Variables ?? new List<ChargeVariableDto>()).Select(x => x.Id).ToList();
            if (ids.Count == 0)
            {
                _repo.ReplaceStandardVariables(connection, transaction, standard.Id, ids, new HashSet<int>());
                return;
            }

            List<ChargeVariableDto> all = _repo.ListVariables(connection, null, true);
            var custom = new HashSet<int>();
            foreach (int id in ids)
            {
                ChargeVariableDto variable = all.FirstOrDefault(x => x.Id == id);
                if (variable == null) { throw ApiException.ValidationFailed("计量变量不存在或已删除（ID " + id + "）"); }
                if (!variable.IsBuiltin) { custom.Add(id); }
            }
            if (custom.Count > MaxCustomVariablesPerStandard)
            {
                throw ApiException.ValidationFailed(
                    "单个收费标准最多引用 " + MaxCustomVariablesPerStandard + " 个自建变量（内置变量不占额度），当前为 " + custom.Count + " 个");
            }
            _repo.ReplaceStandardVariables(connection, transaction, standard.Id, ids, custom);
        }

        private static void ValidateStandard(ChargeStandardRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                throw ApiException.ValidationFailed("收费标准名称不能为空");
            }
            if (string.IsNullOrWhiteSpace(request.Category))
            {
                throw ApiException.ValidationFailed("请选择收费项目类别");
            }
        }

        private ChargeStandardSpecDto BuildSpec(int standardId, ChargeStandardSpecRequest request,
            IList<ChargeVariableDto> variables)
        {
            string formula = (request.Formula ?? string.Empty).Trim();

            // CHG-v1.2.0-14（负责人 2026-09-21）：适用条件下线「房产状态」维度 ——
            // 空置定价统一走「房产用途 = 空置」（房产列表已展示用途，口径唯一、不再两处都能配）。
            if (request.MatchStatus.HasValue)
            {
                throw ApiException.ValidationFailed(
                    "适用条件不再支持「房产状态」（该维度已下线）：如需按空置定价，请选择「房产用途 = 空置」");
            }

            // 公式引用的变量必须已绑定到该收费标准（否则出账时取不到值，属于配置错误，保存阶段即拦下）
            if (formula.Length > 0)
            {
                List<int> bound = (variables ?? new List<ChargeVariableDto>()).Select(x => x.Id).ToList();
                foreach (int id in ChargeSpecMatcher.ParseTokens(formula))
                {
                    if (!bound.Contains(id))
                    {
                        throw ApiException.ValidationFailed(
                            "计算规则引用了未绑定到本收费标准的计量变量（变量 ID " + id + "），请先在变量区勾选该变量");
                    }
                }
            }

            string rewritten = ChargeSpecMatcher.RewriteForEvaluation(formula, variables);
            if (rewritten.Length > 0)
            {
                decimal demo;
                string error;
                IList<string> extra = (variables ?? new List<ChargeVariableDto>())
                    .Select(x => x.VarName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
                var demoContext = new Dictionary<string, decimal>
                {
                    { "单价", 1m }, { "面积", 1m }, { "月数", 1m }, { "天数", 1m }, { "数量", 1m }, { "年数", 1m }
                };
                foreach (string name in extra) { demoContext[name] = 1m; }
                if (!ChargeFormula.TryEvaluate(rewritten, demoContext, extra, out demo, out error))
                {
                    throw ApiException.ValidationFailed("计算规则不合法：" + error);
                }
            }

            List<ChargeFormulaVarDto> used = ChargeSpecMatcher.CollectFormulaVars(variables, formula);
            return new ChargeStandardSpecDto
            {
                StandardId = standardId,
                SpecName = request.SpecName.Trim(),
                MatchUsage = request.MatchUsage,
                MatchStatus = request.MatchStatus,
                MatchSpaceType = request.MatchSpaceType,
                MatchBuilding = string.IsNullOrWhiteSpace(request.MatchBuilding) ? null : request.MatchBuilding.Trim(),
                IsFallback = request.IsFallback,
                UnitPrice = request.UnitPrice,
                Formula = formula,
                FormulaVars = used,
                FormulaVarsJson = used.Count == 0 ? null : Newtonsoft.Json.JsonConvert.SerializeObject(used),
                PriceUnit = ChargeSpecMatcher.DerivePriceUnit(variables, formula),
                CycleName = request.CycleName,
                EffectiveFrom = request.EffectiveFrom,
                Remark = request.Remark,
                Status = request.Status ?? 0
            };
        }

        private static void ValidateVariable(ChargeVariableRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.VarName))
            {
                throw ApiException.ValidationFailed("变量名称不能为空");
            }
            ValidateVariableName(request.VarName);
            if (request.Source != ChargeVariableSource.Manual && request.Source != ChargeVariableSource.Fixed)
            {
                throw ApiException.ValidationFailed(
                    "自建变量只支持「手填」或「固定值」两种取值来源（档案自动 / 周期派生为系统内置专用）");
            }
        }

        private static void ValidateVariableName(string name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            foreach (char c in trimmed)
            {
                if (!char.IsLetter(c) && c != '_')
                {
                    throw ApiException.ValidationFailed("变量名称只能使用中文 / 英文字母（不可含数字、空格或符号），以便写入计算公式");
                }
            }
            if (trimmed.Length == 0)
            {
                throw ApiException.ValidationFailed("变量名称不能为空");
            }
            if (ChargeFormula.Variables.Contains(trimmed) || trimmed == "单价")
            {
                throw ApiException.ValidationFailed("变量名称「" + trimmed + "」为系统内置变量名，请换一个名称");
            }
        }

        private static string NextVariableCode(IDbConnection connection)
        {
            int max = connection.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(CAST(SUBSTR(var_code, 4) AS INTEGER)), 0) FROM t_charge_variable WHERE var_code LIKE 'MQ-%'");
            return "MQ-" + (max + 1).ToString("00", CultureInfo.InvariantCulture);
        }

        internal static string SourceText(ChargeVariableSource source)
        {
            switch (source)
            {
                case ChargeVariableSource.Archive: return "档案自动";
                case ChargeVariableSource.CycleDerived: return "周期派生";
                case ChargeVariableSource.Fixed: return "固定值";
                default: return "手填";
            }
        }
    }
}
