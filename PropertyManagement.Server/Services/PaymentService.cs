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
    /// 收款登记（全额/部分缴，收款金额不得超过账单未收金额 —— CHG-v1.1.2-51 下线「多缴转预存」）、
    /// 收据模板导出与留痕、存量预存款查询与退还（预存款不再由收款产生）、
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

        // ---------- 收款登记（UC-FIN-003；CHG-v1.1.2-51：P-06「多缴转预存」已下线） ----------
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

            // 本次实收（CHG-v1.1.2-51：下线「多缴自动转入预存账户」）
            // 口径：收款金额不得超过该账单未收金额；「多缴 → 预存账户」这条业务链路整条移除，
            // 超收一律拒绝并给出可读提示（原先「超出部分静默转入预存」会让用户看不到这笔钱去哪了）。
            decimal cash = item.Amount;
            if (cash <= 0m)
            {
                throw ApiException.ValidationFailed("收款金额必须大于 0");
            }
            if (cash > remaining)
            {
                throw ApiException.ValidationFailed(
                    "收款金额不能超过该账单未收金额 ¥" + remaining.ToString("0.00") +
                    "，请调整后重新收款");
            }
            decimal toBill = Math.Min(cash, remaining - usedFromPreDeposit);
            // 业主预存余额抵扣后，等额现金回存预存账户（余额不变），仅存量预存款适用
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
            _finance.UpdateBillAmountAndPaid(connection, transaction, bill);

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
            if (request == null)
            {
                throw ApiException.BadRequest("请求不能为空");
            }
            ValidateRefundRequest(request);
            // CHG-v1.1.2-04（负责人 2026-09-19 裁定 A）：账务调整允许不选关联账单，用于冲正/补收等无账单场景；
            // 退款/减免仍必须关联账单（它们要冲减某张账单的实缴金额）。
            if (request.BillId <= 0 && request.RefundType != RefundType.Adjustment)
            {
                throw ApiException.ValidationFailed("退款/减免必须选择关联账单；无账单的冲正/补收请在「账务调整」页签登记");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                RefundAdjustmentDto refund = ApplyRefund(connection, transaction, request.BillId, request, operatorName);
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
                    items.Add(ApplyRefund(connection, transaction, billId, request, operatorName));
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
                throw ApiException.ValidationFailed("必须填写退款/减免原因");
            }
        }

        /// <summary>单张账单的退款/减免/调整核心（单张与批量共用）。</summary>
        private RefundAdjustmentDto ApplyRefund(IDbConnection connection, IDbTransaction transaction,
            int billId, RefundAdjustmentRequest request, string operatorName)
        {
            {
                // CHG-v1.1.2-04：无关联账单的「账务调整」（冲正/补收）—— 只登记一笔调整记录，不动任何账单状态。
                if (billId <= 0)
                {
                    return ApplyAdjustmentWithoutBill(connection, transaction, request, operatorName);
                }

                BillDto bill = _finance.GetBill(connection, billId);
                if (bill == null)
                {
                    throw ApiException.NotFound("账单不存在或已删除");
                }
                if (bill.Status == BillStatus.Draft)
                {
                    throw ApiException.ValidationFailed("账单 " + bill.Id + " 尚未发布，不能登记退款/减免/调整");
                }
                // CHG-v1.1.2-07（负责人 2026-09-19 裁定 A）：减免/调整允许重复登记（累计不超实缴），
                // 退款保留「已冲正不可重复」的拦截。
                // CHG-v1.1.2-40：减免改为「调减应收」，同样不能在已冲正账单上登记（账务调整用于冲正/补收，保留）。
                if (bill.Status == BillStatus.Reversed && request.RefundType != RefundType.Adjustment)
                {
                    throw ApiException.ValidationFailed("账单已冲正，不能重复退款/减免；无账单的冲正/补收请使用「账务调整」页签");
                }

                decimal paid = bill.PaidAmount;
                decimal unreceived = bill.Amount - paid;   // 未收余额（应收 − 实缴）
                int direction = ResolveAdjustDirection(request.RefundType, request.Method);
                bool isDiscount = request.RefundType == RefundType.Discount;

                if (isDiscount)
                {
                    // CHG-v1.1.2-40（负责人 2026-09-20 反馈）：减免＝**直接调减账单应收**，与退款（冲减实缴）
                    // 是两条独立链路。上游封顶取「未收余额」，保证减免后 应收 ≥ 实缴（不产生虚增的已缴/多收）。
                    if (request.Amount > unreceived)
                    {
                        throw ApiException.ValidationFailed(
                            "减免金额不能超过账单未收余额 " + Math.Max(0m, unreceived).ToString("0.00") +
                            " 元（应收 " + bill.Amount.ToString("0.00") + " − 已缴 " + paid.ToString("0.00") + "）");
                    }
                }
                else if (direction != 1 && request.Amount > paid)
                {
                    // CHG-v1.1.2-39：改为「实缴冲减」口径 —— paid_amount 记净实缴（历史冲减已扣除），
                    // 因此本次金额直接与「当前净实缴」比较；「调增补收」方向（方式=补收/调增）表示补收，不受此限。
                    // BR-FIN-06：退款/调减冲正累计不超实缴（CHG-v1.1.2-07 由「单次不超」改为「累计不超」）
                    decimal already = _finance.SumPaidCutsByBill(connection, transaction, bill.Id);
                    throw ApiException.ValidationFailed(
                        already > 0m
                            ? "当前可冲减实缴仅剩 " + paid.ToString("0.00") + " 元（历史已冲减 " + already.ToString("0.00") + " 元），本次 " + request.Amount.ToString("0.00") + " 元已超出"
                            : "退款金额不能超过实缴金额 " + paid.ToString("0.00") + " 元");
                }

                decimal threshold = ReadRefundThreshold(connection);
                if (request.Amount > threshold && !request.ConfirmedByManager)
                {
                    // BR-FIN-10：大额退款权限控制（当前仅系统管理员角色，体现为阈值 + 确认标记）
                    throw ApiException.Forbidden(
                    "金额超过 " + threshold.ToString("0.00") + " 元属大额退款，需负责人确认后再提交");
                }

                var refund = new RefundAdjustmentDto
                {
                    BillId = bill.Id,
                    RefundType = request.RefundType,
                    Amount = request.Amount,
                    Reason = request.Reason.Trim(),
                    Method = string.IsNullOrWhiteSpace(request.Method) ? null : request.Method.Trim(),
                    AdjustDir = direction,
                    AttachmentName = request.AttachmentName,
                    AttachmentPath = request.AttachmentPath,
                    // CHG-v1.1.2-41：经办人随单据落库（导出 PDF / 审计追溯）
                    OperatorName = operatorName
                };
                // CHG-v1.1.2-07：减免/调整可重复登记 → 申请编号带毫秒，避免同一秒内多条记录编号相同
                refund.RefNo = "RF-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "-" + bill.Id;

                refund.Id = _finance.InsertRefund(connection, transaction, refund);
                refund.CreatedAt = DateTime.Now;

                // CHG-v1.1.2-39（负责人 2026-09-20 反馈）：不得把账单一律置为「已冲正」——原实现把账单改成 4，
                // 而收款登记只列 未缴/部分缴/逾期，导致「仍有未收金额」的账单连同剩余欠款一起消失，钱收不回来。
                // CHG-v1.1.2-40（负责人 2026-09-20 反馈）：两条落账链路彻底拆开 ——
                //   退款 / 调整冲减 → 冲减 paid_amount（净实缴，补收方向为调增），应收不动；
                //   减免          → 调减 amount（应收），实缴不动，不产生任何资金流出。
                // 状态一律按「应收 / 净实缴」重算 → 有欠款就继续出现在收款登记与欠费台账。
                BillStatus oldStatus = bill.Status;
                decimal oldAmount = bill.Amount;
                string detail;
                if (isDiscount)
                {
                    bill.Amount = oldAmount - request.Amount;
                    detail = "应收 " + oldAmount.ToString("0.00") + " → " + bill.Amount.ToString("0.00") +
                             "，实缴 " + paid.ToString("0.00") + " 不变";
                }
                else
                {
                    decimal netPaid = direction == 1 ? paid + request.Amount : paid - request.Amount;
                    if (netPaid < 0m) { netPaid = 0m; }
                    bill.PaidAmount = netPaid;
                    detail = "实缴 " + paid.ToString("0.00") + " → " + netPaid.ToString("0.00");
                }
                bill.Status = ResolveBillStatusByMoney(bill.Amount, bill.PaidAmount, bill.DueAt);
                _finance.UpdateBillAmountAndPaid(connection, transaction, bill);
                _finance.InsertBillStatusLog(connection, transaction, new BillStatusLogDto
                {
                    BillId = bill.Id,
                    OldStatus = oldStatus,
                    NewStatus = bill.Status,
                    Reason = request.RefundType + "：" + refund.Reason + "（" + detail + "）"
                });

                return refund;
            }
        }

        /// <summary>
        /// CHG-v1.1.2-39/-40：按「应收 / 净实缴」重算账单状态 ——
        /// 净实缴 ≥ 应收 → 已缴（应收被减免至 0 同样视为已缴清）；否则按到期日判逾期
        /// （口径与 MarkOverdue 一致：**到期日次日起**才算逾期），再落 部分缴 / 待缴。
        /// </summary>
        private static BillStatus ResolveBillStatusByMoney(decimal amount, decimal paidAmount, DateTime dueAt)
        {
            if (paidAmount >= amount) { return BillStatus.Paid; }
            if ((DateTime.Today - dueAt.Date).Days > 1) { return BillStatus.Overdue; }
            return paidAmount > 0m ? BillStatus.Partial : BillStatus.Pending;
        }

        /// <summary>
        /// CHG-v1.1.2-04：无关联账单的账务调整（冲正/补收）。
        /// 口径：t_payment_refund.bill_id 记 0（库未开启外键约束），只落一条调整记录 + 审计留痕，
        /// 不改动任何账单状态，也不影响收款登记/欠费台账/财务报表的历史口径。
        /// </summary>
        private RefundAdjustmentDto ApplyAdjustmentWithoutBill(IDbConnection connection, IDbTransaction transaction,
            RefundAdjustmentRequest request, string operatorName)
        {
            decimal threshold = ReadRefundThreshold(connection);
            if (request.Amount > threshold && !request.ConfirmedByManager)
            {
                throw ApiException.Forbidden(
                    "金额超过 " + threshold.ToString("0.00") + " 元属大额调整，需负责人确认后再提交");
            }

            var refund = new RefundAdjustmentDto
            {
                BillId = 0,
                RefundType = request.RefundType,
                Amount = request.Amount,
                Reason = request.Reason.Trim(),
                Method = string.IsNullOrWhiteSpace(request.Method) ? null : request.Method.Trim(),
                AdjustDir = ResolveAdjustDirection(request.RefundType, request.Method),
                AttachmentName = request.AttachmentName,
                AttachmentPath = request.AttachmentPath,
                // CHG-v1.1.2-41：经办人随单据落库（导出 PDF / 审计追溯）
                OperatorName = operatorName
            };
            refund.RefNo = "RF-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "-0";
            refund.Id = _finance.InsertRefund(connection, transaction, refund);
            refund.CreatedAt = DateTime.Now;
            return refund;
        }

        /// <summary>
        /// CHG-v1.1.2-12：账务调整的 +/− 由「方式」决定 ——
        /// 「调增补收」= 1（计入收入方向）、「调减冲正」= 2（冲减方向）、其它/未指定 = 0。
        /// 非调整类型（退款/减免）恒为 0。
        /// </summary>
        private static int ResolveAdjustDirection(RefundType type, string method)
        {
            if (type != RefundType.Adjustment) { return 0; }
            string text = method == null ? string.Empty : method.Trim();
            if (text.IndexOf("补收", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("调增", StringComparison.Ordinal) >= 0) { return 1; }
            if (text.IndexOf("冲正", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("调减", StringComparison.Ordinal) >= 0) { return 2; }
            return 0;
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
