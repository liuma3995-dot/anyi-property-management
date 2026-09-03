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
        public ChargeObjectType ObjectType { get; set; } // CHG-M4-09：适用对象（房产/车位），服务端按计价方式派生
        public int? Status { get; set; } // 停用不影响已出账单（UC-FIN-001）

        // T4F-1-5（CHG-M4-11）：类别/计价方式/单价单位/自定义周期
        public string Category { get; set; }
        public string MethodCode { get; set; }
        public string MethodName { get; set; }
        public string PriceUnit { get; set; }
        public string CycleName { get; set; }
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

        /// <summary>收款备注（T4R-3：可空，随收款落库并进入审计留痕）。</summary>
        public string Remark { get; set; }
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

        /// <summary>BR-FIN-10：大额退款需负责人确认标记（当前单角色下前端弹窗确认后置 true）。</summary>
        public bool ConfirmedByManager { get; set; }

        /// <summary>附件（T4F-4-1：≤5MB 必传，本期存文件名与路径）。</summary>
        public string AttachmentName { get; set; }

        public string AttachmentPath { get; set; }
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
}
