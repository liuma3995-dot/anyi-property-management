using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Domain
{
    /// <summary>
    /// 收费规格匹配与计量变量求解（CHG-v1.1.2-26）。
    ///
    /// 职责（纯函数，无副作用）：
    /// 1) 规格匹配：按缴费对象属性（房产用途 / 状态 / 车位类型 / 楼栋）命中规格，取最具体的一条；
    ///    无档案对象（自定义缴费对象）由用户手选规格；都未命中时回落「兜底规格」。
    /// 2) 计量求解：按变量来源（档案自动 / 周期派生 / 手填 / 固定值）构建公式上下文。
    /// 3) 单位推导：单价单位 = 元 / 公式中非固定值变量的单位组合（如 元/台·月、元/桶、元/㎡·月）。
    /// 4) 公式记号：落库使用 {v:ID} 引用变量（改名不影响历史公式），求值前重写为变量名。
    /// </summary>
    public static class ChargeSpecMatcher
    {
        /// <summary>单价变量名（内置，公式中始终可用）。</summary>
        public const string PriceVariable = "单价";

        private static readonly Regex TokenRegex = new Regex(@"\{v:(\d+)\}", RegexOptions.Compiled);

        // ---------- 1) 规格匹配 ----------

        /// <summary>
        /// 命中规格。返回 null 时 <paramref name="reason"/> 为业务化中文原因（记入失败明细）。
        /// 优先级：用户手选规格 &gt; 维度最具体 &gt; 兜底规格。
        /// </summary>
        public static ChargeStandardSpecDto Match(IList<ChargeStandardSpecDto> specs, BillObjectCandidate candidate,
            int? explicitSpecId, out string reason)
        {
            reason = null;
            var enabled = (specs ?? new List<ChargeStandardSpecDto>())
                .Where(x => x.Status == 0)
                .ToList();
            if (enabled.Count == 0)
            {
                reason = "该收费标准尚未维护启用中的规格价目，请先在「价目表」页签补充规格";
                return null;
            }

            // 用户手选（自定义缴费对象无档案属性，规格由用户指定）
            if (explicitSpecId.HasValue && explicitSpecId.Value > 0)
            {
                ChargeStandardSpecDto picked = enabled.FirstOrDefault(x => x.Id == explicitSpecId.Value);
                if (picked != null) { return picked; }
            }

            List<ChargeStandardSpecDto> concrete = enabled.Where(x => !x.IsFallback).ToList();
            List<ChargeStandardSpecDto> hits = concrete.Where(x => IsMatch(x, candidate)).ToList();
            if (hits.Count > 0)
            {
                return hits
                    .OrderByDescending(Specificity)
                    .ThenBy(x => x.Id)
                    .First();
            }

            ChargeStandardSpecDto fallback = enabled
                .Where(x => x.IsFallback)
                .OrderByDescending(Specificity)
                .ThenBy(x => x.Id)
                .FirstOrDefault();
            if (fallback != null) { return fallback; }

            reason = "未命中任何规格，且该收费标准未配置「不限（兜底）」规格：" + DescribeCandidate(candidate);
            return null;
        }

        private static bool IsMatch(ChargeStandardSpecDto spec, BillObjectCandidate candidate)
        {
            if (spec.MatchUsage.HasValue && spec.MatchUsage.Value != (candidate.Usage ?? int.MinValue)) { return false; }
            if (spec.MatchStatus.HasValue && spec.MatchStatus.Value != (candidate.Status ?? int.MinValue)) { return false; }
            if (spec.MatchSpaceType.HasValue && spec.MatchSpaceType.Value != (candidate.SpaceType ?? int.MinValue)) { return false; }
            if (!string.IsNullOrWhiteSpace(spec.MatchBuilding) &&
                !string.Equals(spec.MatchBuilding.Trim(), (candidate.BuildingNo ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }

        private static int Specificity(ChargeStandardSpecDto spec)
        {
            int score = 0;
            if (spec.MatchUsage.HasValue) { score++; }
            if (spec.MatchStatus.HasValue) { score++; }
            if (spec.MatchSpaceType.HasValue) { score++; }
            if (!string.IsNullOrWhiteSpace(spec.MatchBuilding)) { score++; }
            return score;
        }

        private static string DescribeCandidate(BillObjectCandidate candidate)
        {
            switch (candidate.Kind)
            {
                case BillObjectKind.Parking:
                    return "车位「" + candidate.No + "」（类型 " + SpaceTypeText(candidate.SpaceType) + "）";
                case BillObjectKind.Owner:
                    return "业主「" + candidate.No + "」";
                case BillObjectKind.Custom:
                    return "自定义缴费对象「" + candidate.No + "」";
                default:
                    return "房产「" + candidate.BuildingNo + " " + candidate.No + "」（用途 " + UsageText(candidate.Usage) +
                           "、状态 " + StatusText(candidate.Status) + "）";
            }
        }

        public static string UsageText(int? usage)
        {
            if (!usage.HasValue) { return "未填"; }
            // CHG-v1.1.2-48：用途新增「空置」（0 住宅 / 1 商铺 / 2 空置）
            switch (usage.Value)
            {
                case 1: return "商铺";
                case 2: return "空置";
                default: return "住宅";
            }
        }

        public static string StatusText(int? status)
        {
            if (!status.HasValue) { return "未填"; }
            switch (status.Value)
            {
                case 0: return "空置";
                case 1: return "入住";
                case 2: return "装修中";
                default: return "未填";
            }
        }

        public static string SpaceTypeText(int? spaceType)
        {
            if (!spaceType.HasValue) { return "未填"; }
            switch (spaceType.Value)
            {
                case 0: return "产权";
                case 1: return "普通";
                case 2: return "临时";
                default: return "未填";
            }
        }

        // ---------- 2) 计量求解 ----------

        /// <summary>
        /// 构建公式上下文：内置 5 变量（兼容历史公式） + 该收费标准引用的计量变量。
        /// <paramref name="extraNames"/> 为可识别变量名集合（供公式解析器放行）。
        /// </summary>
        public static IDictionary<string, decimal> BuildContext(ChargeStandardSpecDto spec,
            IList<ChargeVariableDto> variables, BillObjectCandidate candidate, BillingCycleDto cycle,
            IDictionary<int, decimal> measures, out IList<string> extraNames)
        {
            var context = new Dictionary<string, decimal> { { PriceVariable, spec == null ? 0m : spec.UnitPrice }, { "数量", 1m } };
            if (candidate.Area.HasValue && candidate.Area.Value > 0m) { context["面积"] = candidate.Area.Value; }
            if (cycle != null)
            {
                // CHG-v1.1.2-35 / -36：周期派生变量的统一口径（详见 ResolveCycleFactors）
                int months;
                int days;
                decimal years;
                ResolveCycleFactors(spec, cycle, out months, out days, out years);
                context["月数"] = months;
                context["年数"] = years;
                context["天数"] = days;
            }

            var names = new List<string>();
            foreach (ChargeVariableDto variable in variables ?? new List<ChargeVariableDto>())
            {
                string name = (variable.VarName ?? string.Empty).Trim();
                if (name.Length == 0) { continue; }
                if (!names.Contains(name)) { names.Add(name); }

                decimal value;
                switch (variable.Source)
                {
                    case ChargeVariableSource.Fixed:
                        context[name] = variable.DefaultValue ?? 1m;
                        break;
                    case ChargeVariableSource.CycleDerived:
                        if (context.TryGetValue(name, out value)) { context[name] = value; }
                        break;
                    case ChargeVariableSource.Archive:
                        if (candidate.Kind != BillObjectKind.Custom && TryArchiveValue(variable.FieldKey, candidate, out value))
                        {
                            context[name] = value;
                        }
                        else if (TryMeasure(measures, variable, out value))
                        {
                            context[name] = value;
                        }
                        break;
                    default:
                        if (TryMeasure(measures, variable, out value)) { context[name] = value; }
                        break;
                }
            }
            extraNames = names;
            return context;
        }

        private static bool TryMeasure(IDictionary<int, decimal> measures, ChargeVariableDto variable, out decimal value)
        {
            value = 0m;
            if (measures != null && measures.TryGetValue(variable.Id, out value)) { return true; }
            if (variable.DefaultValue.HasValue)
            {
                value = variable.DefaultValue.Value;
                return true;
            }
            return false;
        }

        private static bool TryArchiveValue(string fieldKey, BillObjectCandidate candidate, out decimal value)
        {
            value = 0m;
            switch ((fieldKey ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "property.area":
                case "property.inner_area":
                    if (candidate.Area.HasValue && candidate.Area.Value > 0m) { value = candidate.Area.Value; return true; }
                    return false;
                case "property.usage":
                    if (candidate.Usage.HasValue) { value = candidate.Usage.Value; return true; }
                    return false;
                case "property.status":
                    if (candidate.Status.HasValue) { value = candidate.Status.Value; return true; }
                    return false;
                case "property.count":
                case "parking.count":
                    value = 1m;
                    return true;
                case "parking.type":
                    if (candidate.SpaceType.HasValue) { value = candidate.SpaceType.Value; return true; }
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>含首尾的自然月数（2026-01-01 ~ 2026-12-31 = 12；跨月不足一月按 1 计）。</summary>
        public static int CountInclusiveMonths(DateTime start, DateTime end)
        {
            int months = (end.Year - start.Year) * 12 + (end.Month - start.Month) + 1;
            return months < 1 ? 1 : months;
        }

        /// <summary>
        /// CHG-v1.1.2-35：规格计费周期折算月数（每月 1 / 每季 3 / 每半年 6 / 每年 12）。
        /// 一次性与自定义 / 未设置一律返回 0（由 <see cref="ResolveCycleFactors"/> 分别按「不纳入计算」与「账期跨度」处理）。
        /// </summary>
        public static int CycleMonthsFromName(string cycleName)
        {
            string name = (cycleName ?? string.Empty).Trim();
            if (name.Length == 0) { return 0; }
            if (name.Contains("一次性")) { return 0; }
            if (name.Contains("半年")) { return 6; }
            if (name.Contains("每年") || name.Contains("按年") || name.Contains("年缴")) { return 12; }
            if (name.Contains("每季") || name.Contains("季度")) { return 3; }
            if (name.Contains("每月") || name.Contains("按月") || name.Contains("月缴")) { return 1; }
            return 0;
        }

        /// <summary>是否为「一次性」计费周期（一次性不纳入计算规则）。</summary>
        public static bool IsOneTimeCycle(string cycleName)
        {
            return (cycleName ?? string.Empty).Trim().Contains("一次性");
        }

        /// <summary>
        /// CHG-v1.1.2-36：本次出账的周期派生取值（月数 / 天数 / 年数）——唯一口径：
        /// ① **一次性 → 均为 1**（规格计费周期不纳入计算规则：公式里无论用月数 / 年数 / 天数，都等效「不乘」），
        /// ② 固定周期（每月 1 / 每季 3 / 每半年 6 / 每年 12）→ 月数按月数、天数 = 月数 × 30、年数 = 月数 ÷ 12，
        /// ③ 自定义 / 未设置 → 回落账期起止日期跨度（月数 = 含首尾自然月、天数 = 实际天数、年数 = 月数 ÷ 12）。
        /// </summary>
        public static void ResolveCycleFactors(ChargeStandardSpecDto spec, BillingCycleDto cycle,
            out int months, out int days, out decimal years)
        {
            string name = spec == null ? null : spec.CycleName;
            if (IsOneTimeCycle(name))
            {
                months = 1; days = 1; years = 1m;
                return;
            }
            int fromSpec = CycleMonthsFromName(name);
            if (fromSpec > 0)
            {
                months = fromSpec;
                days = fromSpec * 30;
                years = Math.Round(fromSpec / 12m, 4, MidpointRounding.AwayFromZero);
                return;
            }
            months = cycle == null ? 1 : CountInclusiveMonths(cycle.StartDate, cycle.EndDate);
            days = cycle == null ? 1 : (cycle.EndDate.Date - cycle.StartDate.Date).Days + 1;
            if (days < 1) { days = 1; }
            years = Math.Round(months / 12m, 4, MidpointRounding.AwayFromZero);
        }

        // ---------- 3) 单位推导 ----------

        /// <summary>
        /// 单价单位 = 元 / 公式中「非固定值」变量的单位组合。
        /// 例：单价 × 台数 × 月数 → 元/台·月；单价 × 桶数 → 元/桶；单价 × 数量（固定值）→ 元。
        /// </summary>
        public static string DerivePriceUnit(IList<ChargeVariableDto> variables, string formula)
        {
            if (string.IsNullOrWhiteSpace(formula)) { return "元"; }
            var units = new List<string>();
            foreach (int id in ParseTokens(formula))
            {
                ChargeVariableDto variable = (variables ?? new List<ChargeVariableDto>()).FirstOrDefault(x => x.Id == id);
                if (variable == null || variable.Source == ChargeVariableSource.Fixed) { continue; }
                string unit = (variable.Unit ?? string.Empty).Trim();
                if (unit.Length > 0 && !units.Contains(unit)) { units.Add(unit); }
            }
            return units.Count == 0 ? "元" : "元/" + string.Join("·", units);
        }

        /// <summary>公式中引用到的变量（含名称 / 单位快照，落库用）。</summary>
        public static List<ChargeFormulaVarDto> CollectFormulaVars(IList<ChargeVariableDto> variables, string formula)
        {
            var used = new List<ChargeFormulaVarDto>();
            foreach (int id in ParseTokens(formula))
            {
                ChargeVariableDto variable = (variables ?? new List<ChargeVariableDto>()).FirstOrDefault(x => x.Id == id);
                if (variable == null || used.Any(x => x.Id == id)) { continue; }
                used.Add(new ChargeFormulaVarDto { Id = variable.Id, Name = variable.VarName, Unit = variable.Unit });
            }
            return used;
        }

        // ---------- 4) 公式记号 ----------

        /// <summary>把公式里的 {v:ID} 记号重写为变量名（求值用）。未识别的记号原样保留，由求值给出可读错误。</summary>
        public static string RewriteForEvaluation(string formula, IList<ChargeVariableDto> variables)
        {
            if (string.IsNullOrWhiteSpace(formula)) { return formula; }
            return TokenRegex.Replace(formula, m =>
            {
                int id;
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) { return m.Value; }
                ChargeVariableDto variable = (variables ?? new List<ChargeVariableDto>()).FirstOrDefault(x => x.Id == id);
                return variable == null ? m.Value : (variable.VarName ?? string.Empty).Trim();
            });
        }

        /// <summary>公式变量引用 ID 集合。</summary>
        public static List<int> ParseTokens(string formula)
        {
            var ids = new List<int>();
            if (string.IsNullOrWhiteSpace(formula)) { return ids; }
            foreach (Match match in TokenRegex.Matches(formula))
            {
                int id;
                if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && !ids.Contains(id))
                {
                    ids.Add(id);
                }
            }
            return ids;
        }

        /// <summary>构造变量记号（客户端插入变量与接口保存共用）。</summary>
        public static string Token(int variableId)
        {
            return "{v:" + variableId.ToString(CultureInfo.InvariantCulture) + "}";
        }
    }
}
