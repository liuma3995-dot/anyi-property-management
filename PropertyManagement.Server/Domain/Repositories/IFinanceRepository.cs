using System;
using System.Collections.Generic;
using System.Data;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>
    /// 财务仓储（D4-1~D4-6）：收费项目/计费周期/账单/收款/收据/退款/预存/支出/报表。
    /// 事务边界由服务层控制；返回契约 DTO（展示用字段随 CHG-M4-07 补充）。
    /// </summary>
    public interface IFinanceRepository
    {
        // ---------- 收费项目（UC-FIN-001） ----------
        List<ChargeItemDto> ListChargeItems(IDbConnection connection, string keyword, string category);
        ChargeItemDto GetChargeItem(IDbConnection connection, int id);
        ChargeItemDto GetChargeItemByName(IDbConnection connection, string name);
        int InsertChargeItem(IDbConnection connection, IDbTransaction transaction, ChargeItemDto item);
        void UpdateChargeItem(IDbConnection connection, IDbTransaction transaction, ChargeItemDto item);
        void SoftDeleteChargeItem(IDbConnection connection, IDbTransaction transaction, int id);

        // ---------- 计费周期（UC-FIN-001） ----------
        List<BillingCycleDto> ListCycles(IDbConnection connection);
        BillingCycleDto GetCycle(IDbConnection connection, int id);
        int InsertCycle(IDbConnection connection, IDbTransaction transaction, BillingCycleDto cycle);
        void UpdateCycle(IDbConnection connection, IDbTransaction transaction, BillingCycleDto cycle);
        void DeleteCycle(IDbConnection connection, IDbTransaction transaction, int id);

        // ---------- 账单生成/发布（UC-FIN-002，FL-FIN-01） ----------
        IEnumerable<BillObjectCandidate> ListPropertyCandidates(IDbConnection connection);
        IEnumerable<BillObjectCandidate> ListParkingCandidates(IDbConnection connection);
        /// <summary>
        /// BR-INF-02（M7 BUG-002 修复）：缴费对象是否存在有效「房产-业主」关系。
        /// 房产取 t_owner_property_rel（del_flag=0 且 rel_status≠2 已解除）；车位取 t_parking_space.owner_id。
        /// 口径与收款侧 <see cref="GetOwnerIdByBill"/> 一致。
        /// </summary>
        bool HasValidOwnerRelation(IDbConnection connection, int? propertyId, int? parkingId);
        BillDto FindDuplicateBill(IDbConnection connection, IDbTransaction transaction, int chargeItemId, int? propertyId, int? parkingId, int cycleId);
        int InsertBill(IDbConnection connection, IDbTransaction transaction, BillDto bill); // 按 bill.DelFlag 落库（失败占位=1）
        void UpdateBillPaidAmount(IDbConnection connection, IDbTransaction transaction, BillDto bill);
        void MarkOverdue(IDbConnection connection, IDbTransaction transaction, DateTime now);
        void InsertBillStatusLog(IDbConnection connection, IDbTransaction transaction, BillStatusLogDto log);
        BillDto GetBill(IDbConnection connection, int id);
        List<BillListItemDto> ListDraftBillsByBatch(IDbConnection connection, int batchId);
        List<BillListItemDto> ListPublishedBillsByBatch(IDbConnection connection, int batchId);
        void PublishBatchBills(IDbConnection connection, IDbTransaction transaction, int batchId);
        int InsertBillGenerateLog(IDbConnection connection, IDbTransaction transaction, BillGenerateLogDto log);
        BillGenerateLogDto GetBillGenerateLog(IDbConnection connection, int id);
        void UpdateGenerateLogResult(IDbConnection connection, IDbTransaction transaction, int id, int success, int fail, string failDetail);
        PageResult<BillListItemDto> QueryBills(IDbConnection connection, BillQueryRequest query);

        /// <summary>账单生成批次列表（CHG-M4-10：PG-FIN-02 批次工作台）。</summary>
        List<BillBatchDto> QueryGenerateLogs(IDbConnection connection);

        /// <summary>软删账单批次及其中账单（CHG-M4-16：批次/账单 del_flag=1，状态日志留痕）。</summary>
        void SoftDeleteBillBatch(IDbConnection connection, IDbTransaction transaction, int batchId);
        List<BillListItemDto> ListPendingBillsByOwner(IDbConnection connection, int ownerId);
        PaymentStatisticsDto GetPaymentStatistics(IDbConnection connection);

        // ---------- 欠费台账（UC-FIN-007） ----------
        PageResult<ArrearDto> QueryArrears(IDbConnection connection, BillQueryRequest query);
        void InsertArrearRemind(IDbConnection connection, IDbTransaction transaction, int billId, string channel, string note, int? userId);
        void SoftDeleteBill(IDbConnection connection, IDbTransaction transaction, int id);

        // ---------- 收款/收据（UC-FIN-003/011，P-06） ----------
        int InsertPayment(IDbConnection connection, IDbTransaction transaction, PaymentDto payment);
        PaymentDto GetPayment(IDbConnection connection, int id);
        List<PaymentDto> ListPayments(IDbConnection connection, PageRequest query, out int total);
        int InsertReceipt(IDbConnection connection, IDbTransaction transaction, ReceiptDto receipt);
        ReceiptDto GetReceiptByPayment(IDbConnection connection, int paymentId);
        ReceiptDto GetReceipt(IDbConnection connection, int receiptId);
        void MarkReceiptPrinted(IDbConnection connection, IDbTransaction transaction, ReceiptDto receipt);
        void InsertPrintLog(IDbConnection connection, IDbTransaction transaction, string bizType, int bizId);
        PreDepositDto GetPreDeposit(IDbConnection connection, int ownerId);
        void IncreasePreDeposit(IDbConnection connection, IDbTransaction transaction, int ownerId, decimal delta);
        void DecreasePreDeposit(IDbConnection connection, IDbTransaction transaction, int ownerId, decimal delta);
        decimal GetOwnerPreDeposit(IDbConnection connection, int ownerId);
        int GetOwnerIdByBill(IDbConnection connection, int billId);

        // ---------- 退款/减免/调整（UC-FIN-004） ----------
        int InsertRefund(IDbConnection connection, IDbTransaction transaction, RefundAdjustmentDto refund);
        List<RefundAdjustmentDto> ListRefunds(IDbConnection connection, PageRequest query, out int total);
        decimal GetBillPaidAmount(IDbConnection connection, int billId);

        // ---------- 支出（UC-FIN-005/006） ----------
        List<ExpenseCategoryDto> ListExpenseCategories(IDbConnection connection);
        ExpenseCategoryDto GetExpenseCategory(IDbConnection connection, int id);
        int InsertExpenseCategory(IDbConnection connection, IDbTransaction transaction, ExpenseCategoryDto category);
        void UpdateExpenseCategory(IDbConnection connection, IDbTransaction transaction, ExpenseCategoryDto category);
        void SoftDeleteExpenseCategory(IDbConnection connection, IDbTransaction transaction, int id);
        bool ExpenseCategoryReferenced(IDbConnection connection, int categoryId);
        int InsertExpense(IDbConnection connection, IDbTransaction transaction, ExpenseDto expense);
        void UpdateExpense(IDbConnection connection, IDbTransaction transaction, ExpenseDto expense);
        void SoftDeleteExpense(IDbConnection connection, IDbTransaction transaction, int id);
        ExpenseDto GetExpense(IDbConnection connection, int id);
        List<ExpenseDto> ListExpenses(IDbConnection connection, PageRequest query, out int total);
        void InsertExpenseObjectRels(IDbConnection connection, IDbTransaction transaction, int expenseId, IEnumerable<ExpenseObjectRelDto> rels);

        // ---------- 报表/流水（UC-FIN-009/010/012） ----------
        PageResult<LedgerEntryDto> QueryLedger(IDbConnection connection, LedgerQueryRequest query);
        FinancialReportDto BuildFinancialReport(IDbConnection connection, FinancialReportWindow window);
        int InsertReportLog(IDbConnection connection, IDbTransaction transaction, ReportLogDto log);
        ReportLogDto GetReportLog(IDbConnection connection, int id);
    }

    /// <summary>账单生成候选对象（房产/车位）。</summary>

    /// <summary>财务报表计算窗口（T4F-8-1：本期/上期/本季累计/收费项目/含退费冲销）。</summary>
    public class FinancialReportWindow
    {
        public DateTime From { get; set; }        // 本期开始
        public DateTime To { get; set; }          // 本期结束
        public DateTime PrevFrom { get; set; }    // 上期开始（环比）
        public DateTime PrevTo { get; set; }      // 上期结束
        public DateTime QuarterFrom { get; set; } // 本季开始（本季累计）
        public DateTime QuarterTo { get; set; }   // 本季结束
        public int? ChargeItemId { get; set; }    // 收费项目筛选（null=全部）
        public bool IncludeRefund { get; set; }   // 含退费冲销
    }
    public class BillObjectCandidate
    {
        public int Id { get; set; }
        public string No { get; set; }
        public BillObjectKind Kind { get; set; }
        public decimal? Area { get; set; }    // T4F-1-5：房产建筑面积（按建筑面积计费用；车位为 null）
    }

    public enum BillObjectKind
    {
        Property = 0,
        Parking = 1
    }
}
