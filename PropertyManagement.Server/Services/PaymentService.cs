using System;
using System.Collections.Generic;
using System.Data;
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
                BillDto bill = _finance.GetBill(connection, request.BillId);
                if (bill == null)
                {
                    throw ApiException.NotFound("账单不存在或已删除");
                }
                if (bill.Status == BillStatus.Paid || bill.Status == BillStatus.Reversed)
                {
                    throw ApiException.ValidationFailed("账单已缴清或已冲正，不能重复收款");
                }
                if (bill.Status == BillStatus.Draft)
                {
                    throw ApiException.ValidationFailed("账单尚未发布，不能收款");
                }

                int ownerId = _finance.GetOwnerIdByBill(connection, bill.Id);
                if (ownerId <= 0)
                {
                    throw ApiException.BadRequest("账单未关联业主，无法登记收款");
                }

                decimal remaining = bill.Amount - bill.PaidAmount;
                if (remaining <= 0)
                {
                    throw ApiException.ValidationFailed("账单已无应缴金额");
                }

                // P-06 自动抵扣：业主预存款余额优先抵扣账单
                decimal preDeposit = _finance.GetOwnerPreDeposit(connection, ownerId);
                decimal usedFromPreDeposit = 0m;
                if (preDeposit > 0m && remaining > 0m)
                {
                    usedFromPreDeposit = Math.Min(preDeposit, remaining);
                    _finance.DecreasePreDeposit(connection, transaction, ownerId, usedFromPreDeposit);
                }

                // 本次实收：先抵剩余，超出部分转预存款（P-06）
                decimal cash = request.Amount;
                decimal toBill = Math.Min(cash, remaining - usedFromPreDeposit);
                decimal excess = cash - toBill;
                if (excess > 0m)
                {
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
                        Reason = "收款登记"
                    });
                }

                var payment = new PaymentDto
                {
                    BillId = bill.Id,
                    Amount = cash,
                    PayMethod = request.PayMethod,
                    PaidAt = DateTime.Now,
                    ToPreDeposit = excess,
                    Remark = string.IsNullOrWhiteSpace(request.Remark) ? string.Empty : request.Remark.Trim()
                };
                payment.Id = _finance.InsertPayment(connection, transaction, payment);

                // 收据（BR-FIN-08：编号唯一）
                var receipt = new ReceiptDto
                {
                    PaymentId = payment.Id,
                    ReceiptNo = "RC-" + DateTime.Now.ToString("yyyyMMdd") + "-" + payment.Id
                };
                receipt.Id = _finance.InsertReceipt(connection, transaction, receipt);

                if (request.PrintReceipt)
                {
                    receipt.PrintCount = 1;
                    _finance.MarkReceiptPrinted(connection, transaction, receipt);
                    _finance.InsertPrintLog(connection, transaction, "receipt", receipt.Id);
                }

                transaction.Commit();

                _audit.Write("PAYMENT_CREATE", "bill", bill.Id.ToString(),
                    string.Format("收款登记：账单 {0}，实收 {1:0.00}，其中抵扣预存 {2:0.00}，转预存 {3:0.00}，收据 {4}{5}",
                        bill.Id, cash, usedFromPreDeposit, excess, receipt.ReceiptNo,
                        string.IsNullOrEmpty(payment.Remark) ? string.Empty : "，备注：" + payment.Remark),
                    userName: operatorName, ip: ip, result: "成功");

                return payment;
            }
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

        public ReceiptDto GetReceipt(int receiptId)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                ReceiptDto receipt = _finance.GetReceipt(connection, receiptId);
                if (receipt == null)
                {
                    throw ApiException.NotFound("收据不存在");
                }
                return receipt;
            }
        }
        public ReceiptDto PrintReceipt(ReceiptPrintRequest request,
            string operatorName = null, string ip = null)
        {
            if (request == null || request.ReceiptId <= 0)
            {
                throw ApiException.BadRequest("收据不能为空");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                ReceiptDto receipt = _finance.GetReceipt(connection, request.ReceiptId);
                if (receipt == null)
                {
                    throw ApiException.NotFound("收据不存在");
                }

                // BR-FIN-08：补打保留原收据号，递增打印次数并留痕
                receipt.PrintCount += 1;
                _finance.MarkReceiptPrinted(connection, transaction, receipt);
                _finance.InsertPrintLog(connection, transaction, "receipt", receipt.Id);
                transaction.Commit();

                _audit.Write("RECEIPT_PRINT", "receipt", receipt.Id.ToString(),
                    "收据打印/补打：编号 " + receipt.ReceiptNo + "，第 " + receipt.PrintCount + " 次",
                    userName: operatorName, ip: ip, result: "成功");
                return receipt;
            }
        }

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
            if (request.Amount <= 0)
            {
                throw ApiException.ValidationFailed("退款/减免金额必须大于 0");
            }
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                throw ApiException.ValidationFailed("必须填写退款/减免原因（BR-FIN-06）");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                BillDto bill = _finance.GetBill(connection, request.BillId);
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

                transaction.Commit();

                _audit.Write("REFUND_CREATE", "bill", bill.Id.ToString(),
                    string.Format("{0} {1:0.00} 元，编号 {2}，原因：{3}",
                        request.RefundType, request.Amount, refund.RefNo, refund.Reason),
                    userName: operatorName, ip: ip, result: "成功");

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
