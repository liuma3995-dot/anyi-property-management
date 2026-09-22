using System;
using System.Collections.Generic;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Contract.Finance
{
    public class ChargeItemRequest
    {
        public string Name { get; set; }
        public ChargePayMode PayMode { get; set; }
        public decimal UnitPrice { get; set; }
        public BillingCycleType CycleType { get; set; }
        /// <summary>
        /// 缴费对象（房产/车位/业主）。CHG-v1.1.0-16：由表单显式选择（null 时服务端按计价方式派生默认值）。
        /// 生成账单时按该字段校验缴费对象类型，避免「项目与对象不匹配却出账成功」。
        /// </summary>
        public ChargeObjectType? ObjectType { get; set; }
        /// <summary>
        /// CHG-v1.1.0-17：缴费对象字典项编码（charge_object）。提供时以字典为准：
        /// property/parking/owner → 系统固定三项；其余 → 自定义缴费对象（ChargeObjectType.Custom）。
        /// </summary>
        public string ObjectCode { get; set; }
        public int? Status { get; set; } // 停用不影响已出账单（UC-FIN-001）

        // T4F-1-5（CHG-M4-11）：类别/计价方式/单价单位/自定义周期
        public string Category { get; set; }
        public string MethodCode { get; set; }
        public string MethodName { get; set; }
        public string PriceUnit { get; set; }
        public string CycleName { get; set; }

        /// <summary>
        /// CHG-v1.1.2-06：用户自定义计价公式（如「单价 * 面积 * 月数」）。
        /// 为空 = 沿用内置计价方式口径（按建筑面积 = 单价 × 面积，其余 = 单价）。
        /// 可用变量：单价 / 面积 / 月数 / 天数 / 数量，运算符仅 + - * / ( )。
        /// </summary>
        public string Formula { get; set; }

        /// <summary>CHG-v1.1.2-26：绑定的收费标准（新增 / 编辑时的唯一价格来源）。</summary>
        public int? StandardId { get; set; }
        /// <summary>CHG-v1.1.2-26：出账时可改价（仅一次性 / 自定义项目允许开启）。</summary>
        public bool AllowPriceOverride { get; set; }
    }

    /// <summary>CHG-v1.1.2-26：计量变量新增 / 编辑请求。</summary>
    public class ChargeVariableRequest
    {
        public string VarName { get; set; }
        public string Unit { get; set; }
        public ChargeVariableValueType ValueType { get; set; }
        /// <summary>用户自建仅允许 Manual / Fixed（负责人裁定 ⑨）。</summary>
        public ChargeVariableSource Source { get; set; }
        public decimal? DefaultValue { get; set; }
        public ChargeVariableScope ObjectScope { get; set; }
        public string Remark { get; set; }
        public int? Status { get; set; }
    }

    /// <summary>CHG-v1.1.2-26：收费标准新增 / 编辑请求。</summary>
    public class ChargeStandardRequest
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string Remark { get; set; }
        public int? Status { get; set; }
        /// <summary>引用的计量变量 ID 列表（内置不占额度，用户自建 ≤ 5 个）。</summary>
        public List<int> VariableIds { get; set; }
    }

    /// <summary>CHG-v1.1.2-26：规格明细新增 / 编辑请求。</summary>
    public class ChargeStandardSpecRequest
    {
        public string SpecName { get; set; }
        public int? MatchUsage { get; set; }
        public int? MatchStatus { get; set; }
        public int? MatchSpaceType { get; set; }
        public string MatchBuilding { get; set; }
        public bool IsFallback { get; set; }
        public decimal UnitPrice { get; set; }
        /// <summary>计算公式，变量以 {v:ID} 记号引用；为空 = 单价 × 数量。</summary>
        public string Formula { get; set; }
        public string CycleName { get; set; }
        public string EffectiveFrom { get; set; }
        public string Remark { get; set; }
        public int? Status { get; set; }
        /// <summary>
        /// 改价专用：为 true 时保存新规格并自动把同一标准下「同名旧规格」置为停用（不原地改价）。
        /// </summary>
        public bool DeprecateSameName { get; set; }
        /// <summary>变更原因（改价必填，写入审计日志）。</summary>
        public string ChangeReason { get; set; }
    }

    /// <summary>
    /// CHG-v1.1.2-26：自定义缴费对象的出账行（无档案对象：规格手选 + 计量参数手填）。
    /// </summary>
    public class BillCustomPayerRequest
    {
        /// <summary>缴费对象名称（租户 / 广告商 / 外部单位，手工填写）。</summary>
        public string PayerName { get; set; }
        /// <summary>合同 / 备注编号（选填，用于区分同名对象）。</summary>
        public string ContractNo { get; set; }
        /// <summary>手工指定的规格 ID（收费标准下的某条规格）；为空时取该标准唯一启用规格。</summary>
        public int? SpecId { get; set; }
        /// <summary>计量取值：键 = 计量变量 ID，值 = 用户填写或系统带出的数值。</summary>
        public Dictionary<int, decimal> Measures { get; set; }
        /// <summary>
        /// CHG-v1.1.2-50：出账时改价（仅「出账时可改价」开启的收费项目可提交）。
        /// 为空＝按价目表规格单价计价（默认）；有值＝本次出账按该单价计价并写入账单快照。
        /// </summary>
        public decimal? UnitPriceOverride { get; set; }
    }

    /// <summary>CHG-v1.1.2-26：出账试算请求（只读，不落库；与生成账单同口径）。</summary>
    public class BillPreviewRequest
    {
        public int ChargeItemId { get; set; }
        public int CycleId { get; set; }
        public List<int> PropertyIds { get; set; }
        public List<int> ParkingIds { get; set; }
        public List<int> OwnerIds { get; set; }
        public List<BillCustomPayerRequest> CustomPayers { get; set; }
        /// <summary>CHG-v1.1.2-34：档案对象（房产 / 车位 / 业主）出账行手填的计量参数。</summary>
        public List<BillObjectMeasureRequest> ObjectMeasures { get; set; }
    }

    /// <summary>
    /// CHG-v1.1.2-34：档案对象的出账计量参数（价目表口径：来源=手填 的变量在出账表单按对象逐行填写）。
    /// 与自定义缴费对象的 <see cref="BillCustomPayerRequest.Measures"/> 同口径，仅键不同（按对象类型 + ID）。
    /// </summary>
    public class BillObjectMeasureRequest
    {
        /// <summary>缴费对象类型：property（房产）｜parking（车位）｜owner（业主）。</summary>
        public string Kind { get; set; }
        public int ObjectId { get; set; }
        /// <summary>计量取值：键 = 计量变量 ID，值 = 出账行手填值（未填的变量不提交）。</summary>
        public Dictionary<int, decimal> Measures { get; set; }
        /// <summary>
        /// CHG-v1.1.2-50：出账时改价（仅「出账时可改价」开启的收费项目可提交）。
        /// 为空＝按价目表规格单价计价（默认）；有值＝该缴费对象本次出账按此单价计价并写入账单快照。
        /// </summary>
        public decimal? UnitPriceOverride { get; set; }
        /// <summary>
        /// CHG-v1.2.0-12：档案对象（房产 / 车位 / 业主）的出账行**手选规格**。
        /// 为空＝按价目表自动匹配（原有口径）；有值＝该行按此规格计价并写入账单快照。
        /// 背景（负责人 2026-09-21）：同一价目表里不同规格价格不同，自动匹配只认「适用条件 + 兜底」，
        /// 现场需要按行指定规格（如不同楼栋/车位类型走不同价）。
        /// </summary>
        public int? SpecId { get; set; }
    }

    /// <summary>CHG-v1.1.2-26：启停请求（0 启用 / 1 停用），收费标准、规格、计量变量共用。</summary>
    public class ChargeStandardStatusRequest
    {
        public int Status { get; set; }
    }

    /// <summary>CHG-v1.1.2-33：收费项目清单导出请求（PDF / Excel）。</summary>
    public class ChargeItemExportRequest
    {
        public ExportFormat Format { get; set; }
        public string Keyword { get; set; }
        public string Category { get; set; }
    }

    /// <summary>轻量字典项新增请求（T4F-1-5：自定义类别/计价方式/计费周期，CHG-M4-11）。</summary>
    public class DictItemCreateRequest
    {
        public string ItemName { get; set; }   // 自定义项名称（必填）
        public string Remark { get; set; }     // 附加信息（自定义计价方式单位文本，如 张；可选）
    }

    public class BillingCycleRequest
    {
        public BillingCycleType CycleType { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    /// <summary>
    /// 账单生成请求（UC-FIN-002：按收费项目 + 周期 + 对象范围）。
    /// CHG-v1.1.0-10：缴费对象由用户显式选择，**空集合＝校验失败**，不再表示「全部对象」。
    /// CHG-v1.1.0-11：缴费对象增加「业主」维度（OwnerIds），用于办卡费/清理费/维修费等面向业主本人的收费项目。
    /// </summary>
    public class BillGenerateRequest
    {
        public int ChargeItemId { get; set; }
        public int CycleId { get; set; }
        public List<int> PropertyIds { get; set; }
        public List<int> ParkingIds { get; set; }
        public List<int> OwnerIds { get; set; }
        /// <summary>
        /// CHG-v1.1.0-18：自定义缴费对象（租户/广告商/外部单位等无档案对象）手工填写的缴费人名称列表。
        /// 仅当收费项目的缴费对象为自定义时使用：一行名称生成一张账单，property/parking/owner 三类 ID 全为空。
        /// </summary>
        public List<string> CustomPayerNames { get; set; }
        /// <summary>
        /// CHG-v1.1.2-26：自定义缴费对象的出账明细（规格 + 计量参数）。
        /// 为空时回落到 CustomPayerNames 的旧口径（单价 × 公式默认值）。
        /// </summary>
        public List<BillCustomPayerRequest> CustomPayers { get; set; }
        /// <summary>
        /// CHG-v1.1.2-34：档案对象（房产 / 车位 / 业主）出账行手填的计量参数。
        /// 价目表公式引用的「手填」变量在档案对象下没有取值来源，由用户在生成账单表单逐行填写。
        /// </summary>
        public List<BillObjectMeasureRequest> ObjectMeasures { get; set; }
    }

    /// <summary>
    /// 缴费对象候选查询（CHG-v1.1.0-10：生成账单弹窗选择器，只读）。
    /// </summary>
    public class BillObjectQueryRequest
    {
        /// <summary>候选类型："property"（房产，默认）｜"parking"（车位）｜"owner"（业主）。</summary>
        public string Kind { get; set; }

        /// <summary>关键字（楼栋／单元／房号／车位编号／业主姓名／手机号；可空）。</summary>
        public string Keyword { get; set; }
    }

    /// <summary>账单发布请求（草稿→发布，失败清单重推）。</summary>
    public class BillPublishRequest
    {
        public int BatchId { get; set; }
    }
    /// <summary>账单批次删除请求（CHG-M4-16：仅草稿/发布失败批次可删除，保留审计轨迹）。</summary>
    public class BillBatchDeleteRequest
    {
        public int BatchId { get; set; }
    }

    /// <summary>账单查询条件（UC-FIN-007 欠费台账/账单列表，分页）。</summary>
    public class BillQueryRequest : PageRequest
    {
        public BillStatus? Status { get; set; }
        public int? PropertyId { get; set; }
        /// <summary>CHG-v1.1.0-11：按业主直缴账单过滤（收款侧按缴费对象取账单）。</summary>
        public int? OwnerId { get; set; }
        /// <summary>
        /// CHG-v1.1.0-13：按缴费人（业主）过滤 —— 覆盖其名下房产、车位与业主直缴的全部账单，
        /// 收款登记「一个业主一行」即用该口径取应缴明细。
        /// </summary>
        public int? PayerOwnerId { get; set; }
        /// <summary>
        /// CHG-v1.1.0-18：按自定义缴费对象名称过滤（收款登记「一个自定义缴费对象一行」的应缴明细口径）。
        /// </summary>
        public string PayerName { get; set; }
        public int? ChargeItemId { get; set; }
        public DateTime? DueFrom { get; set; }
        public DateTime? DueTo { get; set; }
        public bool ArrearsOnly { get; set; }

        /// <summary>
        /// CHG-v1.2.0-31：排除「已归档」账单（收款登记「应缴明细」记录管理口径）。
        /// 归档只影响本列表可见性，账单与收款/退款/财报/流水/业主档案数据完全不变；
        /// 其它模块（账单工作台等）不传该标记，保持原行为。
        /// </summary>
        public bool ExcludeArchived { get; set; }
    }

    /// <summary>
    /// 收款登记「应缴明细」批量删除已结清记录（CHG-v1.2.0-31）。
    /// 语义 = 归档：只把账单从「应缴明细」列表移除，**不触碰**账单本身、
    /// 收款记录、退款记录、财务报表、收支明细流水与业主档案缴费概况。
    /// 未结清（含部分缴 / 逾期 / 未缴）的账单会被服务端逐条拒绝。
    /// </summary>
    public class SettledBillArchiveRequest
    {
        /// <summary>要清理的账单主键（来自「应缴明细」已结清行）。</summary>
        public List<int> BillIds { get; set; }
    }

    /// <summary>
    /// 收款登记「应缴明细」导出 PDF 请求（CHG-v1.2.0-32）。
    /// 口径 = 当前所选缴费对象的**全部应缴明细（含已结清）**，
    /// 供用户在清理（归档）记录之前先导出归档。
    /// </summary>
    public class ArrearDetailExportRequest
    {
        /// <summary>缴费人（业主）主键（与 PayerName 二选一，优先级高于对象维度）。</summary>
        public int? PayerOwnerId { get; set; }

        /// <summary>自定义缴费对象名称（租户/广告商等无档案对象）。</summary>
        public string PayerName { get; set; }

        /// <summary>无缴费人时的兜底对象维度：业主直缴账单的业主主键。</summary>
        public int? OwnerId { get; set; }

        /// <summary>无缴费人时的兜底对象维度：房产（或车位）主键。</summary>
        public int? PropertyId { get; set; }

        /// <summary>缴费对象展示名（业主姓名 / 自定义缴费对象名称），仅用于 PDF 抬头。</summary>
        public string PayerDisplay { get; set; }
    }

    /// <summary>收款登记请求（UC-FIN-003：全额/部分缴/超额转预存 P-06）。</summary>
    public class PaymentCreateRequest
    {
        public int BillId { get; set; }
        public decimal Amount { get; set; }
        public PayMethod PayMethod { get; set; }
        public bool PrintReceipt { get; set; }

        /// <summary>收款备注（T4R-3：可空，随收款落库并进入审计留痕）。</summary>
        public string Remark { get; set; }
    }

    /// <summary>
    /// 统一收款请求（CHG-v1.1.0-12）：一次对同一缴费对象下的多个账单收款。
    /// 每条明细仍按账单逐条落库（账单号/收据号不变），共享同一收款流水号 BatchNo。
    /// </summary>
    public class PaymentBatchCreateRequest
    {
        public List<PaymentBatchItemRequest> Items { get; set; }
        public PayMethod PayMethod { get; set; }
        public bool PrintReceipt { get; set; }
        public string Remark { get; set; }
    }

    /// <summary>统一收款明细行（账单 + 本次收款金额）。</summary>
    public class PaymentBatchItemRequest
    {
        public int BillId { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// 收据打印模板导出请求（CHG-v1.1.0-14）。
    /// 说明：收据号已在界面下线，导出模板以「收款流水号 + 逐项明细」为口径，
    /// 统一收款时把同批每张账单的项目/期间/账单号逐行写清。
    /// </summary>
    public class ReceiptTemplateRequest
    {
        public string PayeeName { get; set; }
        public string HandlerName { get; set; }
        public string PayMethod { get; set; }
        public DateTime PaidAt { get; set; }
        public string Remark { get; set; }
        /// <summary>统一收款流水号（单张收款可空）。</summary>
        public string BatchNo { get; set; }
        /// <summary>
        /// CHG-v1.1.0-19：隐藏「缴费对象」列 —— 自定义缴费对象场景下缴款人与缴费对象为同一名称，
        /// 同名列会重复展示产生歧义（服务端仍会按「全部明细与缴款人同名」二次确认后隐藏）。
        /// </summary>
        public bool HideObjectColumn { get; set; }
        public List<ReceiptTemplateItemRequest> Items { get; set; }
    }

    /// <summary>收据模板明细行（一张账单一行）。</summary>
    public class ReceiptTemplateItemRequest
    {
        public string BillNo { get; set; }
        public string ObjectText { get; set; }
        public string ChargeItemName { get; set; }
        public string CyclePeriod { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>收据打印请求（UC-FIN-011：补打保留原号 BR-FIN-08）。</summary>
    public class ReceiptPrintRequest
    {
        public int ReceiptId { get; set; }
    }

    /// <summary>退款/减免/调整请求（UC-FIN-004，超额阻止 BR-FIN-06）。</summary>
    public class RefundAdjustmentRequest
    {
        public int BillId { get; set; }
        /// <summary>
        /// CHG-v1.1.0-13：关联账单多选（退款/减免/调整支持一次登记多张账单）。
        /// 口径：对每张账单各登记一条记录，金额取本请求的 <see cref="Amount"/>（即「每张金额」），
        /// 各记录保留各自账单号与申请编号，便于逐张追溯。
        /// </summary>
        public List<int> BillIds { get; set; }
        public RefundType RefundType { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; }

        /// <summary>
        /// CHG-v1.1.2-12：处理方式（退款＝「原路退回」等；减免＝「直接调减账单应收」；
        /// 账务调整＝「调增补收」「调减冲正」）。
        /// 账务调整时该字段决定 +/−：调增补收 = 收入方向，调减冲正 = 冲减方向。
        /// </summary>
        public string Method { get; set; }

        /// <summary>BR-FIN-10：大额退款需负责人确认标记（当前单角色下前端弹窗确认后置 true）。</summary>
        public bool ConfirmedByManager { get; set; }

        /// <summary>附件（T4F-4-1：≤5MB 必传，本期存文件名与路径）。</summary>
        public string AttachmentName { get; set; }

        public string AttachmentPath { get; set; }

        /// <summary>
        /// CHG-v1.2.0-37：**逐张金额**（多选 / 全选登记时使用）—— 金额输入框显示的是「合计」，
        /// 提交时按每张账单各自的登记上限（退款/调整＝实缴，减免＝未收余额）逐张核销。
        /// 为空时回落旧口径：<see cref="Amount"/> 视作「每张金额」。
        /// </summary>
        public List<RefundBatchItemRequest> Items { get; set; }
    }

    /// <summary>批量登记的逐张金额（CHG-v1.2.0-37）：账单主键 + 该张本次登记金额。</summary>
    public class RefundBatchItemRequest
    {
        public int BillId { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// 退款/减免/调整单据导出请求（CHG-v1.1.2-41）。
    /// 口径：只传单据主键 —— 金额、账单口径、经办人一律由服务端按主键回查，
    /// 保证导出的 PDF 与库内记录一致（不采信客户端传值）。
    /// </summary>
    public class RefundRecordExportRequest
    {
        /// <summary>退款/减免/调整记录主键（记录表行）。</summary>
        public int RefundId { get; set; }

        /// <summary>导出备注（可选，如「用于业委会备案」；写入 PDF 备注栏）。</summary>
        public string Remark { get; set; }
    }

    /// <summary>
    /// 业主档案导出 PDF 请求（CHG-v1.2.0-13）。
    /// 内容 = 本年度缴费概况 + 缴费明细记录（账单明细 + 收款明细）；金额与口径一律由服务端回查。
    /// </summary>
    public class OwnerProfileExportRequest
    {
        /// <summary>统计年度（按账单到期日所属年度）；为空 = 当前年度。</summary>
        public int? Year { get; set; }

        /// <summary>
        /// CHG-v1.2.0-17：业主主键。&gt;0 = 只导出该业主；为空 / 0 = **导出全部业主**的缴费明细记录。
        /// </summary>
        public int? OwnerId { get; set; }
    }

    /// <summary>预存款余额退还请求（P-06 简单版）。</summary>
    public class PreDepositRefundRequest
    {
        public int OwnerId { get; set; }
        public decimal Amount { get; set; }
    }

    public class ExpenseCategoryRequest
    {
        public string Name { get; set; }
        public string CategoryType { get; set; }
    }

    /// <summary>支出登记请求（UC-FIN-005，关联对象可空）。</summary>
    public class ExpenseCreateRequest
    {
        public int CategoryId { get; set; }
        public decimal Amount { get; set; }
        public DateTime ExpenseDate { get; set; }
        public string Note { get; set; }
        public string Payee { get; set; }        // T4F-5-1（CHG-M4-14）：收款方
        public List<ExpenseObjectRelDto> Objects { get; set; }
    }

    /// <summary>收支流水查询（UC-FIN-010，分页）。</summary>
    public class LedgerQueryRequest : PageRequest
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string BizType { get; set; }
        public string Subject { get; set; }  // T4F-7-1：科目筛选（收款→收费项目名、支出→支出分类名等）
    }

    /// <summary>财务报表查询（UC-FIN-009：month/quarter + 期间）。</summary>
    public class FinancialReportQueryRequest
    {
        public string PeriodType { get; set; } // month / quarter
        public string Period { get; set; }     // 2026-08 或 2026-Q3
        public string ComparePeriod { get; set; } // T4F-8-1：对比期间（2026-07 或 2026-Q2，默认上一期）
        public int? ChargeItemId { get; set; }    // T4F-8-1：收费项目筛选（null=全部）
        public bool? IncludeRefund { get; set; }  // T4F-8-1：含退费冲销（默认 true）
    }

    /// <summary>报表导出请求（UC-FIN-012，Excel/PDF，留痕 t_report_log）。</summary>
    public class ReportExportRequest
    {
        public FinancialReportQueryRequest Query { get; set; }
        public ExportFormat Format { get; set; }
    }
    /// <summary>失败清单重推（CHG-M4-04：FL-FIN-01，按批次重试失败对象）。</summary>
    public class BillRetryRequest
    {
        public int BatchId { get; set; }
    }

    /// <summary>欠费催缴渠道记录（CHG-M4-04：UC-FIN-007，渠道：电话/短信/上门/微信等）。</summary>
    public class ArrearRemindRequest
    {
        public int BillId { get; set; }
        public string Channel { get; set; }
        public string Note { get; set; }
    }

    /// <summary>
    /// 欠费台账「移出台账」请求（CHG-v1.1.2-03）。
    /// 语义：只把该账单行移出台账可见范围，不软删账单、不影响其它模块；可「恢复台账」。
    /// </summary>
    public class ArrearDismissRequest
    {
        /// <summary>移出台账的账单 id 列表（移出台账 / 批量移出）。</summary>
        public List<int> BillIds { get; set; }
        /// <summary>恢复台账：已移出记录的 id 列表（t_arrear_dismiss.id）。</summary>
        public List<int> DismissIds { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// 收支明细流水导出请求（CHG-v1.1.2-05）：沿用财务报表导出通道（ClosedXML / PDFsharp），留痕 t_report_log。
    /// </summary>
    public class LedgerExportRequest
    {
        public LedgerQueryRequest Query { get; set; }
        public ExportFormat Format { get; set; }
    }

    /// <summary>
    /// 支出登记明细导出请求（CHG-v1.2.0-25）。
    /// 口径与页面一致：导出**当前筛选条件下的支出明细**（关键字 / 类别 / 状态），不导出全库。
    /// 本期只落 PDF（Excel 总表仍由财务报表模块提供）。
    /// </summary>
    public class ExpenseExportRequest
    {
        public ExportFormat Format { get; set; }

        /// <summary>关键字：命中摘要 / 分类 / 收款方 / 支出编号（ZC-0001）。</summary>
        public string Keyword { get; set; }

        /// <summary>支出分类 id（0 / null = 全部分类）。</summary>
        public int? CategoryId { get; set; }

        /// <summary>
        /// 状态口径与页面下拉一致：0 = 全部，1 = 仅未删除（已支付），2 = 仅已删除。
        /// </summary>
        public int StatusFilter { get; set; }
    }
}
