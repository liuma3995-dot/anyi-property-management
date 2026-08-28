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
        public decimal Amount { get; set; }
        public DateTime ExpenseDate { get; set; }
        public string Note { get; set; }
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
    }

    /// <summary>报表明细行。</summary>
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
    }
}
