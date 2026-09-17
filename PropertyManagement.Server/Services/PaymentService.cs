using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using PropertyManagement.Contract.Common;
using Dapper;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 收款/收据/退款服务（D4-2 + D4-3，UC-FIN-003/004/011，P-06，BR-FIN-02/06/08/10）：
    /// 收款登记（部分缴/超额转预存/预存自动抵扣）、收据打印与补打留痕、
    /// 退款/减免/调整（原因必填、不超实缴、大额需确认）、预存款查询与退还。
    /// </summary>
    public class PaymentService
    {
        private const string RefundApproveThresholdKey = "refund.approve.threshold";

        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IFinanceRepository _finance;
        private readonly AuditService _audit;

        public PaymentService()
            : this(new SqliteConnectionFactory(), new SqlFinanceRepository(), new AuditService())
        {
        }

        public PaymentService(IDbConnectionFactory connectionFactory, IFinanceRepository finance, AuditService audit)
        {
            _connectionFactory = connectionFactory;
            _finance = finance;
            _audit = audit;
        }

        // ---------- 收款登记（UC-FIN-003，P-06 简单版） ----------
        public PaymentDto CreatePayment(PaymentCreateRequest request,
            string operatorName = null, string ip = null)
        {
            if (request == null || request.BillId <= 0)
            {
                throw ApiException.BadRequest("账单不能为空");
            }
            if (request.Amount <= 0)
            {
                throw ApiException.ValidationFailed("收款金额必须大于 0");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                // CHG-v1.1.0-15：单张收款同样生成收款流水号（与收支明细流水「关联单据」同一编号），收据号已下线
                string batchNo = BuildBatchNo(connection);
                SettlementResult settled = SettleBill(connection, transaction, new PaymentBatchItemRequest
                {
                    BillId = request.BillId,
                    Amount = request.Amount
                }, request.PayMethod, request.PrintReceipt, request.Remark, batchNo);
                transaction.Commit();

                _audit.Write("PAYMENT_CREATE", "bill", settled.Payment.BillId.ToString(),
                    string.Format("收款登记：账单 {0}，实收 {1:0.00}，其中抵扣预存 {2:0.00}，转预存 {3:0.00}，收款流水号 {4}{5}",
                        settled.Payment.BillId, settled.Payment.Amount, settled.UsedFromPreDeposit, settled.Payment.ToPreDeposit,
                        batchNo,
                        string.IsNullOrEmpty(settled.Payment.Remark) ? string.Empty : "，备注：" + settled.Payment.Remark),
                    userName: operatorName, ip: ip, result: "成功");

                return settled.Payment;
            }
        }

        /// <summary>
        /// 统一收款（CHG-v1.1.0-12）：对同一缴费对象下的多个账单一次性收款。
        /// 实现口径：按账单逐条落 t_payment（各自保留账单号与收据号，退款/减免仍可按账单号跨模块引用），
        /// 同一批共享 batch_no 流水号；任何一条明细校验失败，整批回滚。
        /// </summary>
        public PaymentBatchResultDto CreateBatchPayment(PaymentBatchCreateRequest request,
            string operatorName = null, string ip = null)
        {
            if (request == null || request.Items == null || request.Items.Count == 0)
            {
                throw ApiException.BadRequest("请至少选择一张账单");
            }

            var items = request.Items.Where(x => x != null && x.BillId > 0).ToList();
            if (items.Count == 0)
            {
                throw ApiException.BadRequest("请至少选择一张账单");
            }
            if (items.Count != items.Select(x => x.BillId).Distinct().Count())
            {
                throw ApiException.ValidationFailed("同一账单只能收款一次，请重新选择");
            }
            if (items.Any(x => x.Amount <= 0))
            {
                throw ApiException.ValidationFailed("每张账单的收款金额必须大于 0");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                string batchNo = BuildBatchNo(connection);
                var payments = new List<PaymentDto>();
                decimal total = 0m;
                foreach (PaymentBatchItemRequest item in items)
                {
                    SettlementResult settled = SettleBill(connection, transaction, item,
                        request.PayMethod, request.PrintReceipt, request.Remark, batchNo);
                    payments.Add(settled.Payment);
                    total += settled.Payment.Amount;
                }
                transaction.Commit();

                _audit.Write("PAYMENT_BATCH_CREATE", "payment_batch", batchNo,
                    string.Format("统一收款：流水号 {0}，账单 {1} 张，合计 {2:0.00}，方式 {3}{4}",
                        batchNo, payments.Count, total, request.PayMethod,
                        string.IsNullOrWhiteSpace(request.Remark) ? string.Empty : "，备注：" + request.Remark.Trim()),
                    userName: operatorName, ip: ip, result: "成功");

                return new PaymentBatchResultDto
                {
                    BatchNo = batchNo,
                    Payments = payments,
                    TotalAmount = total,
                    Count = payments.Count
                };
            }
        }

        /// <summary>统一收款流水号：PAY-yyyyMMdd-####（当日序号，事务内查重）。</summary>
        private static string BuildBatchNo(IDbConnection connection)
        {
            string prefix = "PAY-" + DateTime.Now.ToString("yyyyMMdd") + "-";
            int today = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM (SELECT DISTINCT batch_no FROM t_payment WHERE batch_no LIKE @prefix)",
                new { prefix = prefix + "%" });
            return prefix + (today + 1).ToString("D4");
        }

        /// <summary>
        /// 单张账单结算核心（单笔收款与统一收款共用）：
        /// 预存抵扣 → 超额转预存 → 更新账单状态 → 落收款记录 → 出收据（BR-FIN-08 编号唯一）。
        /// </summary>
        private SettlementResult SettleBill(IDbConnection connection, IDbTransaction transaction,
            PaymentBatchItemRequest item, PayMethod payMethod, bool printReceipt, string remark, string batchNo)
        {
            BillDto bill = _finance.GetBill(connection, item.BillId);
            if (bill == null)
            {
                throw ApiException.NotFound("账单不存在或已删除");
            }
            if (bill.Status == BillStatus.Paid || bill.Status == BillStatus.Reversed)
            {
                throw ApiException.ValidationFailed("账单 " + bill.Id + " 已缴清或已冲正，不能重复收款");
            }
            if (bill.Status == BillStatus.Draft)
            {
                throw ApiException.ValidationFailed("账单 " + bill.Id + " 尚未发布，不能收款");
            }

            int ownerId = _finance.GetOwnerIdByBill(connection, bill.Id);
            // CHG-v1.1.0-18：自定义缴费对象账单（payer_name 手工填写，无业主档案）允许收款，
            // 但不参与预存款抵扣/超额转预存（预存主体是业主）。
            bool isCustomPayer = ownerId <= 0 && !string.IsNullOrWhiteSpace(bill.PayerName);
            if (ownerId <= 0 && !isCustomPayer)
            {
                throw ApiException.BadRequest("账单 " + bill.Id + " 未关联业主，无法登记收款");
            }

            decimal remaining = bill.Amount - bill.PaidAmount;
            if (remaining <= 0)
            {
                throw ApiException.ValidationFailed("账单 " + bill.Id + " 已无应缴金额");
            }

            // P-06 自动抵扣：业主预存款余额优先抵扣账单（自定义缴费对象无预存主体）
            decimal preDeposit = ownerId > 0 ? _finance.GetOwnerPreDeposit(connection, ownerId) : 0m;
            decimal usedFromPreDeposit = 0m;
            if (preDeposit > 0m && remaining > 0m)
            {
                usedFromPreDeposit = Math.Min(preDeposit, remaining);
                _finance.DecreasePreDeposit(connection, transaction, ownerId, usedFromPreDeposit);
            }

            // 本次实收：先抵剩余，超出部分转预存款（P-06）
            decimal cash = item.Amount;
            decimal toBill = Math.Min(cash, remaining - usedFromPreDeposit);
            decimal excess = cash - toBill;
            if (excess > 0m)
            {
                if (isCustomPayer)
                {
                    throw ApiException.ValidationFailed(
                        "自定义缴费对象不支持超额转预存，请将收款金额调整为不超过应缴金额");
                }
                _finance.IncreasePreDeposit(connection, transaction, ownerId, excess);
            }

            decimal totalCovered = usedFromPreDeposit + toBill;
            bill.PaidAmount += totalCovered;
            BillStatus oldStatus = bill.Status;
            bill.Status = bill.PaidAmount >= bill.Amount
                ? BillStatus.Paid
                : (bill.PaidAmount > 0m ? BillStatus.Partial : bill.Status);
            _finance.UpdateBillPaidAmount(connection, transaction, bill);

            if (bill.Status != oldStatus)
            {
                _finance.InsertBillStatusLog(connection, transaction, new BillStatusLogDto
                {
                    BillId = bill.Id,
                    OldStatus = oldStatus,
                    NewStatus = bill.Status,
                    Reason = string.IsNullOrEmpty(batchNo) ? "收款登记" : "统一收款（" + batchNo + "）"
                });
            }

            var payment = new PaymentDto
            {
                BillId = bill.Id,
                Amount = cash,
                PayMethod = payMethod,
                PaidAt = DateTime.Now,
                ToPreDeposit = excess,
                BatchNo = batchNo,
                Remark = string.IsNullOrWhiteSpace(remark) ? string.Empty : remark.Trim()
            };
            payment.Id = _finance.InsertPayment(connection, transaction, payment);

            // CHG-v1.1.0-15：收据号前后端下线 —— 不再生成 t_receipt 记录，
            // 收款凭据统一以「收款流水号」（t_payment.batch_no）标识，收支明细流水「关联单据」同源。
            // 存量 t_receipt 数据保留（历史流水的关联单据仍可追溯），BR-FIN-08 对存量数据继续有效。

            return new SettlementResult
            {
                Payment = payment,
                UsedFromPreDeposit = usedFromPreDeposit
            };
        }

        private class SettlementResult
        {
            public PaymentDto Payment { get; set; }
            public decimal UsedFromPreDeposit { get; set; }
        }
        // ---------- 收款历史/收据 ----------
        public PageResult<PaymentDto> QueryPayments(PageRequest query)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                int total;
                List<PaymentDto> items = _finance.ListPayments(connection, query ?? new PageRequest(), out total);
                return new PageResult<PaymentDto>
                {
                    PageIndex = query == null ? 1 : query.PageIndex,
                    PageSize = query == null ? 20 : query.PageSize,
                    Total = total,
                    Items = items
                };
            }
        }

        public PaymentDto GetPayment(int id)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                PaymentDto payment = _finance.GetPayment(connection, id);
                if (payment == null)
                {
                    throw ApiException.NotFound("缴费记录不存在");
                }
                return payment;
            }
        }

        // CHG-v1.1.0-15：收据号前后端下线 —— 原「收据查询 / 收据打印·补打」接口一并移除，
        // 收款凭据改为导出「收据打印模板」（POST /reports/receipt-template），
        // 收款标识统一使用「收款流水号」（t_payment.batch_no，与收支明细流水「关联单据」同源）。

        // ---------- 预存款（P-06 简单版） ----------
        public PreDepositDto GetPreDeposit(int ownerId)
        {
            if (ownerId <= 0)
            {
                throw ApiException.BadRequest("业主不能为空");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _finance.GetPreDeposit(connection, ownerId) ?? new PreDepositDto
                {
                    OwnerId = ownerId,
                    Balance = 0m,
                    UpdatedAt = DateTime.Now
                };
            }
        }

        public PreDepositDto RefundPreDeposit(PreDepositRefundRequest request,
            string operatorName = null, string ip = null)
        {
            if (request == null || request.OwnerId <= 0)
            {
                throw ApiException.BadRequest("业主不能为空");
            }
            if (request.Amount <= 0)
            {
                throw ApiException.ValidationFailed("退还金额必须大于 0");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                decimal balance = _finance.GetOwnerPreDeposit(connection, request.OwnerId);
                if (balance < request.Amount)
                {
                    throw ApiException.ValidationFailed("预存款余额不足，当前余额 " + balance.ToString("0.00") + " 元");
                }

                _finance.DecreasePreDeposit(connection, transaction, request.OwnerId, request.Amount);
                transaction.Commit();

                _audit.Write("PRE_DEPOSIT_REFUND", "owner", request.OwnerId.ToString(),
                    "预存款退还：" + request.Amount.ToString("0.00") + " 元，退还后余额 " +
                    (balance - request.Amount).ToString("0.00") + " 元",
                    userName: operatorName, ip: ip, result: "成功");
                return GetPreDeposit(request.OwnerId);
            }
        }

        // ---------- 退款/减免/调整（UC-FIN-004，BR-FIN-06/10） ----------
        public RefundAdjustmentDto CreateRefund(RefundAdjustmentRequest request,
            string operatorName = null, string ip = null)
        {
            if (request == null || request.BillId <= 0)
            {
                throw ApiException.BadRequest("账单不能为空");
            }
            ValidateRefundRequest(request);

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                RefundAdjustmentDto refund = ApplyRefund(connection, transaction, request.BillId, request);
                transaction.Commit();

                _audit.Write("REFUND_CREATE", "bill", refund.BillId.ToString(),
                    string.Format("{0} {1:0.00} 元，编号 {2}，原因：{3}",
                        request.RefundType, request.Amount, refund.RefNo, refund.Reason),
                    userName: operatorName, ip: ip, result: "成功");

                return refund;
            }
        }

        /// <summary>
        /// 批量登记退款/减免/调整（CHG-v1.1.0-13）：对所选多张账单**逐张**登记，
        /// 金额口径为「每张金额」（合计 = 每张金额 × 张数）；任一张校验失败则整批回滚。
        /// 每张账单各自生成申请编号与账单号引用，保证退款/减免仍可按账单号跨模块追溯。
        /// </summary>
        public RefundBatchResultDto CreateRefundBatch(RefundAdjustmentRequest request,
            string operatorName = null, string ip = null)
        {
            List<int> billIds = request == null || request.BillIds == null
                ? new List<int>()
                : request.BillIds.Where(x => x > 0).Distinct().ToList();
            if (billIds.Count == 0)
            {
                throw ApiException.BadRequest("请至少选择一张账单");
            }
            ValidateRefundRequest(request);

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                var items = new List<RefundAdjustmentDto>();
                foreach (int billId in billIds)
                {
                    items.Add(ApplyRefund(connection, transaction, billId, request));
                }
                transaction.Commit();

                decimal total = request.Amount * items.Count;
                _audit.Write("REFUND_BATCH_CREATE", "refund_batch", items[0].RefNo,
                    string.Format("批量{0}：账单 {1} 张，每张 {2:0.00} 元，合计 {3:0.00} 元，原因：{4}",
                        request.RefundType, items.Count, request.Amount, total, request.Reason.Trim()),
                    userName: operatorName, ip: ip, result: "成功");

                return new RefundBatchResultDto
                {
                    Items = items,
                    Count = items.Count,
                    TotalAmount = total
                };
            }
        }

        private static void ValidateRefundRequest(RefundAdjustmentRequest request)
        {
            if (request.Amount <= 0)
            {
                throw ApiException.ValidationFailed("退款/减免金额必须大于 0");
            }
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                throw ApiException.ValidationFailed("必须填写退款/减免原因（BR-FIN-06）");
            }
        }

        /// <summary>单张账单的退款/减免/调整核心（单张与批量共用）。</summary>
        private RefundAdjustmentDto ApplyRefund(IDbConnection connection, IDbTransaction transaction,
            int billId, RefundAdjustmentRequest request)
        {
            {
                BillDto bill = _finance.GetBill(connection, billId);
                if (bill == null)
                {
                    throw ApiException.NotFound("账单不存在或已删除");
                }
                if (bill.Status == BillStatus.Reversed)
                {
                    throw ApiException.ValidationFailed("账单已冲正，不能重复操作");
                }

                decimal paid = bill.PaidAmount;
                if (request.Amount > paid)
                {
                    // BR-FIN-06：退款不超实缴
                    throw ApiException.ValidationFailed(
                        "退款/减免金额不能超过实缴金额 " + paid.ToString("0.00") + " 元（BR-FIN-06）");
                }

                decimal threshold = ReadRefundThreshold(connection);
                if (request.Amount > threshold && !request.ConfirmedByManager)
                {
                    // BR-FIN-10：大额退款权限控制（当前仅系统管理员角色，体现为阈值 + 确认标记）
                    throw ApiException.Forbidden(
                        "金额超过 " + threshold.ToString("0.00") + " 元属大额退款，需负责人确认后再提交（BR-FIN-10）");
                }

                var refund = new RefundAdjustmentDto
                {
                    BillId = bill.Id,
                    RefundType = request.RefundType,
                    Amount = request.Amount,
                    Reason = request.Reason.Trim(),
                    AttachmentName = request.AttachmentName,
                    AttachmentPath = request.AttachmentPath
                };
                refund.RefNo = "RF-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + bill.Id;

                refund.Id = _finance.InsertRefund(connection, transaction, refund);
                refund.CreatedAt = DateTime.Now;

                // 账单置为已冲正并留痕（状态模型：已缴/部分缴 → 已冲正）
                BillStatus oldStatus = bill.Status;
                bill.Status = BillStatus.Reversed;
                _finance.UpdateBillPaidAmount(connection, transaction, bill);
                _finance.InsertBillStatusLog(connection, transaction, new BillStatusLogDto
                {
                    BillId = bill.Id,
                    OldStatus = oldStatus,
                    NewStatus = BillStatus.Reversed,
                    Reason = request.RefundType + "：" + refund.Reason
                });

                return refund;
            }
        }

        public PageResult<RefundAdjustmentDto> QueryRefunds(PageRequest query)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                int total;
                List<RefundAdjustmentDto> items = _finance.ListRefunds(connection, query ?? new PageRequest(), out total);
                return new PageResult<RefundAdjustmentDto>
                {
                    PageIndex = query == null ? 1 : query.PageIndex,
                    PageSize = query == null ? 20 : query.PageSize,
                    Total = total,
                    Items = items
                };
            }
        }

        private static decimal ReadRefundThreshold(IDbConnection connection)
        {
            string value = connection.ExecuteScalar<string>(
                "SELECT param_value FROM t_param WHERE param_key = @key", new { key = RefundApproveThresholdKey });
            decimal threshold;
            return decimal.TryParse(value, out threshold) && threshold > 0m ? threshold : 1000m;
        }
    }
}
