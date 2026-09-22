using System;
using System.Collections.Generic;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Contract.Finance
{
    /// <summary>收费项目（t_charge_item，UC-FIN-001，BR-FIN-03）。</summary>
    public class ChargeItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public ChargePayMode PayMode { get; set; }
        public decimal UnitPrice { get; set; }
        public BillingCycleType CycleType { get; set; }
        public int Status { get; set; }
        public ChargeObjectType ObjectType { get; set; }   // CHG-M4-09：适用对象（房产/车位），由计价方式派生
        /// <summary>
        /// CHG-v1.1.0-17：缴费对象字典项编码（charge_object：property/parking/owner 为系统固定三项，
        /// 其余为自定义项）；为空表示历史数据（按 ObjectType 展示）。
        /// </summary>
        public string ObjectCode { get; set; }
        /// <summary>CHG-v1.1.0-17：缴费对象显示名（字典项名称；缺失时按 ObjectType 回落「房产/车位/业主」）。</summary>
        public string ObjectName { get; set; }

        // T4F-1-5：收费项目类别/计价方式/单价单位/自定义周期（CHG-M4-11）
        public string Category { get; set; }      // 类别（收费项目类别字典名，如 物业费/代收代缴）
        public string MethodCode { get; set; }    // 计价方式字典 code（area/house/parking/share/onetime/自定义）
        public string MethodName { get; set; }    // 计价方式显示名（按建筑面积/按户/按车位…）
        public string PriceUnit { get; set; }     // 单价单位（㎡/户/车位/张…）
        public string CycleName { get; set; }     // 自定义计费周期名（如 每季/每半年）；内置周期为空

        /// <summary>
        /// CHG-v1.1.2-06：用户自定义计价公式；为空 = 沿用内置计价方式口径。
        /// 生成账单时优先按公式计算金额（可用变量：单价 / 面积 / 月数 / 天数 / 数量）。
        /// </summary>
        public string Formula { get; set; }

        /// <summary>CHG-v1.1.2-26：绑定的收费标准（价目表主体）；为空 = 未搬迁的历史项目。</summary>
        public int? StandardId { get; set; }
        /// <summary>CHG-v1.1.2-26：收费标准名称（列表展示用）。</summary>
        public string StandardName { get; set; }
        /// <summary>CHG-v1.1.2-26：该收费标准下的启用规格数与总规格数。</summary>
        public int SpecCount { get; set; }
        /// <summary>FIX-v1.1.2-03：启用中的规格数（默认单价列「起」标注依据）。</summary>
        public int EnabledSpecCount { get; set; }
        /// <summary>CHG-v1.1.2-26：出账时可改价（仅一次性 / 自定义项目可开启）。</summary>
        public bool AllowPriceOverride { get; set; }
        /// <summary>CHG-v1.1.2-26：规格价格概览（如「住宅 ¥1.50 · 商铺 ¥3.00」）。</summary>
        public string SpecPriceText { get; set; }
    }

    /// <summary>
    /// CHG-v1.1.2-26：计量变量（t_charge_variable）。
    /// 计量单位与计算规则统一由变量驱动：单价单位 = 元 / 公式中变量的单位组合。
    /// </summary>
    public class ChargeVariableDto
    {
        public int Id { get; set; }
        public string VarCode { get; set; }
        public string VarName { get; set; }
        public string Unit { get; set; }
        public ChargeVariableValueType ValueType { get; set; }
        public ChargeVariableSource Source { get; set; }
        public decimal? DefaultValue { get; set; }
        public ChargeVariableScope ObjectScope { get; set; }
        /// <summary>档案自动类变量绑定的档案字段（内置专用，如 property.area）。</summary>
        public string FieldKey { get; set; }
        /// <summary>内置变量可停用、不可删除。</summary>
        public bool IsBuiltin { get; set; }
        public string Remark { get; set; }
        public int Status { get; set; }
        public int Sort { get; set; }
        /// <summary>被多少个收费标准的公式引用（删除 / 停用校验用）。</summary>
        public int UsedCount { get; set; }
        /// <summary>该变量在所属收费标准中是否为用户自建（额度校验与界面标识用）。</summary>
        public bool IsCustom { get; set; }
    }

    /// <summary>CHG-v1.1.2-26：公式变量引用（落库使用 ID + 名称快照，改名不影响历史公式）。</summary>
    public class ChargeFormulaVarDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
    }

    /// <summary>CHG-v1.1.2-26：收费标准规格明细（t_charge_standard_spec）。</summary>
    public class ChargeStandardSpecDto
    {
        public int Id { get; set; }
        public int StandardId { get; set; }
        public string SpecName { get; set; }
        /// <summary>匹配维度：房产用途 0 住宅 1 商铺；NULL = 不限。</summary>
        public int? MatchUsage { get; set; }
        /// <summary>匹配维度：房产状态 0 空置 1 入住 2 装修中；NULL = 不限。</summary>
        public int? MatchStatus { get; set; }
        /// <summary>匹配维度：车位类型 0 产权 1 人防 2 临时；NULL = 不限。</summary>
        public int? MatchSpaceType { get; set; }
        /// <summary>匹配维度：楼栋号；NULL = 不限。</summary>
        public string MatchBuilding { get; set; }
        /// <summary>兜底规格（其它规格都未命中时使用）。</summary>
        public bool IsFallback { get; set; }
        public decimal UnitPrice { get; set; }
        public string Formula { get; set; }
        /// <summary>公式引用的变量（ID + 名称快照）。</summary>
        public List<ChargeFormulaVarDto> FormulaVars { get; set; }
        public string PriceUnit { get; set; }
        public string CycleName { get; set; }
        public string EffectiveFrom { get; set; }
        public string Remark { get; set; }
        public int Status { get; set; }
        /// <summary>公式变量引用的持久化形态（JSON，落库用；对外读取请用 <see cref="FormulaVars"/>）。</summary>
        public string FormulaVarsJson { get; set; }
    }

    /// <summary>CHG-v1.1.2-26：收费标准（价目表主体，t_charge_standard）。</summary>
    public class ChargeStandardDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Remark { get; set; }
        public int Status { get; set; }
        public List<ChargeStandardSpecDto> Specs { get; set; }
        /// <summary>该收费标准引用的计量变量。</summary>
        public List<ChargeVariableDto> Variables { get; set; }
        /// <summary>被多少个收费项目引用。</summary>
        public int ItemCount { get; set; }
    }

    /// <summary>CHG-v1.1.2-26：规格匹配结果（出账试算与生成共用）。</summary>
    public class ChargeSpecMatchDto
    {
        public int SpecId { get; set; }
        public string SpecName { get; set; }
        public bool IsFallback { get; set; }
        public decimal UnitPrice { get; set; }
        public string Formula { get; set; }
        public string PriceUnit { get; set; }
        /// <summary>未命中任何规格且无兜底时为 false，该行进入失败明细。</summary>
        public bool Matched { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>CHG-v1.1.2-26：出账试算行（账单工作台「出账预演」：规格自动匹配 + 金额试算）。</summary>
    public class BillPreviewRowDto
    {
        public string ObjectKey { get; set; }
        public string ObjectText { get; set; }
        public string SpecName { get; set; }
        public decimal UnitPrice { get; set; }
        public string PriceUnit { get; set; }
        /// <summary>CHG-v1.1.2-50：本次出账行是否使用「出账时可改价」的单价（界面据此标注「改价」）。</summary>
        public bool UnitPriceOverridden { get; set; }
        public string Formula { get; set; }
        /// <summary>计量取值展示文本（如「面积 24㎡ · 月数 3」）。</summary>
        public string MeasureText { get; set; }
        public decimal Amount { get; set; }
        public bool Matched { get; set; }
        public bool IsFallback { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>CHG-v1.1.2-26：出账试算结果。</summary>
    public class BillPreviewResult
    {
        public List<BillPreviewRowDto> Rows { get; set; }
        public int MatchedCount { get; set; }
        public int FallbackCount { get; set; }
        public int FailedCount { get; set; }
        public decimal TotalAmount { get; set; }
    }




    /// <summary>账单生成失败清单行（CHG-M4-08：BR-FIN-01 失败对象 + 原因，FL-FIN-01 重推依据）。</summary>
    public class BillFailureDto
    {
        public int? PropertyId { get; set; }
        public int? ParkingId { get; set; }
        /// <summary>CHG-v1.1.0-11：业主缴费对象（业主直缴失败行）。</summary>
        public int? OwnerId { get; set; }
        /// <summary>CHG-v1.1.0-21：自定义缴费对象名称（该类失败行重推时按名称重新出账）。</summary>
        public string PayerName { get; set; }
        public string No { get; set; }
        public string Reason { get; set; }
        /// <summary>
        /// CHG-v1.1.2-50：失败行重推（FL-FIN-01）所需的本行出账输入快照
        /// —— 规格 / 手填计量参数 / 出账改价单价。
        /// 不记录的话，重推会丢失用户填写的规格与改后单价，按价目表默认价重出，金额与用户本意不符。
        /// </summary>
        public int? SpecId { get; set; }
        public Dictionary<int, decimal> Measures { get; set; }
        public decimal? UnitPriceOverride { get; set; }
    }
    /// <summary>账单列表行（CHG-M4-07：含业主/房产/收费项目/周期展示字段，PG-FIN-02/03 使用）。</summary>
    public class BillListItemDto
    {
        public int Id { get; set; }
        public int ChargeItemId { get; set; }
        public string ChargeItemName { get; set; }
        public int? PropertyId { get; set; }
        public int? ParkingId { get; set; }
        /// <summary>CHG-v1.1.0-11：业主直缴账单的缴费对象（与 property/parking 互斥）。</summary>
        public int? OwnerId { get; set; }
        public string PropertyNo { get; set; }
        /// <summary>CHG-v1.1.0-12：楼栋（收款登记缴费对象展示「姓名 → 楼栋 → 房号」）。</summary>
        public string BuildingNo { get; set; }
        /// <summary>CHG-v1.1.0-12：房号。</summary>
        public string RoomNo { get; set; }

        /// <summary>
        /// CHG-v1.2.0-33：单元号（业主-房产关系绑定的房产若带单元，取该房产的单元）。
        /// 与 BuildingNo / RoomNo 同源同序（同一套主房产），供收款登记 / 退款页的
        /// 「楼栋/单元/房号」组合展示用。
        /// </summary>
        public string UnitNo { get; set; }

        /// <summary>CHG-v1.1.0-12：车位编号（车位账单展示用）。</summary>
        public string SpaceNo { get; set; }
        /// <summary>
        /// CHG-v1.1.0-13：缴费人（业主）ID —— 直缴取 owner_id，房产取有效业主关系，车位取绑定业主。
        /// 收款登记按此聚合「一个业主一行，列出其全部欠费项目」。
        /// </summary>
        public int? PayerOwnerId { get; set; }
        /// <summary>
        /// CHG-v1.1.0-18：自定义缴费对象账单的缴费人名称（property/parking/owner 均为空时有效）。
        /// 收款登记按该名称聚合「一个自定义缴费对象一行」。
        /// </summary>
        public string PayerName { get; set; }
        public string OwnerName { get; set; }
        public int CycleId { get; set; }
        public string CyclePeriod { get; set; }
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public BillStatus Status { get; set; }
        public DateTime DueAt { get; set; }
        public int? GenerateBatchId { get; set; }
        public bool DelFlag { get; set; }
        /// <summary>CHG-v1.1.2-26：出账命中的规格（历史账单为空）。</summary>
        public int? ChargeSpecId { get; set; }
        /// <summary>CHG-v1.1.2-26：计量取值快照（JSON，键为变量名）。</summary>
        public string MeasureSnapshot { get; set; }
        /// <summary>CHG-v1.1.2-26：出账时使用的单价快照。</summary>
        public decimal? UnitPriceSnapshot { get; set; }
        /// <summary>CHG-v1.1.2-26：出账时使用的公式快照。</summary>
        public string FormulaSnapshot { get; set; }
    }
    /// <summary>计费周期（t_billing_cycle）。</summary>
    public class BillingCycleDto
    {
        public int Id { get; set; }
        public BillingCycleType CycleType { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    /// <summary>应收账单（t_bill，UC-FIN-002/007，BR-FIN-01/07）。</summary>
    public class BillDto
    {
        public int Id { get; set; }
        public int ChargeItemId { get; set; }
        public int? PropertyId { get; set; }
        public int? ParkingId { get; set; }
        /// <summary>CHG-v1.1.0-11：业主直缴账单（与 property/parking 三者互斥）。</summary>
        public int? OwnerId { get; set; }
        /// <summary>
        /// CHG-v1.1.0-18：自定义缴费对象账单的缴费人名称（租户/广告商/外部单位等无档案对象，由用户手工填写）。
        /// 与 property_id / parking_id / owner_id 互斥。
        /// </summary>
        public string PayerName { get; set; }
        public int CycleId { get; set; }
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public BillStatus Status { get; set; }
        public DateTime DueAt { get; set; }
        public int? GenerateBatchId { get; set; }
        public bool DelFlag { get; set; }
        /// <summary>CHG-v1.1.2-26：出账命中的规格（历史账单为空）。</summary>
        public int? ChargeSpecId { get; set; }
        /// <summary>CHG-v1.1.2-26：计量取值快照（JSON，键为变量名）。</summary>
        public string MeasureSnapshot { get; set; }
        /// <summary>CHG-v1.1.2-26：出账时使用的单价快照。</summary>
        public decimal? UnitPriceSnapshot { get; set; }
        /// <summary>CHG-v1.1.2-26：出账时使用的公式快照。</summary>
        public string FormulaSnapshot { get; set; }
    }

    /// <summary>缴费记录（t_payment，UC-FIN-003，BR-FIN-02）。</summary>
    public class PaymentDto
    {
        public int Id { get; set; }
        public int BillId { get; set; }
        /// <summary>CHG-v1.1.0-12：统一收款流水号（同一批多账单收款共享；单笔收款为 NULL）。</summary>
        public string BatchNo { get; set; }
        public decimal Amount { get; set; }
        public PayMethod PayMethod { get; set; }
        public DateTime PaidAt { get; set; }
        public PaymentStatus Status { get; set; }
        public decimal ToPreDeposit { get; set; }

        /// <summary>收款备注（T4R-3：随收款登记落库）。</summary>
        public string Remark { get; set; }
    }

    /// <summary>统一收款结果（CHG-v1.1.0-12）。</summary>
    public class PaymentBatchResultDto
    {
        /// <summary>本次统一收款流水号（如 PAY-20260917-0001）。</summary>
        public string BatchNo { get; set; }
        /// <summary>实际入账的分账单收款记录（每张账单一条）。</summary>
        public List<PaymentDto> Payments { get; set; }
        public decimal TotalAmount { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// 收款登记「应缴明细·批量删除已结清记录」结果（CHG-v1.2.0-31）。
    /// 归档只影响应缴明细列表可见性；未结清记录会被拒绝并计入 SkippedUnsettled。
    /// </summary>
    public class BillArchiveResultDto
    {
        /// <summary>已归档（从应缴明细移除）的已结清账单条数。</summary>
        public int ArchivedCount { get; set; }

        /// <summary>被拒绝的账单条数（未结清、已删除或不存在）。</summary>
        public int SkippedCount { get; set; }

        /// <summary>被拒绝的明细说明（如「BILL-0007 未结清」），供页面提示。</summary>
        public List<string> SkippedItems { get; set; }

        /// <summary>结果文案（中文，页面直接展示）。</summary>
        public string Message { get; set; }
    }

    /// <summary>退款/减免/调整批量登记结果（CHG-v1.1.0-13：一组账单各登记一条记录）。</summary>
    public class RefundBatchResultDto
    {
        /// <summary>本次登记生成的记录（每张账单一条，各自申请编号）。</summary>
        public List<RefundAdjustmentDto> Items { get; set; }
        /// <summary>账单张数。</summary>
        public int Count { get; set; }
        /// <summary>合计金额（每张金额 × 张数）。</summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// CHG-v1.2.0-35：本次批量登记中，有多少张账单因「重新变为未结清」而**自动解除已结清归档**
        /// （这些账单会重新出现在收款登记「应缴明细」，可继续收款）。
        /// </summary>
        public int ArchiveReleasedCount { get; set; }
    }

    /// <summary>收据（t_receipt，UC-FIN-011，BR-FIN-08 收据号唯一）。</summary>
    public class ReceiptDto
    {
        public int Id { get; set; }
        public int PaymentId { get; set; }
        public string ReceiptNo { get; set; }
        public int PrintCount { get; set; }
        public DateTime? PrintedAt { get; set; }
    }

    /// <summary>退款/减免/调整记录（t_payment_refund，UC-FIN-004，BR-FIN-06/10）。</summary>
    public class RefundAdjustmentDto
    {
        public int Id { get; set; }
        public int BillId { get; set; }
        public RefundType RefundType { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; }
        public string RefNo { get; set; }
        /// <summary>CHG-v1.1.2-12：处理方式文本（落库留痕）。</summary>
        public string Method { get; set; }
        /// <summary>CHG-v1.1.2-12：调整方向 —— 0 非调整/未指定、1 调增补收（＋）、2 调减冲正（−）。</summary>
        public int AdjustDir { get; set; }
        public DateTime CreatedAt { get; set; }
        /// <summary>CHG-v1.1.2-41：登记经办人（落库 t_payment_refund.operator_name，供单据 PDF 与审计追溯）。</summary>
        public string OperatorName { get; set; }

        // T4F-4-1（CHG-M4-13）：附件 + 关联房产（记录表展示）
        public string AttachmentName { get; set; }
        public string AttachmentPath { get; set; }
        public string PropertyNo { get; set; }

        /// <summary>
        /// CHG-v1.2.0-35：本次退款/调整冲减后该账单**重新变为未结清**，其「已结清归档」标记被自动解除
        /// → 账单重新出现在收款登记「应缴明细」（可继续收款）。
        /// 背景：归档只针对「已结清」记录，退款会让归档记录重新欠费，若不解锁则下拉显示欠费笔数、
        /// 应缴明细却是空的（负责人 2026-09-22 反馈）。
        /// </summary>
        public bool ArchiveReleased { get; set; }
    }

    /// <summary>
    /// 退款/减免/调整单据详情（CHG-v1.1.2-41）：导出 PDF 留档用，
    /// 金额与账单口径**由服务端按单据主键回查**（不采信客户端传值），保证 PDF 与库内数据一致。
    /// </summary>
    public class RefundRecordDetailDto
    {
        public int Id { get; set; }
        public string RefNo { get; set; }
        public RefundType RefundType { get; set; }
        /// <summary>调整方向（0 非调整/未指定、1 调增补收、2 调减冲正）。</summary>
        public int AdjustDir { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; }
        public string Method { get; set; }
        public string OperatorName { get; set; }
        public DateTime CreatedAt { get; set; }
        public string AttachmentName { get; set; }

        /// <summary>关联账单主键；0 表示无关联账单的冲正/补收。</summary>
        public int BillId { get; set; }
        public decimal? BillAmount { get; set; }
        public decimal? BillPaidAmount { get; set; }
        public BillStatus? BillStatus { get; set; }
        public string ChargeItemName { get; set; }
        public string CyclePeriod { get; set; }

        // 缴费对象四件套（由 ReportService 组合成展示文本）
        public string PayerName { get; set; }
        public string OwnerName { get; set; }
        public string BuildingNo { get; set; }
        public string RoomNo { get; set; }
        public string SpaceNo { get; set; }
    }

    /// <summary>预存款（t_pre_deposit，P-06 简单版：多缴转存 + 自动抵扣 + 余额退还）。</summary>
    public class PreDepositDto
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public decimal Balance { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>支出分类（t_expense_category，UC-FIN-006）。</summary>
    public class ExpenseCategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string CategoryType { get; set; }
        public int Status { get; set; }
    }

    /// <summary>支出记录（t_expense，UC-FIN-005，软删除 BR-FIN-10）。</summary>
    public class ExpenseDto
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; }   // CHG-M4-07：列表展示
        public decimal Amount { get; set; }
        public DateTime ExpenseDate { get; set; }
        public string Note { get; set; }
        public string Payee { get; set; }        // T4F-5-1（CHG-M4-14）：收款方
        public int Status { get; set; }
    }

    /// <summary>支出关联对象（t_expense_object_rel）。</summary>
    public class ExpenseObjectRelDto
    {
        public int Id { get; set; }
        public int ExpenseId { get; set; }
        public ExpenseObjectType ObjectType { get; set; }
        public int ObjectId { get; set; }
    }

    /// <summary>账单生成批次（t_bill_generate_log，UC-FIN-002 失败清单）。</summary>
    public class BillGenerateLogDto
    {
        public int Id { get; set; }
        public DateTime GenerateAt { get; set; }
        public int Total { get; set; }
        public int Success { get; set; }
        public int Fail { get; set; }
        public string FailDetail { get; set; }

        /// <summary>CHG-v1.1.0-10：本次生成范围摘要（全选／指定 N 个／筛选条件），用于事后对账。</summary>
        public string ScopeSummary { get; set; }
    }

    /// <summary>缴费对象候选行（CHG-v1.1.0-10：生成账单弹窗选择器）。</summary>
    public class BillObjectCandidateDto
    {
        public int Id { get; set; }
        /// <summary>"property" 或 "parking"。</summary>
        public string Kind { get; set; }
        /// <summary>主文本：楼栋 单元 房号（房产）／车位编号（车位）。</summary>
        public string No { get; set; }
        /// <summary>副文本：建筑面积／用途（房产），车位类型／绑定房（车位）。</summary>
        public string SubText { get; set; }
        /// <summary>缴费人（房产取有效业主，车位取车位绑定业主）。</summary>
        public string OwnerName { get; set; }
        /// <summary>是否未绑定业主（仅作展示标记，生成时仍按 BR-INF-02 判定）。</summary>
        public bool NoOwner { get; set; }
        public decimal? Area { get; set; }
    }

    /// <summary>缴费对象候选查询结果（CHG-v1.1.0-10）。</summary>
    public class BillObjectQueryResult
    {
        public List<BillObjectCandidateDto> Items { get; set; }
        /// <summary>房产口径：因未绑定有效业主被隐藏的房产数量（车位口径恒为 0）。</summary>
        public int HiddenCount { get; set; }
        /// <summary>是否因超出返回上限被截断（请用关键字缩小范围）。</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>草稿批次既有缴费对象（CHG-v1.1.0-10：批次编辑回填）。</summary>
    public class BillObjectSelectionDto
    {
        public List<int> PropertyIds { get; set; }
        public List<int> ParkingIds { get; set; }
        /// <summary>CHG-v1.1.0-11：业主缴费对象（批次编辑回填）。</summary>
        public List<int> OwnerIds { get; set; }
    }

    /// <summary>账单状态变更记录（t_bill_status_log，只追加）。</summary>
    public class BillStatusLogDto
    {
        public int Id { get; set; }
        public int BillId { get; set; }
        public BillStatus OldStatus { get; set; }
        public BillStatus NewStatus { get; set; }
        public DateTime ChangedAt { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>报表/导出记录（t_report_log，UC-FIN-009/012）。</summary>
    public class ReportLogDto
    {
        public int Id { get; set; }
        public string ReportType { get; set; }
        public string Period { get; set; }
        public ExportFormat Format { get; set; }
        public string FilePath { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>已缴/未缴统计（UC-FIN-008）。</summary>
    public class PaymentStatisticsDto
    {
        public int TotalBills { get; set; }
        public decimal TotalAmount { get; set; }
        public int PaidCount { get; set; }
        public decimal PaidAmount { get; set; }
        public int UnpaidCount { get; set; }
        public decimal UnpaidAmount { get; set; }
    }

    /// <summary>月度/季度财务报表（UC-FIN-009）。</summary>
    public class FinancialReportDto
    {
        public string Period { get; set; }
        public decimal IncomeTotal { get; set; }
        public decimal ExpenseTotal { get; set; }
        public decimal Balance { get; set; }
        public List<ReportItemDto> Items { get; set; }
        public string ComparePeriod { get; set; }          // T4F-8-1：对比期间（环比口径）
        public decimal IncomeMomPercent { get; set; }      // T4F-8-1：本期收入环比 %
        public decimal ExpenseMomPercent { get; set; }     // T4F-8-1：本期支出环比 %
        public List<FinancialSummaryItemDto> SummaryItems { get; set; } // T4F-8-1：科目汇总表
    }

    /// <summary>报表明细行。</summary>

    /// <summary>财务报表科目汇总行（T4F-8-1：科目/本期金额/上期金额/环比/本季累计/备注）。</summary>
    public class FinancialSummaryItemDto
    {
        public string Category { get; set; }        // 科目：收费项目名 / 支出分类名 / 退款·冲减
        public decimal CurrentAmount { get; set; }  // 本期金额
        public decimal PreviousAmount { get; set; } // 上期金额
        public decimal? MoM { get; set; }           // 环比 %（上期为 0 时为空）
        public decimal QuarterTotal { get; set; }   // 本季累计
        public string Remark { get; set; }          // 备注（如 代收垃圾清运费）
    }
    public class ReportItemDto
    {
        public DateTime Date { get; set; }
        public string Type { get; set; }        // income / expense
        public string Category { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; }
    }

    /// <summary>收支明细流水（UC-FIN-010，流水只读）。</summary>
    public class LedgerEntryDto
    {
        public int Id { get; set; }
        public DateTime BizTime { get; set; }
        public string BizType { get; set; }     // payment / expense / refund
        public string BizNo { get; set; }
        public decimal InAmount { get; set; }
        public decimal OutAmount { get; set; }
        public string Summary { get; set; }
        public string PayMethod { get; set; }    // T4F-7-1：支付方式（现金/微信/银行转账/POS/原路退回）
        public string OperatorName { get; set; } // T4F-7-1：经手人（来源单据操作人）
        public string Subject { get; set; }      // T4F-7-1：科目（收款→收费项目名、支出→支出分类名、退款→冲销）
        public string OwnerName { get; set; }    // T4F-7-1：付款户主信息（收款/退款/红冲按账单解析房产或车位业主，支出为空）
        /// <summary>CHG-v1.1.2-05：楼栋/房号（车位显示车位编号，自定义缴费对象显示名称，支出为空）。</summary>
        public string ObjectText { get; set; }
    }

    /// <summary>欠费台账行（UC-FIN-007，账龄>90天标红）。</summary>
    public class ArrearDto
    {
        public int BillId { get; set; }
        public string OwnerName { get; set; }
        public string PropertyNo { get; set; }
        public string ChargeItemName { get; set; }
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal ArrearAmount { get; set; }
        public DateTime DueAt { get; set; }
        public int AgingDays { get; set; }
        public string RemindChannel { get; set; }

        /// <summary>
        /// CHG-v1.2.0-24：账单状态（见 <see cref="PropertyManagement.Contract.Enums.BillStatus"/>）。
        /// 台账页「状态」列据此显示 —— 逾期口径由服务端 MarkOverdue 在查询前统一落库
        /// （到期日之后超过 1 天才算逾期），台账不再出现「已逾期却显示未缴」。
        /// </summary>
        public int Status { get; set; }

        public string BuildingNo { get; set; }  // T4F-6-1：楼栋（筛选用，来源 t_building.building_no）

        /// <summary>
        /// CHG-v1.2.0-27：楼栋/房号（跨模块引用基础信息档案，口径同「收支明细流水」的「楼栋/房号/单元」列：
        /// `1号楼/1单元/101`，无单元则 `1号楼/101`）。
        /// 房产账单取本房产；车位账单与**业主直缴**账单「只要行上有业主」即按**业主-房产关系**回查主房产；
        /// 自定义缴费对象（无档案）为空。
        /// </summary>
        public string BuildingPath { get; set; }

        /// <summary>
        /// CHG-v1.2.0-27：缴费对象类型 —— `property`（房产）｜`parking`（车位）｜`owner`（业主直缴）｜
        /// `custom`（自定义缴费对象）。前端据此决定「业主」列留空的文案：
        /// 房产行为「—（空置）」，非业主类缴费对象为「—」（避免把广告商等外部对象误标成空置房产）。
        /// </summary>
        public string ObjectKind { get; set; }

        /// <summary>
        /// CHG-v1.1.2-54：账单期间起止（账单真实账期，来源 t_billing_cycle）。
        /// 台账「欠费期间」列改为直接引用该账期 —— 原实现按到期日倒推一个月推算，
        /// 按月账单看似正确，按年 / 一次性账单会显示成错误区间。
        /// </summary>
        public string CycleStart { get; set; }
        public string CycleEnd { get; set; }
    }

    /// <summary>
    /// 已移出欠费台账的记录（CHG-v1.1.2-03）。
    /// 语义：账单本身仍在（账单工作台/收款登记/退款/报表/流水一切不变），只是不再出现在欠费台账列表；
    /// 删除该剔除记录即「恢复台账」。
    /// </summary>
    public class ArrearDismissDto
    {
        public int Id { get; set; }
        public int BillId { get; set; }
        public string OwnerName { get; set; }
        public string PropertyNo { get; set; }
        public string ChargeItemName { get; set; }
        public decimal ArrearAmount { get; set; }
        public string Reason { get; set; }
        public string Operator { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>账单生成批次行（CHG-M4-10：PG-FIN-02 批次列表，状态由明细派生）。</summary>
    public class BillBatchDto
    {
        public int Id { get; set; }
        public string BatchNo { get; set; }
        public string ChargeItemName { get; set; }
        public int? ChargeItemId { get; set; }   // CHG-M4-12：草稿编辑回填
        public int? CycleId { get; set; }        // CHG-M4-12：草稿编辑回填
        public string CyclePeriod { get; set; }
        public int HouseCount { get; set; }
        public decimal TotalAmount { get; set; }
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        public int DraftCount { get; set; }
        public int PendingCount { get; set; }
        public int PartialCount { get; set; }
        public int PaidCount { get; set; }
        public DateTime GenerateAt { get; set; }
        public string PublishedAtRaw { get; set; }
        /// <summary>CHG-v1.1.0-21：失败对象重推时间（未重推为空）。</summary>
        public string RetriedAtRaw { get; set; }
        /// <summary>CHG-v1.1.0-21：本次重推成功户数（累计口径为最近一次）。</summary>
        public int RetriedCount { get; set; }
        /// <summary>CHG-v1.1.0-10：本次生成范围摘要（批次详情展示）。</summary>
        public string ScopeSummary { get; set; }

        public DateTime? PublishedAt
        {
            get
            {
                DateTime parsed;
                return DateTime.TryParse(PublishedAtRaw, out parsed) ? (DateTime?)parsed : null;
            }
        }

        /// <summary>
        /// 批次状态：Draft=草稿 / Published=已发布 / Partial=部分缴纳 / Failed=发布失败 / Retried=已重推。
        /// CHG-v1.1.0-21：失败对象全部重推成功后（fail=0 且 retried_at 非空、批次自身无账单）显示「已重推」，
        /// 不再计入「发布失败」卡片。
        /// </summary>
        public string Status
        {
            get
            {
                if (FailCount > 0 && SuccessCount == 0) return "Failed";
                if (!string.IsNullOrEmpty(RetriedAtRaw) && DraftCount == 0 && PendingCount == 0 &&
                    PartialCount == 0 && PaidCount == 0)
                {
                    return "Retried";
                }
                if (DraftCount > 0 && PendingCount == 0 && PartialCount == 0 && PaidCount == 0) return "Draft";
                if (PartialCount > 0) return "Partial";
                return "Published";
            }
        }

        /// <summary>CHG-v1.1.0-21：最近一次重推时间（未重推为空）。</summary>
        public DateTime? RetriedAt
        {
            get
            {
                DateTime parsed;
                return DateTime.TryParse(RetriedAtRaw, out parsed) ? (DateTime?)parsed : null;
            }
        }
    }
}
