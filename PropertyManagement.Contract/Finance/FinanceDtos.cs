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

        // T4F-1-5：收费项目类别/计价方式/单价单位/自定义周期（CHG-M4-11）
        public string Category { get; set; }      // 类别（收费项目类别字典名，如 物业费/代收代缴）
        public string MethodCode { get; set; }    // 计价方式字典 code（area/house/parking/share/onetime/自定义）
        public string MethodName { get; set; }    // 计价方式显示名（按建筑面积/按户/按车位…）
        public string PriceUnit { get; set; }     // 单价单位（㎡/户/车位/张…）
        public string CycleName { get; set; }     // 自定义计费周期名（如 每季/每半年）；内置周期为空
    }




    /// <summary>账单生成失败清单行（CHG-M4-08：BR-FIN-01 失败对象 + 原因，FL-FIN-01 重推依据）。</summary>
    public class BillFailureDto
    {
        public int? PropertyId { get; set; }
        public int? ParkingId { get; set; }
        public string No { get; set; }
        public string Reason { get; set; }
    }
    /// <summary>账单列表行（CHG-M4-07：含业主/房产/收费项目/周期展示字段，PG-FIN-02/03 使用）。</summary>
    public class BillListItemDto
    {
        public int Id { get; set; }
        public int ChargeItemId { get; set; }
        public string ChargeItemName { get; set; }
        public int? PropertyId { get; set; }
        public int? ParkingId { get; set; }
        public string PropertyNo { get; set; }
        public string OwnerName { get; set; }
        public int CycleId { get; set; }
        public string CyclePeriod { get; set; }
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public BillStatus Status { get; set; }
        public DateTime DueAt { get; set; }
        public int? GenerateBatchId { get; set; }
        public bool DelFlag { get; set; }
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
        public int CycleId { get; set; }
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public BillStatus Status { get; set; }
        public DateTime DueAt { get; set; }
        public int? GenerateBatchId { get; set; }
        public bool DelFlag { get; set; }
    }

    /// <summary>缴费记录（t_payment，UC-FIN-003，BR-FIN-02）。</summary>
    public class PaymentDto
    {
        public int Id { get; set; }
        public int BillId { get; set; }
        public decimal Amount { get; set; }
        public PayMethod PayMethod { get; set; }
        public DateTime PaidAt { get; set; }
        public PaymentStatus Status { get; set; }
        public decimal ToPreDeposit { get; set; }

        /// <summary>收款备注（T4R-3：随收款登记落库）。</summary>
        public string Remark { get; set; }
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
        public DateTime CreatedAt { get; set; }

        // T4F-4-1（CHG-M4-13）：附件 + 关联房产（记录表展示）
        public string AttachmentName { get; set; }
        public string AttachmentPath { get; set; }
        public string PropertyNo { get; set; }
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
        public string BuildingNo { get; set; }  // T4F-6-1：楼栋（筛选用，来源 t_building.building_no）
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

        public DateTime? PublishedAt
        {
            get
            {
                DateTime parsed;
                return DateTime.TryParse(PublishedAtRaw, out parsed) ? (DateTime?)parsed : null;
            }
        }

        /// <summary>批次状态：Draft=草稿 / Published=已发布 / Partial=部分缴纳 / Failed=发布失败。</summary>
        public string Status
        {
            get
            {
                if (FailCount > 0 && SuccessCount == 0) return "Failed";
                if (DraftCount > 0 && PendingCount == 0 && PartialCount == 0 && PaidCount == 0) return "Draft";
                if (PartialCount > 0) return "Partial";
                return "Published";
            }
        }
    }
}
