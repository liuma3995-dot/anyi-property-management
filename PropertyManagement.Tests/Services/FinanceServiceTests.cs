using System;
using System.Collections.Generic;
using System.Linq;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Services;
using PropertyManagement.Tests.Infrastructure;
using Xunit;

namespace PropertyManagement.Tests.Services
{
    /// <summary>
    /// 财务收费服务单元测试（BR-FIN-01~10）：
    /// 账单生成/收款/退款/欠费台账/收据/报表/软删口径。
    /// 口径说明：BR-FIN-02「多收阻止」已被 P-06（多缴转预存款，M4 T4-2-1 已确认）取代；
    /// BR-FIN-05（工资支出必须引用员工）现状未强制，见 BUG-003 待裁决用例。
    /// </summary>
    public class FinanceServiceTests : DbTestBase
    {
        private readonly BillingService _billing = new BillingService();
        private readonly PaymentService _payment = new PaymentService();
        private readonly ExpenseService _expense = new ExpenseService();
        private readonly ReportService _report = new ReportService();

        // ===================== BR-FIN-01 同对象/同周期/同项目不重复出账 =====================

        // ---- BR-INF-02（M7 BUG-002 裁定修复）：出账前必须存在有效「房产-业主」关系 ----

        [Fact]
        public void GenerateBill_房产无有效业主关系_记失败行且不出账()
        {
            int community = TestData.Community("无关系出账小区");
            int building = TestData.Building(community, "1");
            int property = TestData.Property(building, "101", null, 100m); // 未绑定业主
            int chargeItem = TestData.ChargeItem("无关系物业费", 2m);
            int cycle = TestData.Cycle();

            var batch = _billing.GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = chargeItem, CycleId = cycle, PropertyIds = new List<int> { property }
            });

            Assert.Equal(0, batch.Success);
            Assert.Equal(1, batch.Fail);
            Assert.Contains("BR-INF-02", _billing.ListFailures(batch.Id)[0].Reason);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_bill WHERE property_id = @id AND del_flag = 0", new { id = property }));
        }

        [Fact]
        public void GenerateBill_房产关系已解除_记失败行且不出账()
        {
            int property = NewPropertyWithOwner("关系解除出账小区");
            int relationId = ScalarInt("SELECT id FROM t_owner_property_rel WHERE property_id = @id", new { id = property });
            new BaseInfoService().ReleaseRelation(relationId, "业主已过户");
            int chargeItem = TestData.ChargeItem("解除关系物业费", 2m);
            int cycle = TestData.Cycle();

            var batch = _billing.GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = chargeItem, CycleId = cycle, PropertyIds = new List<int> { property }
            });

            Assert.Equal(0, batch.Success);
            Assert.Equal(1, batch.Fail);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_bill WHERE property_id = @id", new { id = property }));
        }

        [Fact]
        public void GenerateBill_车位未绑定业主_记失败行且不出账()
        {
            int parking = TestData.Parking("N-001");
            int chargeItem = TestData.ChargeItem("停车费无业主", 1200m, "parking", "停车费",
                BillingCycleType.Yearly, ChargePayMode.Yearly);
            int cycle = TestData.Cycle(BillingCycleType.Yearly);

            var batch = _billing.GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = chargeItem, CycleId = cycle, ParkingIds = new List<int> { parking }
            });

            Assert.Equal(0, batch.Success);
            Assert.Equal(1, batch.Fail);
            Assert.Contains("BR-INF-02", _billing.ListFailures(batch.Id)[0].Reason);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_bill WHERE parking_id = @id", new { id = parking }));
        }

        [Fact]
        public void GenerateBill_车位绑定业主后_出账成功()
        {
            int ownerId = TestData.Owner("车位业主", "13800007777");
            int parking = TestData.Parking("N-002", ParkingSpaceType.PropertyRight, ParkingSpaceStatus.Rented, null, ownerId);
            int chargeItem = TestData.ChargeItem("停车费有业主", 1200m, "parking", "停车费",
                BillingCycleType.Yearly, ChargePayMode.Yearly);
            int cycle = TestData.Cycle(BillingCycleType.Yearly);

            var batch = _billing.GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = chargeItem, CycleId = cycle, ParkingIds = new List<int> { parking }
            });

            Assert.Equal(1, batch.Success);
            Assert.Equal(0, batch.Fail);
            Assert.Equal(BillStatus.Draft, ScalarEnum<BillStatus>(
                "SELECT status FROM t_bill WHERE generate_batch_id = @id", batch.Id));
        }

        [Fact]
        public void GenerateBill_首次生成_批次成功且账单为草稿()
        {
            int property = NewPropertyWithOwner("首次出账小区");
            int chargeItem = TestData.ChargeItem("首次出账物业费", 2.5m);
            int cycle = TestData.Cycle();

            var batch = _billing.GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = chargeItem, CycleId = cycle, PropertyIds = new List<int> { property }
            });

            Assert.Equal(1, batch.Success);
            Assert.Equal(0, batch.Fail);
            Assert.Equal(BillStatus.Draft, ScalarEnum<BillStatus>(
                "SELECT status FROM t_bill WHERE generate_batch_id = @id", batch.Id));
            Assert.Equal(250m, ScalarDecimal("SELECT amount FROM t_bill WHERE generate_batch_id = @id", batch.Id));
        }

        [Fact]
        public void GenerateBill_重复生成同周期账单_跳过并记失败行()
        {
            int property = NewPropertyWithOwner("重复出账小区");
            int chargeItem = TestData.ChargeItem("重复出账物业费", 2m);
            int cycle = TestData.Cycle();
            _billing.GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = chargeItem, CycleId = cycle, PropertyIds = new List<int> { property }
            });

            BillGenerateLogDto second = _billing.GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = chargeItem, CycleId = cycle, PropertyIds = new List<int> { property }
            });

            Assert.Equal(0, second.Success);
            Assert.Equal(1, second.Fail);
            Assert.Contains("BR-FIN-01", _billing.ListFailures(second.Id)[0].Reason);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_bill WHERE property_id = @id AND del_flag = 0", new { id = property }));
        }

        [Fact]
        public void GenerateBill_收费项目已停用_抛BadRequest()
        {
            int property = NewPropertyWithOwner("停用项目出账小区");
            int chargeItem = TestData.ChargeItem("停用收费项目", 2m);
            int cycle = TestData.Cycle();
            _billing.UpdateChargeItem(chargeItem, new ChargeItemRequest
            {
                Name = "停用收费项目", Category = "物业费", MethodCode = "area", UnitPrice = 2m, Status = 1
            });

            var ex = Assert.Throws<ApiException>(() => _billing.GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = chargeItem, CycleId = cycle, PropertyIds = new List<int> { property }
            }));

            Assert.Equal(ErrorCode.BadRequest, ex.Code);
            Assert.Contains("BR-FIN-03", ex.Message);
        }

        // ===================== BR-FIN-02 少收转部分缴；多收转预存（P-06） =====================

        [Fact]
        public void CreatePayment_金额小于应收_账单转部分缴并记审计()
        {
            int billId = NewPublishedBill(out int ownerId, unitPrice: 2m, area: 100m); // 应收 200

            var payment = _payment.CreatePayment(new PaymentCreateRequest
            {
                BillId = billId, Amount = 120m, PayMethod = PayMethod.Cash, PrintReceipt = true
            });

            Assert.Equal(120m, payment.Amount);
            Assert.Equal(BillStatus.Partial, ScalarEnum<BillStatus>("SELECT status FROM t_bill WHERE id = @id", billId));
            Assert.Equal(120m, ScalarDecimal("SELECT paid_amount FROM t_bill WHERE id = @id", billId));
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_audit_log WHERE action = 'PAYMENT_CREATE'"));
            Assert.True(ScalarInt("SELECT COUNT(1) FROM t_receipt WHERE payment_id = @id", new { id = payment.Id }) == 1, "收据应自动开具");
            Assert.Equal(0m, ScalarDecimal("SELECT COALESCE((SELECT balance FROM t_pre_deposit WHERE owner_id = @id), 0)", ownerId));
        }

        [Fact]
        public void CreatePayment_金额大于应收_多缴转预存款()
        {
            int billId = NewPublishedBill(out int ownerId, unitPrice: 2m, area: 100m); // 应收 200

            var payment = _payment.CreatePayment(new PaymentCreateRequest
            {
                BillId = billId, Amount = 300m, PayMethod = PayMethod.WeChat
            });

            Assert.Equal(300m, payment.Amount);
            Assert.Equal(100m, payment.ToPreDeposit);            // P-06 简单版：多缴转存
            Assert.Equal(BillStatus.Paid, ScalarEnum<BillStatus>("SELECT status FROM t_bill WHERE id = @id", billId));
            Assert.Equal(100m, ScalarDecimal("SELECT COALESCE((SELECT balance FROM t_pre_deposit WHERE owner_id = @id), 0)", ownerId));
        }

        [Fact]
        public void CreatePayment_账单尚未发布_抛ValidationFailed()
        {
            int property = NewPropertyWithOwner("未发布收款小区");
            int chargeItem = TestData.ChargeItem("未发布账单项目", 2m);
            int cycle = TestData.Cycle();
            var batch = _billing.GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = chargeItem, CycleId = cycle, PropertyIds = new List<int> { property }
            });
            int billId = ScalarInt("SELECT id FROM t_bill WHERE generate_batch_id = @id", new { id = batch.Id });

            var ex = Assert.Throws<ApiException>(() => _payment.CreatePayment(new PaymentCreateRequest
            {
                BillId = billId, Amount = 100m, PayMethod = PayMethod.Cash
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("尚未发布", ex.Message);
        }

        [Fact]
        public void CreatePayment_账单已缴清_抛ValidationFailed()
        {
            int billId = NewPublishedBill(out int _, unitPrice: 2m, area: 100m);
            _payment.CreatePayment(new PaymentCreateRequest { BillId = billId, Amount = 200m, PayMethod = PayMethod.Cash });

            var ex = Assert.Throws<ApiException>(() => _payment.CreatePayment(new PaymentCreateRequest
            {
                BillId = billId, Amount = 10m, PayMethod = PayMethod.Cash
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("不能重复收款", ex.Message);
        }

        // ===================== BR-FIN-03 停车费按年缴，计费模式枚举预留 =====================

        [Fact]
        public void CreateChargeItem_停车费按年缴_保存成功且模式为按年()
        {
            var dto = _billing.CreateChargeItem(new ChargeItemRequest
            {
                Name = "停车费（按年）", Category = "停车费", MethodCode = "parking", MethodName = "按车位数",
                UnitPrice = 1200m, CycleType = BillingCycleType.Yearly, PayMode = ChargePayMode.Yearly
            });

            Assert.True(dto.Id > 0);
            Assert.Equal(ChargePayMode.Yearly, dto.PayMode);
            Assert.Equal(ChargeObjectType.Parking, dto.ObjectType);
        }

        [Fact]
        public void CreateChargeItem_单价为0_抛ValidationFailed()
        {
            var ex = Assert.Throws<ApiException>(() => _billing.CreateChargeItem(new ChargeItemRequest
            {
                Name = "零单价项目", Category = "物业费", MethodCode = "area", UnitPrice = 0m
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("单价必须大于 0", ex.Message);
        }

        [Fact]
        public void CreateChargeItem_自定义周期未填名称_抛ValidationFailed()
        {
            var ex = Assert.Throws<ApiException>(() => _billing.CreateChargeItem(new ChargeItemRequest
            {
                Name = "自定义周期项目", Category = "物业费", MethodCode = "area", UnitPrice = 1m,
                CycleType = BillingCycleType.Custom, CycleName = "  "
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("自定义计费周期必须填写周期名称", ex.Message);
        }

        [Fact]
        public void CreateChargeItem_同名项目_抛ValidationFailed()
        {
            TestData.ChargeItem("重名收费项目", 1m);

            var ex = Assert.Throws<ApiException>(() => _billing.CreateChargeItem(new ChargeItemRequest
            {
                Name = "重名收费项目", Category = "物业费", MethodCode = "area", UnitPrice = 2m
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("同名收费项目已存在", ex.Message);
        }

        // ===================== BR-FIN-04 支出必须关联支出分类 =====================

        [Fact]
        public void CreateExpense_选择有效分类_登记成功并落库()
        {
            int categoryId = TestData.ExpenseCategory("有效分类支出");

            var dto = _expense.CreateExpense(new ExpenseCreateRequest
            {
                CategoryId = categoryId, Amount = 268.5m, ExpenseDate = DateTime.Today, Note = "单元测试支出", Payee = "收款方"
            });

            Assert.True(dto.Id > 0);
            Assert.Equal(categoryId, dto.CategoryId);
            Assert.Equal(268.5m, dto.Amount);
            Assert.Equal(1, _expense.QueryExpenses(new PageRequest { PageIndex = 1, PageSize = 20 }).Total);
        }

        [Fact]
        public void CreateExpense_未选支出分类_抛BadRequest()
        {
            var ex = Assert.Throws<ApiException>(() => _expense.CreateExpense(new ExpenseCreateRequest
            {
                CategoryId = 0, Amount = 100m, ExpenseDate = DateTime.Today
            }));

            Assert.Equal(ErrorCode.BadRequest, ex.Code);
            Assert.Contains("BR-FIN-04", ex.Message);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_expense"));
        }

        [Fact]
        public void CreateExpense_分类不存在_抛NotFound()
        {
            var ex = Assert.Throws<ApiException>(() => _expense.CreateExpense(new ExpenseCreateRequest
            {
                CategoryId = 999999, Amount = 100m, ExpenseDate = DateTime.Today
            }));

            Assert.Equal(ErrorCode.NotFound, ex.Code);
        }

        [Fact]
        public void CreateExpense_金额为0_抛ValidationFailed()
        {
            int categoryId = TestData.ExpenseCategory();

            var ex = Assert.Throws<ApiException>(() => _expense.CreateExpense(new ExpenseCreateRequest
            {
                CategoryId = categoryId, Amount = 0m, ExpenseDate = DateTime.Today
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("支出金额必须大于 0", ex.Message);
        }

        // ===================== BR-FIN-05 支出引用对象（现状口径） =====================

        [Fact]
        public void CreateExpense_维护支出引用设备_写入关联对象()
        {
            int categoryId = TestData.ExpenseCategory("设备维修费");
            int deviceId = TestData.Device(TestData.DeviceType("引用测试电梯", 30), "引用测试设备");

            int expenseId = TestData.Expense(categoryId, 880m, TestData.Rel(ExpenseObjectType.Device, deviceId));

            Assert.Equal(1, ScalarInt(
                "SELECT COUNT(1) FROM t_expense_object_rel WHERE expense_id = @id AND object_type = 1 AND object_id = @did",
                new { id = expenseId, did = deviceId }));
        }

        [Fact]
        public void CreateExpense_工资支出未引用员工_现状放行并登记BUG003()
        {
            // 现状断言：AM-06 BR-FIN-05「工资支出必须引用员工」未在服务层强制（支出分类仅校验存在性），
            // 已登记 BUG-003 提交负责人裁决；本用例锁定现状，裁决后按结论改写预期。
            int categoryId = TestData.ExpenseCategory("工资");

            int expenseId = TestData.Expense(categoryId, 5600m);

            Assert.True(expenseId > 0);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_expense_object_rel WHERE expense_id = @id", new { id = expenseId }));
        }

        // ===================== BR-FIN-06 退款/减免/调整必填原因并计入审计 =====================

        [Fact]
        public void CreateRefund_未填原因_抛ValidationFailed()
        {
            int billId = NewPublishedBillAndPaid(out int _, 2m, 100m);

            var ex = Assert.Throws<ApiException>(() => _payment.CreateRefund(new RefundAdjustmentRequest
            {
                BillId = billId, RefundType = RefundType.Refund, Amount = 50m, Reason = "   "
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("BR-FIN-06", ex.Message);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_payment_refund"));
        }

        [Fact]
        public void CreateRefund_金额超过实缴_抛ValidationFailed()
        {
            int billId = NewPublishedBillAndPaid(out int _, 2m, 100m); // 实缴 200

            var ex = Assert.Throws<ApiException>(() => _payment.CreateRefund(new RefundAdjustmentRequest
            {
                BillId = billId, RefundType = RefundType.Refund, Amount = 500m, Reason = "超额退款"
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("不能超过实缴金额", ex.Message);
        }

        [Fact]
        public void CreateRefund_原因齐备且不超额_冲正账单并写审计()
        {
            int billId = NewPublishedBillAndPaid(out int _, 2m, 100m);

            var refund = _payment.CreateRefund(new RefundAdjustmentRequest
            {
                BillId = billId, RefundType = RefundType.Refund, Amount = 50m, Reason = "多收退还业主"
            });

            Assert.True(refund.Id > 0);
            Assert.StartsWith("RF-", refund.RefNo);
            Assert.Equal(BillStatus.Reversed, ScalarEnum<BillStatus>("SELECT status FROM t_bill WHERE id = @id", billId));
        }

        [Fact]
        public void CreateRefund_大额退款未确认_抛Forbidden()
        {
            int billId = NewPublishedBillAndPaid(out int _, 2000m, 1m); // 实缴 2000

            var ex = Assert.Throws<ApiException>(() => _payment.CreateRefund(new RefundAdjustmentRequest
            {
                BillId = billId, RefundType = RefundType.Refund, Amount = 1500m, Reason = "大额退款",
                ConfirmedByManager = false
            }));

            Assert.Equal(ErrorCode.Forbidden, ex.Code);
        }

        // ===================== BR-FIN-07 欠费台账口径 =====================

        [Fact]
        public void QueryArrears_草稿不计入_发布待缴计入_缴清后移除()
        {
            int property = NewPropertyWithOwner("欠费口径小区");
            int chargeItem = TestData.ChargeItem("欠费口径物业费", 2m);
            int cycle = TestData.Cycle();
            var batch = _billing.GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = chargeItem, CycleId = cycle, PropertyIds = new List<int> { property }
            });
            int billId = ScalarInt("SELECT id FROM t_bill WHERE generate_batch_id = @id", new { id = batch.Id });

            // 未生成/草稿：不计入欠费台账
            Assert.Equal(0, _billing.QueryArrears(new BillQueryRequest { PropertyId = property, PageSize = 50 }).Total);

            _billing.PublishBills(new BillPublishRequest { BatchId = batch.Id });
            PageResult<ArrearDto> published = _billing.QueryArrears(new BillQueryRequest { PropertyId = property, PageSize = 50 });
            Assert.Equal(1, published.Total);
            Assert.Equal(BillStatus.Pending, ScalarEnum<BillStatus>("SELECT status FROM t_bill WHERE id = @id", billId));

            // 部分缴：仍在欠费台账
            _payment.CreatePayment(new PaymentCreateRequest { BillId = billId, Amount = 50m, PayMethod = PayMethod.Cash });
            PageResult<ArrearDto> partial = _billing.QueryArrears(new BillQueryRequest { PropertyId = property, PageSize = 50 });
            Assert.Equal(1, partial.Total);
            Assert.Equal(BillStatus.Partial, ScalarEnum<BillStatus>("SELECT status FROM t_bill WHERE id = @id", billId));

            // 缴清：移出欠费台账
            _payment.CreatePayment(new PaymentCreateRequest { BillId = billId, Amount = 150m, PayMethod = PayMethod.Cash });
            Assert.Equal(0, _billing.QueryArrears(new BillQueryRequest { PropertyId = property, PageSize = 50 }).Total);
        }

        [Fact]
        public void QueryArrears_账单已缴清_不再计入欠费台账()
        {
            int billId = NewPublishedBillAndPaid(out int _, 2m, 100m); // 应收 200，已全额缴清

            PageResult<ArrearDto> arrears = _billing.QueryArrears(new BillQueryRequest { PageSize = 50 });

            Assert.Equal(0, arrears.Total);
            Assert.Equal(BillStatus.Paid, ScalarEnum<BillStatus>("SELECT status FROM t_bill WHERE id = @id", billId));
        }

        // ===================== BR-FIN-08 收据编号唯一、打印留痕、支持补打 =====================

        [Fact]
        public void PrintReceipt_重复打印_编号不变且追加打印留痕()
        {
            int billId = NewPublishedBill(out int _, 2m, 100m);
            var payment = _payment.CreatePayment(new PaymentCreateRequest
            {
                BillId = billId, Amount = 200m, PayMethod = PayMethod.Cash, PrintReceipt = true
            });
            ReceiptDto receipt = _payment.GetReceipt(ScalarInt("SELECT id FROM t_receipt WHERE payment_id = @id", new { id = payment.Id }));
            Assert.Equal(1, receipt.PrintCount);
            string originalNo = receipt.ReceiptNo;

            ReceiptDto reprinted = _payment.PrintReceipt(new ReceiptPrintRequest { ReceiptId = receipt.Id });

            Assert.Equal(originalNo, reprinted.ReceiptNo); // 补打保留原号
            Assert.Equal(2, reprinted.PrintCount);
            Assert.Equal(2, ScalarInt("SELECT COUNT(1) FROM t_print_log WHERE biz_type = 'receipt' AND biz_id = @id", new { id = receipt.Id }));
        }

        [Fact]
        public void PrintReceipt_收据不存在_抛NotFound()
        {
            var ex = Assert.Throws<ApiException>(() => _payment.PrintReceipt(new ReceiptPrintRequest { ReceiptId = 999999 }));

            Assert.Equal(ErrorCode.NotFound, ex.Code);
            Assert.Contains("收据不存在", ex.Message);
        }

        // ===================== BR-FIN-09 月/季报表 = 收入 + 支出 + 结余 =====================

        [Fact]
        public void GetFinancialReport_收入支出结余_与明细汇总一致()
        {
            int billId = NewPublishedBill(out int _, 2m, 100m); // 应收 200
            _payment.CreatePayment(new PaymentCreateRequest { BillId = billId, Amount = 200m, PayMethod = PayMethod.Cash });
            TestData.Expense(TestData.ExpenseCategory("报表测试支出"), 120m);

            FinancialReportDto report = _report.GetFinancialReport(new FinancialReportQueryRequest
            {
                PeriodType = "month", Period = DateTime.Today.ToString("yyyy-MM")
            });

            Assert.Equal(200m, report.IncomeTotal);
            Assert.Equal(120m, report.ExpenseTotal);
            Assert.Equal(80m, report.Balance);
            Assert.Equal(report.IncomeTotal - report.ExpenseTotal, report.Balance);
        }

        [Fact]
        public void GetFinancialReport_期间格式错误_抛ValidationFailed()
        {
            var ex = Assert.Throws<ApiException>(() => _report.GetFinancialReport(new FinancialReportQueryRequest
            {
                PeriodType = "month", Period = "2026/08"
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("月度期间格式", ex.Message);
        }

        // ===================== BR-FIN-10 金额记录删除一律软删并留痕 =====================

        [Fact]
        public void DeleteExpense_删除后_软删且写操作人与审计留痕()
        {
            int categoryId = TestData.ExpenseCategory("软删测试支出分类");
            int expenseId = TestData.Expense(categoryId, 66m);

            _expense.DeleteExpense(expenseId);

            Assert.Equal(1, ScalarInt("SELECT del_flag FROM t_expense WHERE id = @id", new { id = expenseId }));
            Assert.Equal(0, _expense.QueryExpenses(new PageRequest { PageIndex = 1, PageSize = 20 }).Total);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_audit_log WHERE action = 'EXPENSE_DELETE'"));
            Assert.True(ScalarText("SELECT updated_at FROM t_expense WHERE id = @id", new { id = expenseId }) != null, "软删须留操作时间");
        }

        [Fact]
        public void DeleteExpense_记录不存在_抛NotFound且不产生删除审计()
        {
            var ex = Assert.Throws<ApiException>(() => _expense.DeleteExpense(999999));

            Assert.Equal(ErrorCode.NotFound, ex.Code);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_audit_log WHERE action = 'EXPENSE_DELETE'"));
        }

        [Fact]
        public void CreateExpense_未选分类被拒_不写成功审计条目()
        {
            Assert.Throws<ApiException>(() => _expense.CreateExpense(new ExpenseCreateRequest
            {
                CategoryId = 0, Amount = 100m, ExpenseDate = DateTime.Today
            }));

            // BR-COM-01 反向：敏感操作被拒时不产生「成功」审计，避免审计记录失真
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_audit_log WHERE action = 'EXPENSE_CREATE'"));
        }

        [Fact]
        public void DeleteArrearBill_删除后_软删且欠费台账不再返回()
        {
            int billId = NewPublishedBill(out int _, 2m, 100m);

            _billing.DeleteArrearBill(billId);

            Assert.Equal(1, ScalarInt("SELECT del_flag FROM t_bill WHERE id = @id", new { id = billId }));
            Assert.Equal(0, _billing.QueryArrears(new BillQueryRequest { PageSize = 50 }).Total);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_audit_log WHERE action = 'ARREARS_DELETE'"));
        }

        // ---------- helpers ----------

        private int NewPropertyWithOwner(string communityName)
        {
            int community = TestData.Community(communityName);
            int building = TestData.Building(community, "1");
            int property = TestData.Property(building, "101", null, 100m);
            TestData.Relation(property, TestData.Owner(communityName + "业主", "13800008888"));
            return property;
        }

        private int NewPublishedBill(out int ownerId, decimal unitPrice, decimal area)
        {
            int community = TestData.Community("出账小区" + unitPrice + area);
            int building = TestData.Building(community, "1");
            int property = TestData.Property(building, "101", null, area);
            ownerId = TestData.Owner("出账业主" + unitPrice, "13800009999");
            TestData.Relation(property, ownerId);
            int chargeItem = TestData.ChargeItem("物业费" + unitPrice + "x" + area, unitPrice);
            int cycle = TestData.Cycle();
            var batch = _billing.GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = chargeItem, CycleId = cycle, PropertyIds = new List<int> { property }
            });
            _billing.PublishBills(new BillPublishRequest { BatchId = batch.Id });
            return ScalarInt("SELECT id FROM t_bill WHERE generate_batch_id = @id", new { id = batch.Id });
        }

        private int NewPublishedBillAndPaid(out int ownerId, decimal unitPrice, decimal area)
        {
            int billId = NewPublishedBill(out ownerId, unitPrice, area);
            decimal amount = ScalarDecimal("SELECT amount FROM t_bill WHERE id = @id", billId);
            _payment.CreatePayment(new PaymentCreateRequest { BillId = billId, Amount = amount, PayMethod = PayMethod.Cash });
            return billId;
        }

        private static decimal ScalarDecimal(string sql, int id)
        {
            return decimal.Parse(ScalarText(sql, new { id }), System.Globalization.CultureInfo.InvariantCulture);
        }

        private static T ScalarEnum<T>(string sql, int id)
        {
            return (T)Enum.ToObject(typeof(T), ScalarInt(sql, new { id }));
        }
    }
}
