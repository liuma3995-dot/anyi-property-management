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
        public int? Status { get; set; } // 停用不影响已出账单（UC-FIN-001）
    }

    public class BillingCycleRequest
    {
        public BillingCycleType CycleType { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    /// <summary>账单生成请求（UC-FIN-002：按收费项目 + 周期 + 对象范围）。</summary>
    public class BillGenerateRequest
    {
        public int ChargeItemId { get; set; }
        public int CycleId { get; set; }
        public List<int> PropertyIds { get; set; }
        public List<int> ParkingIds { get; set; }
    }

    /// <summary>账单发布请求（草稿→发布，失败清单重推）。</summary>
    public class BillPublishRequest
    {
        public int BatchId { get; set; }
    }

    /// <summary>账单查询条件（UC-FIN-007 欠费台账/账单列表，分页）。</summary>
    public class BillQueryRequest : PageRequest
    {
        public BillStatus? Status { get; set; }
        public int? PropertyId { get; set; }
        public int? ChargeItemId { get; set; }
        public DateTime? DueFrom { get; set; }
        public DateTime? DueTo { get; set; }
        public bool ArrearsOnly { get; set; }
    }

    /// <summary>收款登记请求（UC-FIN-003：全额/部分缴/超额转预存 P-06）。</summary>
    public class PaymentCreateRequest
    {
        public int BillId { get; set; }
        public decimal Amount { get; set; }
        public PayMethod PayMethod { get; set; }
        public bool PrintReceipt { get; set; }
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
        public RefundType RefundType { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; }
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
        public List<ExpenseObjectRelDto> Objects { get; set; }
    }

    /// <summary>收支流水查询（UC-FIN-010，分页）。</summary>
    public class LedgerQueryRequest : PageRequest
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string BizType { get; set; }
    }

    /// <summary>财务报表查询（UC-FIN-009：month/quarter + 期间）。</summary>
    public class FinancialReportQueryRequest
    {
        public string PeriodType { get; set; } // month / quarter
        public string Period { get; set; }     // 2026-08 或 2026-Q3
    }

    /// <summary>报表导出请求（UC-FIN-012，Excel/PDF，留痕 t_report_log）。</summary>
    public class ReportExportRequest
    {
        public FinancialReportQueryRequest Query { get; set; }
        public ExportFormat Format { get; set; }
    }
}
