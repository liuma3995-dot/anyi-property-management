using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PropertyManagement.Contract.Auth;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Contract.Health;

namespace PropertyManagement.Client.Services
{
    /// <summary>演示数据客户端（M3-D3 + M4 财务）：契约一致、数据为夹具，用于后端未启动时的界面演示。</summary>
    public class MockApiClient : IApiClient
    {
        public bool IsMock => true;

        private const string MockUser = "admin";
        private const string MockPassword = "Admin@123";

        private readonly List<ChargeItemDto> _items;
        private readonly List<BillingCycleDto> _cycles;
        private readonly List<BillBatchDto> _batches;
        private readonly List<BillListItemDto> _bills;
        private readonly List<ArrearDto> _arrears;
        private readonly List<ExpenseCategoryDto> _categories;
        private readonly List<ExpenseDto> _expenses;
        private readonly List<RefundAdjustmentDto> _refunds;
        private readonly List<LedgerEntryDto> _ledger;
        private readonly Dictionary<string, List<DictItemDto>> _dicts;

        public MockApiClient()
        {
            _dicts = new Dictionary<string, List<DictItemDto>>
            {
                { "charge_category", new List<DictItemDto>
                    {
                        Dict(1, "charge_category", "property_fee", "物业费", ""),
                        Dict(2, "charge_category", "agency_fee", "代收代缴", ""),
                        Dict(3, "charge_category", "parking_fee", "车位费", ""),
                        Dict(4, "charge_category", "shared_cost", "公摊分摊", ""),
                        Dict(5, "charge_category", "onetime", "一次性", "")
                    }
                },
                { "charge_method", new List<DictItemDto>
                    {
                        Dict(1, "charge_method", "area", "按建筑面积", "㎡"),
                        Dict(2, "charge_method", "house", "按户", "户"),
                        Dict(3, "charge_method", "parking", "按车位", "车位"),
                        Dict(4, "charge_method", "share", "按户均摊", "户"),
                        Dict(5, "charge_method", "onetime", "一次性", "户"),
                        Dict(6, "charge_method", "step", "阶梯单价", ""),
                        Dict(7, "charge_method", "card", "按卡", "张")
                    }
                },
                { "charge_cycle", new List<DictItemDto>
                    {
                        Dict(1, "charge_cycle", "monthly", "按月", ""),
                        Dict(2, "charge_cycle", "yearly", "按年", ""),
                        Dict(3, "charge_cycle", "onetime", "一次性", "")
                    }
                }
            };
            _items = new List<ChargeItemDto>
            {
                Item("物业服务费", ChargeObjectType.Property, "物业费", "area", "按建筑面积", "㎡", 2.80m, BillingCycleType.Monthly, "", 0),
                Item("生活垃圾处理费", ChargeObjectType.Property, "代收代缴", "house", "按户", "户", 8m, BillingCycleType.Monthly, "", 0),
                Item("地下车位管理费", ChargeObjectType.Parking, "车位费", "parking", "按车位", "车位", 50m, BillingCycleType.Monthly, "", 0),
                Item("水电公摊费", ChargeObjectType.Property, "公摊分摊", "share", "按户均摊", "户", 36m, BillingCycleType.Custom, "每季", 0),
                Item("装修管理费", ChargeObjectType.Property, "一次性", "onetime", "一次性", "户", 800m, BillingCycleType.OneTime, "一次性", 0),
                Item("门禁卡工本费", ChargeObjectType.Property, "一次性", "card", "按卡", "张", 20m, BillingCycleType.OneTime, "一次性", 1),
                Item("二次供水清洗费", ChargeObjectType.Property, "代收代缴", "house", "按户", "户", 15m, BillingCycleType.Custom, "每半年", 1)
            };
            _cycles = new List<BillingCycleDto>
            {
                new BillingCycleDto { Id = 1, CycleType = BillingCycleType.Monthly, StartDate = new DateTime(2026, 8, 1), EndDate = new DateTime(2026, 8, 31) },
                new BillingCycleDto { Id = 2, CycleType = BillingCycleType.Monthly, StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 9, 30) },
                new BillingCycleDto { Id = 3, CycleType = BillingCycleType.Yearly, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31) }
            };
            _bills = new List<BillListItemDto>
            {
                Bill(1, "物业服务费", "1栋1单元102", "李桂芳", 426m, 426m, BillStatus.Paid, new DateTime(2026, 8, 31), 1),
                Bill(2, "物业服务费", "1栋1单元102", "李桂芳", 426m, 0m, BillStatus.Pending, new DateTime(2026, 9, 30), 2),
                Bill(3, "生活垃圾处理费", "1栋1单元102", "李桂芳", 8m, 0m, BillStatus.Pending, new DateTime(2026, 9, 30), 2),
                Bill(4, "地下车位管理费", "B1-01", "李桂芳", 850m, 0m, BillStatus.Overdue, new DateTime(2026, 4, 30), 4),
                Bill(5, "物业服务费", "2栋1单元201", "孙立军", 426m, 0m, BillStatus.Overdue, new DateTime(2026, 5, 31), 5),
                Bill(6, "物业服务费", "3栋2单元301", "陈晓东", 426m, 0m, BillStatus.Overdue, new DateTime(2026, 2, 28), 6)
            };
            _arrears = new List<ArrearDto>
            {
                Arrear(4, "李桂芳", "B1-01", "地下车位管理费", 850m, 0m, new DateTime(2026, 4, 30), 120, ""),
                Arrear(5, "孙立军", "2栋1单元201", "物业服务费", 1278m, 0m, new DateTime(2026, 5, 1), 89, "电话催缴 2 次"),
                Arrear(6, "陈晓东", "3栋2单元301", "物业服务费", 2556m, 0m, new DateTime(2026, 2, 1), 178, "函件催缴")
            };
            _categories = new List<ExpenseCategoryDto>
            {
                new ExpenseCategoryDto { Id = 1, Name = "工资", CategoryType = "人事", Status = 0 },
                new ExpenseCategoryDto { Id = 2, Name = "维修维护", CategoryType = "维修", Status = 0 },
                new ExpenseCategoryDto { Id = 3, Name = "水电", CategoryType = "能源", Status = 0 },
                new ExpenseCategoryDto { Id = 4, Name = "外包", CategoryType = "服务", Status = 0 },
                new ExpenseCategoryDto { Id = 5, Name = "其他", CategoryType = "其他", Status = 0 }
            };
            _expenses = new List<ExpenseDto>
            {
                Expense(1, 2, 18600m, new DateTime(2026, 8, 27), "3 单元电梯钢丝绳更换", 0),
                Expense(2, 3, 42600m, new DateTime(2026, 8, 22), "B 区水泵房电费（7 月）", 0),
                Expense(3, 2, 3680m, new DateTime(2026, 8, 23), "2 栋门禁系统主板维修", 0)
            };
            _refunds = new List<RefundAdjustmentDto>
            {
                new RefundAdjustmentDto { Id = 1, BillId = 4, RefundType = RefundType.Refund, Amount = 426m, Reason = "重复缴费", RefNo = "REF-2608-01", CreatedAt = new DateTime(2026, 8, 28, 10, 30, 0) }
            };
            _ledger = new List<LedgerEntryDto>
            {
        Ledger(1, new DateTime(2026, 8, 28, 10, 32, 0), "payment", "SK-2026-00345", 1280m, 0m, "物业服务费 2026-08", "张伟"),
        Ledger(2, new DateTime(2026, 8, 28, 9, 18, 0), "payment", "SK-2026-00344", 426m, 0m, "物业服务费 2026-07", "李娜"),
                Ledger(3, new DateTime(2026, 8, 27, 16, 5, 0), "expense", "ZC-2608-12", 0m, 18600m, "维修支出 3 单元电梯钢丝绳更换"),
        Ledger(4, new DateTime(2026, 8, 27, 11, 40, 0), "payment", "SK-2026-00343", 2000m, 0m, "预存款转入", "王芳"),
                Ledger(5, new DateTime(2026, 8, 26, 10, 7, 0), "expense", "ZC-2608-11", 0m, 4320m, "绿化支出 八月绿植养护"),
        Ledger(6, new DateTime(2026, 8, 25, 17, 48, 0), "refund", "REF-2608-01", 0m, 426m, "冲销（重复缴费退回）", "张伟")
            };
            _batches = new List<BillBatchDto>();
        }

        public Task<HealthResponse> GetHealthAsync()
        {
            return Task.FromResult(new HealthResponse
            {
                Service = "PropertyManagement.Server（Mock）",
                Version = "v0.1-mock",
                Status = "ok",
                ServerTime = DateTime.Now
            });
        }

        public Task<LoginResult> LoginAsync(LoginRequest request)
        {
            if (request == null ||
                !string.Equals(request.UserName, MockUser, StringComparison.OrdinalIgnoreCase) ||
                request.Password != MockPassword)
            {
                throw new ApiClientException(ErrorCode.Unauthorized, "用户名或密码错误（演示账号 admin / Admin@123）");
            }

            return Task.FromResult(new LoginResult
            {
                Token = "mock-token-" + Guid.NewGuid().ToString("N"),
                DisplayName = "系统管理员",
                ExpiresAt = DateTime.Now.AddHours(8)
            });
        }

        public Task LogoutAsync()
        {
            return Task.CompletedTask;
        }

        public Task ChangePasswordAsync(ChangePasswordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            {
                throw new ApiClientException(ErrorCode.ValidationFailed, "新密码长度不能少于 6 位");
            }
            return Task.CompletedTask;
        }

        public Task<DashboardDto> GetDashboardAsync()
        {
            var dto = new DashboardDto
            {
                PendingReminders = 6,
                ArrearAmount = 25000.00m,
                ArrearCount = 8,
                HandlingEmergency = 1,
                PendingReview = 2,
                HandlingDisputes = 3,
                RepairingDevices = 2,
                MonthReceivable = 120000.00m,
                ReceivableTrend = "较上月 +6.8%",
                MonthReceived = 95000.00m,
                CollectionRate = 79.2m,
                ReceivedTrend = "收缴率 79.2%",
                OverdueTrend = "较上月 -2 户",
                MaintenanceDue = 2,
                DutyToday = 4,
                RecentReminders = new List<ReminderDto>
                {
                    new ReminderDto { Id = 1, Type = "EQUIPMENT_INSPECTION", DueAt = DateTime.Now.AddDays(3), Status = ReminderStatus.Pending },
                    new ReminderDto { Id = 2, Type = "ARREARS", DueAt = DateTime.Now.AddDays(1), Status = ReminderStatus.Pending },
                    new ReminderDto { Id = 3, Type = "DISPUTE_OVERDUE", DueAt = DateTime.Now.AddDays(5), Status = ReminderStatus.Pending }
                }
            };
            return Task.FromResult(dto);
        }

        // ==================== M4 财务收费（PG-FIN-01~08） ====================

        public Task<List<ChargeItemDto>> GetChargeItemsAsync(string keyword = null, string category = null)
        {
            IEnumerable<ChargeItemDto> list = _items;
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                list = list.Where(x => x.Name.Contains(keyword));
            }
            if (!string.IsNullOrWhiteSpace(category))
            {
                list = list.Where(x => string.Equals(x.Category, category, StringComparison.Ordinal));
            }
            return Task.FromResult(list.ToList());
        }

        public Task<ChargeItemDto> CreateChargeItemAsync(ChargeItemRequest request)
        {
            var item = new ChargeItemDto
            {
                Id = _items.Count + 1,
                Name = request.Name,
                PayMode = request.PayMode,
                UnitPrice = request.UnitPrice,
                CycleType = request.CycleType,
                ObjectType = request.ObjectType,
                Status = request.Status ?? 0,
                Category = request.Category,
                MethodCode = request.MethodCode,
                MethodName = request.MethodName,
                PriceUnit = request.PriceUnit,
                CycleName = request.CycleName
            };
            _items.Add(item);
            return Task.FromResult(item);
        }

        public Task<ChargeItemDto> UpdateChargeItemAsync(int id, ChargeItemRequest request)
        {
            ChargeItemDto item = _items.First(x => x.Id == id);
            item.Name = request.Name;
            item.PayMode = request.PayMode;
            item.UnitPrice = request.UnitPrice;
            item.CycleType = request.CycleType;
            item.ObjectType = request.ObjectType;
            item.Status = request.Status ?? 0;
            item.Category = request.Category;
            item.MethodCode = request.MethodCode;
            item.MethodName = request.MethodName;
            item.PriceUnit = request.PriceUnit;
            item.CycleName = request.CycleName;
            return Task.FromResult(item);
        }

        public Task DeleteChargeItemAsync(int id)
        {
            ChargeItemDto item = _items.First(x => x.Id == id);
            item.Status = 1;
            return Task.CompletedTask;
        }

        public Task<List<DictItemDto>> GetDictItemsAsync(string typeCode)
        {
            List<DictItemDto> list;
            if (!_dicts.TryGetValue(typeCode, out list))
            {
                list = new List<DictItemDto>();
            }
            return Task.FromResult(list.ToList());
        }

        public Task<DictItemDto> CreateDictItemAsync(string typeCode, DictItemCreateRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ItemName))
            {
                throw new ApiClientException(ErrorCode.ValidationFailed, "自定义项名称不能为空");
            }
            List<DictItemDto> list;
            if (!_dicts.TryGetValue(typeCode, out list))
            {
                list = new List<DictItemDto>();
                _dicts[typeCode] = list;
            }
            string name = request.ItemName.Trim();
            if (list.Any(x => string.Equals(x.ItemName, name, StringComparison.Ordinal)))
            {
                throw new ApiClientException(ErrorCode.ValidationFailed, "同名字典项已存在");
            }
            var item = new DictItemDto
            {
                Id = list.Count + 1,
                TypeCode = typeCode,
                ItemCode = "C" + (100 + list.Count + 1),
                ItemName = name,
                Remark = request.Remark ?? string.Empty,
                Sort = list.Count + 1,
                Status = DictItemStatus.Enabled
            };
            list.Add(item);
            return Task.FromResult(item);
        }

        public Task<List<BillingCycleDto>> GetCyclesAsync()
        {
            return Task.FromResult(_cycles.ToList());
        }

        public Task<BillingCycleDto> CreateCycleAsync(BillingCycleRequest request)
        {
            if (request.EndDate < request.StartDate)
            {
                throw new ApiClientException(422, "周期结束日期不能早于开始日期");
            }
            var cycle = new BillingCycleDto
            {
                Id = _cycles.Count + 1,
                CycleType = request.CycleType,
                StartDate = request.StartDate,
                EndDate = request.EndDate
            };
            _cycles.Add(cycle);
            return Task.FromResult(cycle);
        }

        public Task<BillGenerateLogDto> GenerateBillsAsync(BillGenerateRequest request)
        {
            int id = _batches.Count + 1;
            int fail = 0;
            var failures = new List<BillFailureDto>();
            foreach (BillListItemDto b in _bills)
            {
                if (b.ChargeItemId == request.ChargeItemId && b.CycleId == request.CycleId)
                {
                    fail++;
                    failures.Add(new BillFailureDto { No = b.PropertyNo, Reason = "同对象同周期同项目账单已存在（BR-FIN-01）" });
                }
            }
            var log = new BillGenerateLogDto { Id = id, GenerateAt = DateTime.Now, Total = 2, Success = fail == 0 ? 2 : 0, Fail = fail, FailDetail = "{\"failures\":[]}" };
            _batches.Add(new BillBatchDto
            {
                Id = id,
                BatchNo = "BILL-" + id,
                ChargeItemName = "停车费",
                CyclePeriod = "2026-01-01 ~ 2026-12-31",
                HouseCount = 2,
                TotalAmount = 2400m,
                SuccessCount = log.Success,
                FailCount = fail,
                DraftCount = fail == 0 ? 2 : 0,
                GenerateAt = DateTime.Now
            });
            return Task.FromResult(log);
        }

        public Task<BillGenerateLogDto> PublishBillsAsync(BillPublishRequest request)
        {
            BillBatchDto batch = _batches.First(x => x.Id == request.BatchId);
            batch.DraftCount = 0;
            batch.PendingCount = batch.HouseCount;
            batch.PublishedAtRaw = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return Task.FromResult(new BillGenerateLogDto { Id = batch.Id, GenerateAt = DateTime.Now, Total = batch.HouseCount, Success = batch.HouseCount, Fail = 0 });
        }

        public Task<BillGenerateLogDto> RetryFailuresAsync(int batchId)
        {
            return Task.FromResult(new BillGenerateLogDto { Id = batchId, GenerateAt = DateTime.Now, Total = 2, Success = 2, Fail = 0 });
        }

        public Task<List<BillBatchDto>> GetGenerateLogsAsync()
        {
            return Task.FromResult(_batches.ToList());
        }

        public Task DeleteBillBatchAsync(int batchId)
        {
            BillBatchDto batch = _batches.FirstOrDefault(x => x.Id == batchId);
            if (batch == null)
            {
                throw new ApiClientException(404, "账单批次不存在");
            }
            _batches.Remove(batch);
            return Task.CompletedTask;
        }

        public Task<List<BillFailureDto>> GetFailuresAsync(int batchId)
        {
            return Task.FromResult(new List<BillFailureDto>
            {
                new BillFailureDto { PropertyId = 1, No = "B1-01", Reason = "同对象同周期同项目账单已存在（BR-FIN-01）" },
                new BillFailureDto { PropertyId = 2, No = "B1-02", Reason = "同对象同周期同项目账单已存在（BR-FIN-01）" }
            });
        }

        public Task<PageResult<BillListItemDto>> QueryBillsAsync(BillQueryRequest request)
        {
            IEnumerable<BillListItemDto> list = _bills;
            if (request.Status.HasValue)
            {
                list = list.Where(x => x.Status == request.Status.Value);
            }
            if (request.ArrearsOnly)
            {
                list = list.Where(x => x.Amount > x.PaidAmount && (x.Status == BillStatus.Pending || x.Status == BillStatus.Partial || x.Status == BillStatus.Overdue));
            }
            return Page(list);
        }

        public Task<PageResult<ArrearDto>> QueryArrearsAsync(BillQueryRequest request)
        {
            return Page(_arrears);
        }

        public Task RecordRemindAsync(ArrearRemindRequest request)
        {
            return Task.CompletedTask;
        }

        public Task DeleteArrearAsync(int billId)
        {
            _arrears.RemoveAll(x => x.BillId == billId);
            _bills.RemoveAll(x => x.Id == billId);
            return Task.CompletedTask;
        }

        public Task<PaymentStatisticsDto> GetPaymentStatisticsAsync()
        {
            return Task.FromResult(new PaymentStatisticsDto
            {
                TotalBills = _bills.Count,
                TotalAmount = _bills.Sum(x => x.Amount),
                PaidCount = _bills.Count(x => x.Status == BillStatus.Paid),
                PaidAmount = _bills.Where(x => x.Status == BillStatus.Paid).Sum(x => x.PaidAmount),
                UnpaidCount = _bills.Count(x => x.Amount > x.PaidAmount),
                UnpaidAmount = _bills.Where(x => x.Amount > x.PaidAmount).Sum(x => x.Amount - x.PaidAmount)
            });
        }

        public Task<PaymentDto> CreatePaymentAsync(PaymentCreateRequest request)
        {
            BillListItemDto bill = _bills.First(x => x.Id == request.BillId);
            bill.PaidAmount = Math.Min(bill.Amount, bill.PaidAmount + request.Amount);
            bill.Status = bill.PaidAmount >= bill.Amount ? BillStatus.Paid : BillStatus.Partial;
            return Task.FromResult(new PaymentDto
            {
                Id = 100 + request.BillId,
                BillId = request.BillId,
                Amount = request.Amount,
                PayMethod = request.PayMethod,
                PaidAt = DateTime.Now,
                Status = PaymentStatus.Normal,
                ToPreDeposit = Math.Max(0, request.Amount - (bill.Amount - bill.PaidAmount))
            });
        }

        public Task<PaymentDto> GetPaymentAsync(int id)
        {
            return Task.FromResult(new PaymentDto { Id = id, BillId = 1, Amount = 1280m, PayMethod = PayMethod.Cash, PaidAt = DateTime.Now, Status = PaymentStatus.Normal });
        }

        public Task<ReceiptDto> GetReceiptByPaymentAsync(int paymentId)
        {
            return Task.FromResult(new ReceiptDto { Id = paymentId, PaymentId = paymentId, ReceiptNo = "SK-2026-00345", PrintCount = 1, PrintedAt = DateTime.Now });
        }

        public Task<ReceiptDto> PrintReceiptAsync(int receiptId)
        {
            return Task.FromResult(new ReceiptDto { Id = receiptId, PaymentId = receiptId, ReceiptNo = "SK-2026-00345", PrintCount = 2, PrintedAt = DateTime.Now });
        }

        public Task<PreDepositDto> GetPreDepositAsync(int ownerId)
        {
            return Task.FromResult(new PreDepositDto { Id = ownerId, OwnerId = ownerId, Balance = 100m, UpdatedAt = DateTime.Now });
        }

        public Task<RefundAdjustmentDto> CreateRefundAsync(RefundAdjustmentRequest request)
        {
            var dto = new RefundAdjustmentDto
            {
                Id = _refunds.Count + 1,
                BillId = request.BillId,
                RefundType = request.RefundType,
                Amount = request.Amount,
                Reason = request.Reason,
                RefNo = "REF-" + DateTime.Now.ToString("yyMMdd") + "-" + (_refunds.Count + 1),
                CreatedAt = DateTime.Now
            };
            _refunds.Insert(0, dto);
            return Task.FromResult(dto);
        }

        public Task<PageResult<RefundAdjustmentDto>> QueryRefundsAsync(PageRequest request)
        {
            return Page(_refunds);
        }

        public Task<List<ExpenseCategoryDto>> GetExpenseCategoriesAsync()
        {
            return Task.FromResult(_categories.ToList());
        }

        public Task<ExpenseDto> CreateExpenseAsync(ExpenseCreateRequest request)
        {
            ExpenseCategoryDto cat = _categories.First(x => x.Id == request.CategoryId);
            var dto = new ExpenseDto
            {
                Id = _expenses.Count + 1,
                CategoryId = request.CategoryId,
                CategoryName = cat.Name,
                Amount = request.Amount,
                ExpenseDate = request.ExpenseDate == default(DateTime) ? DateTime.Now : request.ExpenseDate,
                Note = request.Note,
                Status = 0
            };
            _expenses.Insert(0, dto);
            return Task.FromResult(dto);
        }

        public Task<PageResult<ExpenseDto>> QueryExpensesAsync(PageRequest request)
        {
            return Page(_expenses);
        }

        public Task DeleteExpenseAsync(int id)
        {
            ExpenseDto e = _expenses.FirstOrDefault(x => x.Id == id);
            if (e != null) { e.Status = 1; }
            return Task.CompletedTask;
        }

        public Task<PageResult<LedgerEntryDto>> GetLedgerAsync(LedgerQueryRequest request)
        {
            return Page(_ledger);
        }

        public Task<FinancialReportDto> GetFinancialReportAsync(FinancialReportQueryRequest request)
        {
            return Task.FromResult(new FinancialReportDto
            {
                Period = request == null ? "2026-08" : request.Period,
                ComparePeriod = request == null || string.IsNullOrEmpty(request.ComparePeriod) ? "2026-07" : request.ComparePeriod,
                IncomeTotal = 192338m,
                ExpenseTotal = 86420m,
                Balance = 105918m,
                IncomeMomPercent = 3.5m,
                ExpenseMomPercent = -2.1m,
                Items = new List<ReportItemDto>
                {
                    new ReportItemDto { Date = new DateTime(2026, 8, 28, 10, 32, 0), Type = "income", Category = "物业服务费", Amount = 1280m, Note = "SK-2026-00345" },
                    new ReportItemDto { Date = new DateTime(2026, 8, 27, 16, 5, 0), Type = "expense", Category = "维修支出", Amount = 18600m, Note = "ZC-2608-12" },
                    new ReportItemDto { Date = new DateTime(2026, 8, 26, 10, 7, 0), Type = "expense", Category = "绿化支出", Amount = 4320m, Note = "ZC-2608-11" }
                },
                SummaryItems = new List<FinancialSummaryItemDto>
                {
                    new FinancialSummaryItemDto { Category = "物业服务费", CurrentAmount = 182560m, PreviousAmount = 176400m, MoM = 3.5m, QuarterTotal = 521300m, Remark = "收入" },
                    new FinancialSummaryItemDto { Category = "车位费", CurrentAmount = 9778m, PreviousAmount = 9200m, MoM = 6.3m, QuarterTotal = 28150m, Remark = "收入" },
                    new FinancialSummaryItemDto { Category = "退款/冲减", CurrentAmount = -426m, PreviousAmount = 0m, MoM = null, QuarterTotal = -426m, Remark = "冲销" },
                    new FinancialSummaryItemDto { Category = "维修支出", CurrentAmount = 62100m, PreviousAmount = 65400m, MoM = -5.0m, QuarterTotal = 189600m, Remark = "支出" },
                    new FinancialSummaryItemDto { Category = "绿化支出", CurrentAmount = 24320m, PreviousAmount = 23900m, MoM = 1.8m, QuarterTotal = 70200m, Remark = "支出" }
                }
            });
        }

        public Task<ReportLogDto> ExportReportAsync(ReportExportRequest request)
        {
            return Task.FromResult(new ReportLogDto
            {
                Id = 1,
                ReportType = "financial",
                Period = request.Query == null ? "2026-08" : request.Query.Period,
                Format = request.Format,
                FilePath = "C:\\ProgramData\\PropertyManagement\\exports\\demo.xlsx",
                CreatedAt = DateTime.Now
            });
        }

        // ==================== 内部工具 ====================

        private static Task<PageResult<T>> Page<T>(IEnumerable<T> list)
        {
            var arr = list.ToList();
            return Task.FromResult(new PageResult<T> { PageIndex = 1, PageSize = 20, Total = arr.Count, Items = arr });
        }

        private static ChargeItemDto Item(string name, ChargeObjectType obj, string category, string methodCode, string methodName, string priceUnit, decimal price, BillingCycleType cycle, string cycleName, int status)
        {
            return new ChargeItemDto { Id = _seed++, Name = name, ObjectType = obj, PayMode = ChargePayMode.Monthly, UnitPrice = price, CycleType = cycle, Status = status, Category = category, MethodCode = methodCode, MethodName = methodName, PriceUnit = priceUnit, CycleName = cycleName };
        }

        private static DictItemDto Dict(int id, string typeCode, string itemCode, string itemName, string remark)
        {
            return new DictItemDto { Id = id, TypeCode = typeCode, ItemCode = itemCode, ItemName = itemName, Remark = remark, Sort = id, Status = DictItemStatus.Enabled };
        }

        private static ChargeItemDto Item(string name, ChargeObjectType obj, ChargePayMode mode, decimal price, BillingCycleType cycle, int status)
        {
            return new ChargeItemDto { Id = _seed++, Name = name, ObjectType = obj, PayMode = mode, UnitPrice = price, CycleType = cycle, Status = status };
        }

        private static int _seed = 1;

        private static BillListItemDto Bill(int id, string item, string no, string owner, decimal amount, decimal paid, BillStatus status, DateTime due, int cycleId)
        {
            return new BillListItemDto
            {
                Id = id,
                ChargeItemId = 1,
                ChargeItemName = item,
                PropertyNo = no,
                OwnerName = owner,
                CycleId = cycleId,
                CyclePeriod = due.AddMonths(-1).ToString("yyyy-MM") + " ~ " + due.ToString("yyyy-MM"),
                Amount = amount,
                PaidAmount = paid,
                Status = status,
                DueAt = due
            };
        }

        private static ArrearDto Arrear(int billId, string owner, string no, string item, decimal amount, decimal paid, DateTime due, int aging, string remind)
        {
            return new ArrearDto
            {
                BillId = billId,
                OwnerName = owner,
                PropertyNo = no,
                ChargeItemName = item,
                Amount = amount,
                PaidAmount = paid,
                ArrearAmount = amount - paid,
                DueAt = due,
                AgingDays = aging,
                RemindChannel = remind
            };
        }

        private static ExpenseDto Expense(int id, int catId, decimal amount, DateTime date, string note, int status)
        {
            return new ExpenseDto { Id = id, CategoryId = catId, CategoryName = "", Amount = amount, ExpenseDate = date, Note = note, Status = status };
        }

        private static LedgerEntryDto Ledger(int id, DateTime time, string type, string no, decimal inAmt, decimal outAmt, string summary, string ownerName = "")
        {
            string subject = summary;
            if (summary.Contains("预存")) { subject = "预存款"; }
            string payMethod = type == "payment" ? "现金" : (type == "refund" ? "原路退回" : "银行转账");
            return new LedgerEntryDto
            {
                Id = id, BizTime = time, BizType = type, BizNo = no,
                InAmount = inAmt, OutAmount = outAmt, Summary = summary,
                Subject = subject, PayMethod = payMethod, OperatorName = "系统管理员",
                OwnerName = ownerName
            };
        }
    }
}
