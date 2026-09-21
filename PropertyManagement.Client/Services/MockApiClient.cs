using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PropertyManagement.Contract.Auth;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Contract.Health;
using PropertyManagement.Contract.Org;
using PropertyManagement.Contract.PhoneBook;
using PropertyManagement.Contract.Dispute;
using PropertyManagement.Contract.Emergency;
using PropertyManagement.Contract.Equipment;

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
            // 与后端简化规则一致：6-20 位且必须同时包含字母和数字（演示模式同样给出规则提示）
            if (request.NewPassword.Length > 20 ||
                !System.Text.RegularExpressions.Regex.IsMatch(request.NewPassword, "[A-Za-z]") ||
                !System.Text.RegularExpressions.Regex.IsMatch(request.NewPassword, "[0-9]"))
            {
                throw new ApiClientException(ErrorCode.ValidationFailed, "新密码需 6-20 位且同时包含字母和数字");
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
                // CHG-v1.1.2-55：收缴率分子（本月账期账单已收）+ 环比（百分点差）
                MonthCycleReceived = 95000.00m,
                CollectionRateTrend = "较上月 +1.2 个百分点",
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

        // ==================== R17 顶部栏与仪表盘交互（演示数据） ====================

        public Task<DashboardDto> GetDashboardAsync(string period)
        {
            return GetDashboardAsync();
        }

        public Task<TodoCenterDto> GetTodosAsync(int limit = 20)
        {
            var items = new List<TodoItemDto>
            {
                new TodoItemDto
                {
                    Id = "reminder-1", Kind = "maintenance", KindText = "到期提醒",
                    Title = "B 栋电梯 年检到期",
                    Meta = "设备台账  ·  截止 " + DateTime.Today.AddDays(3).ToString("MM-dd") + "  ·  责任班组 工程部",
                    Tag = "3 天后到期", Level = "warning", DueAt = DateTime.Today.AddDays(3),
                    TargetModule = "equipment", TargetPage = "到期提醒", TargetId = 1
                },
                new TodoItemDto
                {
                    Id = "bill-1", Kind = "arrears", KindText = "欠费催缴",
                    Title = "3 栋 201 室 欠费 ¥1,200.00",
                    Meta = "财务收费  ·  应收 ¥1,200.00  ·  逾期 12 天  ·  业主 张伟",
                    Tag = "逾期", Level = "danger", DueAt = DateTime.Today.AddDays(-12),
                    TargetModule = "finance", TargetPage = "欠费台账", TargetId = 1
                },
                new TodoItemDto
                {
                    Id = "dispute-1", Kind = "dispute", KindText = "纠纷处理",
                    Title = "JF-2609-003  楼上漏水纠纷",
                    Meta = "纠纷调解  ·  漏水  ·  发生 08-26（已 17 天）",
                    Tag = "处理中", Level = "info", DueAt = DateTime.Today.AddDays(-17),
                    TargetModule = "dispute", TargetPage = "纠纷列表", TargetId = 1
                }
            };

            var center = new TodoCenterDto
            {
                Total = items.Count,
                CountByKind = items.GroupBy(x => x.Kind).ToDictionary(g => g.Key, g => g.Count()),
                Items = items.Take(limit <= 0 ? 20 : limit).ToList()
            };
            return Task.FromResult(center);
        }

        public Task<GlobalSearchResultDto> SearchAsync(string keyword)
        {
            var result = new GlobalSearchResultDto
            {
                Keyword = keyword ?? string.Empty,
                Groups = new List<GlobalSearchGroupDto>()
            };
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return Task.FromResult(result);
            }

            result.Groups.Add(new GlobalSearchGroupDto
            {
                Module = "baseinfo",
                ModuleName = "基础信息",
                Items = new List<GlobalSearchItemDto>
                {
                    new GlobalSearchItemDto
                    {
                        Title = "3 栋 1 单元 " + keyword, Subtitle = "安怡花园  ·  建筑面积 88.5 ㎡",
                        TargetModule = "baseinfo", TargetPage = "房产列表", Keyword = keyword, TargetId = 1
                    }
                }
            });
            result.Total = 1;
            return Task.FromResult(result);
        }

        private string _profileDisplayName;
        private string _profilePhone;
        private string _profileBio;
        private string _profileAvatarKey;

        public Task<UserProfileDto> GetProfileAsync()
        {
            return Task.FromResult(new UserProfileDto
            {
                Id = 1, UserName = MockUser, DisplayName = _profileDisplayName,
                Phone = _profilePhone, Bio = _profileBio, AvatarKey = _profileAvatarKey,
                Role = "系统管理员", AvatarOptions = MockAvatarOptions()
            });
        }

        public Task<UserProfileDto> UpdateProfileAsync(UserProfileRequest request)
        {
            _profileDisplayName = request == null ? null : request.DisplayName;
            _profilePhone = request == null ? null : request.Phone;
            _profileBio = request == null ? null : request.Bio;
            _profileAvatarKey = request == null ? null : request.AvatarKey;
            return GetProfileAsync();
        }

        private static List<AvatarOptionDto> MockAvatarOptions()
        {
            var keys = new[] { "avatar-01", "avatar-02", "avatar-03", "avatar-04", "avatar-05", "avatar-06", "avatar-07", "avatar-08" };
            var labels = new[] { "管理员", "客服", "工程", "安保", "财务", "保洁", "秩序", "访客" };
            var list = new List<AvatarOptionDto>();
            for (int i = 0; i < keys.Length; i++)
            {
                list.Add(new AvatarOptionDto { Key = keys[i], Label = labels[i] });
            }
            return list;
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
                ObjectType = request.ObjectType ?? ChargeObjectType.Property,
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
            item.ObjectType = request.ObjectType ?? item.ObjectType;
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

        // ---------- CHG-v1.1.2-26：价目表与计量变量（演示夹具） ----------

        private readonly List<ChargeVariableDto> _variables = new List<ChargeVariableDto>
        {
            new ChargeVariableDto { Id = 1, VarCode = "MQ-01", VarName = "建筑面积", Unit = "㎡", ValueType = ChargeVariableValueType.Decimal, Source = ChargeVariableSource.Archive, ObjectScope = ChargeVariableScope.Property, FieldKey = "property.area", IsBuiltin = true, Status = 0, Sort = 1 },
            new ChargeVariableDto { Id = 4, VarCode = "MQ-04", VarName = "月数", Unit = "月", ValueType = ChargeVariableValueType.Integer, Source = ChargeVariableSource.CycleDerived, ObjectScope = ChargeVariableScope.Any, IsBuiltin = true, Status = 0, Sort = 4 },
            new ChargeVariableDto { Id = 6, VarCode = "MQ-06", VarName = "数量", Unit = "个", ValueType = ChargeVariableValueType.Integer, Source = ChargeVariableSource.Fixed, DefaultValue = 1m, ObjectScope = ChargeVariableScope.Any, IsBuiltin = true, Status = 0, Sort = 6 },
            new ChargeVariableDto { Id = 9, VarCode = "MQ-09", VarName = "台数", Unit = "台", ValueType = ChargeVariableValueType.Integer, Source = ChargeVariableSource.Manual, ObjectScope = ChargeVariableScope.Custom, IsBuiltin = true, Status = 0, Sort = 9 }
        };

        private readonly List<ChargeStandardDto> _standards = new List<ChargeStandardDto>
        {
            new ChargeStandardDto
            {
                Id = 1, Name = "物业服务费", Category = "物业费", Status = 0, ItemCount = 1,
                Variables = new List<ChargeVariableDto>(),
                Specs = new List<ChargeStandardSpecDto>
                {
                    new ChargeStandardSpecDto { Id = 1, StandardId = 1, SpecName = "住宅", MatchUsage = 0, UnitPrice = 1.50m, Formula = "{v:1} * {v:4} * 单价", PriceUnit = "元/㎡·月", CycleName = "每月", EffectiveFrom = "2026-01-01", Status = 0 },
                    new ChargeStandardSpecDto { Id = 2, StandardId = 1, SpecName = "商铺", MatchUsage = 1, UnitPrice = 3.00m, Formula = "{v:1} * {v:4} * 单价", PriceUnit = "元/㎡·月", CycleName = "每月", EffectiveFrom = "2026-01-01", Status = 0 },
                    new ChargeStandardSpecDto { Id = 3, StandardId = 1, SpecName = "统一价", IsFallback = true, UnitPrice = 1.80m, Formula = "{v:1} * {v:4} * 单价", PriceUnit = "元/㎡·月", CycleName = "每月", EffectiveFrom = "2026-01-01", Status = 0 }
                }
            },
            new ChargeStandardDto
            {
                Id = 2, Name = "门禁卡工本费", Category = "一次性", Status = 0, ItemCount = 1,
                Variables = new List<ChargeVariableDto>(),
                Specs = new List<ChargeStandardSpecDto>
                {
                    new ChargeStandardSpecDto { Id = 4, StandardId = 2, SpecName = "统一价", IsFallback = true, UnitPrice = 20.00m, Formula = "单价", PriceUnit = "元", CycleName = "一次性", EffectiveFrom = "2026-01-01", Status = 0 }
                }
            }
        };

        public Task<List<ChargeStandardDto>> GetChargeStandardsAsync(string keyword = null, string category = null, bool includeDisabled = true)
        {
            IEnumerable<ChargeStandardDto> list = _standards;
            if (!string.IsNullOrWhiteSpace(keyword)) { list = list.Where(x => x.Name.Contains(keyword)); }
            if (!includeDisabled) { list = list.Where(x => x.Status == 0); }
            return Task.FromResult(list.Select(CloneStandard).ToList());
        }

        public Task<ChargeStandardDto> GetChargeStandardAsync(int id)
        {
            return Task.FromResult(CloneStandard(_standards.First(x => x.Id == id)));
        }

        public Task<ChargeStandardDto> CreateChargeStandardAsync(ChargeStandardRequest request)
        {
            var dto = new ChargeStandardDto
            {
                Id = _standards.Count + 1,
                Name = request.Name,
                Category = request.Category,
                Remark = request.Remark,
                Status = request.Status ?? 0,
                Specs = new List<ChargeStandardSpecDto>(),
                Variables = (request.VariableIds ?? new List<int>())
                    .Select(id => _variables.FirstOrDefault(v => v.Id == id))
                    .Where(v => v != null).ToList()
            };
            _standards.Add(dto);
            return Task.FromResult(CloneStandard(dto));
        }

        public Task<ChargeStandardDto> UpdateChargeStandardAsync(int id, ChargeStandardRequest request)
        {
            ChargeStandardDto dto = _standards.First(x => x.Id == id);
            dto.Name = request.Name;
            dto.Category = request.Category;
            dto.Remark = request.Remark;
            dto.Status = request.Status ?? dto.Status;
            if (request.VariableIds != null)
            {
                dto.Variables = request.VariableIds
                    .Select(vid => _variables.FirstOrDefault(v => v.Id == vid))
                    .Where(v => v != null).ToList();
            }
            return Task.FromResult(CloneStandard(dto));
        }

        public Task DeleteChargeStandardAsync(int id)
        {
            _standards.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }

        public Task<ChargeStandardDto> ToggleChargeStandardAsync(int id, int status)
        {
            ChargeStandardDto dto = _standards.First(x => x.Id == id);
            dto.Status = status;
            return Task.FromResult(CloneStandard(dto));
        }

        public Task<ChargeStandardSpecDto> CreateChargeSpecAsync(int standardId, ChargeStandardSpecRequest request)
        {
            ChargeStandardDto standard = _standards.First(x => x.Id == standardId);
            var spec = new ChargeStandardSpecDto
            {
                Id = _standards.SelectMany(x => x.Specs ?? new List<ChargeStandardSpecDto>()).Count() + 1,
                StandardId = standardId,
                SpecName = request.SpecName,
                MatchUsage = request.MatchUsage,
                MatchStatus = request.MatchStatus,
                MatchSpaceType = request.MatchSpaceType,
                MatchBuilding = request.MatchBuilding,
                IsFallback = request.IsFallback,
                UnitPrice = request.UnitPrice,
                Formula = request.Formula,
                PriceUnit = "元",
                CycleName = request.CycleName,
                EffectiveFrom = request.EffectiveFrom,
                Remark = request.Remark,
                Status = request.Status ?? 0
            };
            if (standard.Specs == null) { standard.Specs = new List<ChargeStandardSpecDto>(); }
            if (request.DeprecateSameName)
            {
                foreach (ChargeStandardSpecDto old in standard.Specs.Where(x => x.SpecName == request.SpecName))
                {
                    old.Status = 1;
                }
            }
            standard.Specs.Add(spec);
            return Task.FromResult(spec);
        }

        public Task<ChargeStandardSpecDto> UpdateChargeSpecAsync(int id, ChargeStandardSpecRequest request)
        {
            ChargeStandardSpecDto spec = _standards.SelectMany(x => x.Specs ?? new List<ChargeStandardSpecDto>())
                .First(x => x.Id == id);
            spec.SpecName = request.SpecName;
            spec.MatchUsage = request.MatchUsage;
            spec.MatchStatus = request.MatchStatus;
            spec.MatchSpaceType = request.MatchSpaceType;
            spec.MatchBuilding = request.MatchBuilding;
            spec.IsFallback = request.IsFallback;
            spec.UnitPrice = request.UnitPrice;
            spec.Formula = request.Formula;
            spec.CycleName = request.CycleName;
            spec.EffectiveFrom = request.EffectiveFrom;
            spec.Remark = request.Remark;
            spec.Status = request.Status ?? spec.Status;
            return Task.FromResult(spec);
        }

        public Task DeleteChargeSpecAsync(int id)
        {
            foreach (ChargeStandardDto standard in _standards)
            {
                if (standard.Specs != null) { standard.Specs.RemoveAll(x => x.Id == id); }
            }
            return Task.CompletedTask;
        }

        public Task ToggleChargeSpecAsync(int id, int status)
        {
            ChargeStandardSpecDto spec = _standards.SelectMany(x => x.Specs ?? new List<ChargeStandardSpecDto>())
                .First(x => x.Id == id);
            spec.Status = status;
            return Task.CompletedTask;
        }

        public Task<List<ChargeVariableDto>> GetChargeVariablesAsync(string keyword = null, bool includeDisabled = true)
        {
            IEnumerable<ChargeVariableDto> list = _variables;
            if (!string.IsNullOrWhiteSpace(keyword)) { list = list.Where(x => x.VarName.Contains(keyword)); }
            if (!includeDisabled) { list = list.Where(x => x.Status == 0); }
            return Task.FromResult(list.ToList());
        }

        public Task<ChargeVariableDto> CreateChargeVariableAsync(ChargeVariableRequest request)
        {
            var dto = new ChargeVariableDto
            {
                Id = _variables.Count + 1,
                VarCode = "MQ-" + (_variables.Count + 1).ToString("00"),
                VarName = request.VarName,
                Unit = request.Unit,
                ValueType = request.ValueType,
                Source = request.Source,
                DefaultValue = request.DefaultValue,
                ObjectScope = request.ObjectScope,
                Remark = request.Remark,
                Status = request.Status ?? 0
            };
            _variables.Add(dto);
            return Task.FromResult(dto);
        }

        public Task<ChargeVariableDto> UpdateChargeVariableAsync(int id, ChargeVariableRequest request)
        {
            ChargeVariableDto dto = _variables.First(x => x.Id == id);
            dto.VarName = request.VarName;
            dto.Unit = request.Unit;
            dto.ValueType = request.ValueType;
            dto.Source = request.Source;
            dto.DefaultValue = request.DefaultValue;
            dto.ObjectScope = request.ObjectScope;
            dto.Remark = request.Remark;
            dto.Status = request.Status ?? dto.Status;
            return Task.FromResult(dto);
        }

        public Task DeleteChargeVariableAsync(int id)
        {
            _variables.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }

        public Task ToggleChargeVariableAsync(int id, int status)
        {
            ChargeVariableDto dto = _variables.First(x => x.Id == id);
            dto.Status = status;
            return Task.CompletedTask;
        }

        public Task<BillPreviewResult> PreviewBillsAsync(BillPreviewRequest request)
        {
            return Task.FromResult(new BillPreviewResult
            {
                Rows = new List<BillPreviewRowDto>(),
                MatchedCount = 0,
                FallbackCount = 0,
                FailedCount = 0,
                TotalAmount = 0m
            });
        }

        public Task<ReportLogDto> ExportChargeItemsAsync(ChargeItemExportRequest request)
        {
            return Task.FromResult(new ReportLogDto
            {
                Id = 1,
                ReportType = "charge_item",
                Period = DateTime.Now.ToString("yyyyMMddHHmmss"),
                Format = request == null ? ExportFormat.Pdf : request.Format,
                FilePath = "（演示模式不生成文件）"
            });
        }

        private static ChargeStandardDto CloneStandard(ChargeStandardDto source)
        {
            return new ChargeStandardDto
            {
                Id = source.Id,
                Name = source.Name,
                Category = source.Category,
                Remark = source.Remark,
                Status = source.Status,
                ItemCount = source.ItemCount,
                Specs = source.Specs == null ? new List<ChargeStandardSpecDto>() : source.Specs.ToList(),
                Variables = source.Variables == null ? new List<ChargeVariableDto>() : source.Variables.ToList()
            };
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

        public Task<DictItemDto> CreateDictItemAsync(string typeCode, DictItemRequest request)
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

        public Task DeleteCycleAsync(int id)
        {
            BillingCycleDto cycle = _cycles.FirstOrDefault(x => x.Id == id);
            if (cycle != null) { _cycles.Remove(cycle); }
            return Task.FromResult(0);
        }

        public Task<BillGenerateLogDto> GenerateBillsAsync(BillGenerateRequest request)
        {
            int id = _batches.Count + 1;
            int fail = 0;
            var failures = new List<BillFailureDto>();
            // CHG-v1.1.0-18：同对象同周期同项目允许重复出账（演示实现同步取消判重拦截）
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

        /// <summary>CHG-v1.1.0-10：演示实现——按房产/车位类型返回候选（无服务端过滤）。</summary>
        public Task<BillObjectQueryResult> QueryBillObjectsAsync(BillObjectQueryRequest request)
        {
            bool parking = request != null && string.Equals(request.Kind, "parking", StringComparison.OrdinalIgnoreCase);
            var result = new BillObjectQueryResult { Items = new List<BillObjectCandidateDto>(), HiddenCount = 0 };
            string kw = request == null ? null : request.Keyword;
            foreach (BillListItemDto b in _bills)
            {
                if (parking) { continue; }
                if (!string.IsNullOrWhiteSpace(kw) &&
                    (b.PropertyNo ?? string.Empty).IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                result.Items.Add(new BillObjectCandidateDto
                {
                    Id = b.PropertyId ?? 0,
                    Kind = "property",
                    No = b.PropertyNo,
                    SubText = "演示数据",
                    OwnerName = b.OwnerName
                });
            }
            return Task.FromResult(result);
        }

        /// <summary>CHG-v1.1.0-10：演示实现——批次编辑回填返回空选择。</summary>
        public Task<BillObjectSelectionDto> GetBatchBillObjectsAsync(int batchId)
        {
            return Task.FromResult(new BillObjectSelectionDto
            {
                PropertyIds = new List<int>(),
                ParkingIds = new List<int>()
            });
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
                new BillFailureDto { PropertyId = 1, No = "B1-01", Reason = "房产不存在有效「房产-业主」关系，请先在业主-房产关系中绑定业主后再出账" },
                new BillFailureDto { PropertyId = 2, No = "B1-02", Reason = "房产缺少建筑面积，无法按建筑面积计费" }
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

        // CHG-v1.1.2-03：移出台账（仅影响台账可见性，演示客户端下等价于从台账列表移除）
        public Task<int> DismissArrearsAsync(ArrearDismissRequest request)
        {
            List<int> ids = request == null || request.BillIds == null ? new List<int>() : request.BillIds;
            int affected = _arrears.RemoveAll(x => ids.Contains(x.BillId));
            return Task.FromResult(affected);
        }

        public Task<List<ArrearDismissDto>> QueryDismissedArrearsAsync()
        {
            return Task.FromResult(new List<ArrearDismissDto>());
        }

        public Task<int> RestoreArrearsAsync(ArrearDismissRequest request)
        {
            return Task.FromResult(0);
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

        /// <summary>CHG-v1.1.0-12：演示实现——统一收款（逐张核销并共享流水号）。</summary>
        public Task<PaymentBatchResultDto> CreateBatchPaymentAsync(PaymentBatchCreateRequest request)
        {
            string batchNo = "PAY-" + DateTime.Now.ToString("yyyyMMdd") + "-0001";
            var payments = new List<PaymentDto>();
            foreach (PaymentBatchItemRequest item in request.Items ?? new List<PaymentBatchItemRequest>())
            {
                BillListItemDto bill = _bills.FirstOrDefault(x => x.Id == item.BillId);
                if (bill == null) { continue; }
                bill.PaidAmount = Math.Min(bill.Amount, bill.PaidAmount + item.Amount);
                bill.Status = bill.PaidAmount >= bill.Amount ? BillStatus.Paid : BillStatus.Partial;
                payments.Add(new PaymentDto
                {
                    Id = 100 + bill.Id,
                    BillId = bill.Id,
                    BatchNo = batchNo,
                    Amount = item.Amount,
                    PayMethod = request.PayMethod,
                    PaidAt = DateTime.Now,
                    Status = PaymentStatus.Normal
                });
            }
            return Task.FromResult(new PaymentBatchResultDto
            {
                BatchNo = batchNo,
                Payments = payments,
                Count = payments.Count,
                TotalAmount = payments.Sum(x => x.Amount)
            });
        }

        // CHG-v1.1.0-15：收据号前后端下线（原收据查询/打印实现已移除）

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

        /// <summary>CHG-v1.1.0-13：演示实现——批量退款/减免/调整（每张账单一条记录）。</summary>
        public Task<RefundBatchResultDto> CreateRefundBatchAsync(RefundAdjustmentRequest request)
        {
            var items = new List<RefundAdjustmentDto>();
            foreach (int billId in request.BillIds ?? new List<int>())
            {
                items.Add(new RefundAdjustmentDto
                {
                    Id = _refunds.Count + 1,
                    BillId = billId,
                    RefundType = request.RefundType,
                    Amount = request.Amount,
                    Reason = request.Reason,
                    RefNo = "REF-" + DateTime.Now.ToString("yyMMdd") + "-" + (_refunds.Count + 1),
                    CreatedAt = DateTime.Now
                });
            }
            foreach (var dto in items) { _refunds.Insert(0, dto); }
            return Task.FromResult(new RefundBatchResultDto
            {
                Items = items,
                Count = items.Count,
                TotalAmount = request.Amount * items.Count
            });
        }

        public Task<List<ExpenseCategoryDto>> GetExpenseCategoriesAsync()
        {
            return Task.FromResult(_categories.ToList());
        }

        public Task<ExpenseCategoryDto> CreateExpenseCategoryAsync(ExpenseCategoryRequest request)
        {
            var dto = new ExpenseCategoryDto
            {
                Id = (_categories.Count == 0 ? 0 : _categories.Max(x => x.Id)) + 1,
                Name = request == null ? string.Empty : request.Name,
                CategoryType = request == null ? null : request.CategoryType,
                Status = 0
            };
            _categories.Add(dto);
            return Task.FromResult(dto);
        }

        public Task DeleteExpenseCategoryAsync(int id)
        {
            ExpenseCategoryDto item = _categories.FirstOrDefault(x => x.Id == id);
            if (item != null) { item.Status = 1; }
            return Task.CompletedTask;
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

        public Task<RecordBatchDeleteResultDto> BatchDeleteExpensesAsync(RecordBatchDeleteRequest request)
        {
            int deleted = 0;
            if (request != null && request.Ids != null)
            {
                foreach (ExpenseDto e in _expenses.Where(x => request.Ids.Contains(x.Id) && x.Status == 0).ToList())
                {
                    e.Status = 1;
                    deleted++;
                }
            }
            return Task.FromResult(new RecordBatchDeleteResultDto { Deleted = deleted });
        }

        public Task<PageResult<LedgerEntryDto>> GetLedgerAsync(LedgerQueryRequest request)
        {
            return Page(_ledger);
        }

        /// <summary>CHG-v1.1.2-05：流水导出（演示客户端只返回一条导出记录，不落真实文件）。</summary>
        public Task<ReportLogDto> ExportLedgerAsync(LedgerExportRequest request)
        {
            return Task.FromResult(new ReportLogDto
            {
                Id = 1,
                ReportType = "ledger",
                Period = DateTime.Now.ToString("yyyyMMddHHmmss"),
                Format = request == null ? ExportFormat.Excel : request.Format,
                FilePath = "C:\\ProgramData\\PropertyManagement\\exports\\ledger-demo.xlsx",
                CreatedAt = DateTime.Now
            });
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

        /// <summary>CHG-v1.1.0-14：演示实现——导出收据打印模板。</summary>
        public Task<ReportLogDto> ExportReceiptTemplateAsync(ReceiptTemplateRequest request)
        {
            return Task.FromResult(new ReportLogDto
            {
                Id = 2,
                ReportType = "receipt",
                Period = DateTime.Now.ToString("yyyy-MM-dd"),
                Format = ExportFormat.Pdf
            });
        }

        /// <summary>CHG-v1.1.2-41：演示实现——导出退款/减免/调整单据 PDF。</summary>
        public Task<ReportLogDto> ExportRefundRecordAsync(RefundRecordExportRequest request)
        {
            return Task.FromResult(new ReportLogDto
            {
                Id = 3,
                ReportType = "refund",
                Period = DateTime.Now.ToString("yyyy-MM-dd"),
                Format = ExportFormat.Pdf
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
                RemindChannel = remind,
                // CHG-v1.1.2-54：账期与账单真实账期同源（Mock 按到期日所在自然月给出示例账期）
                CycleStart = new DateTime(due.Year, due.Month, 1).ToString("yyyy-MM-dd"),
                CycleEnd = new DateTime(due.Year, due.Month, DateTime.DaysInMonth(due.Year, due.Month)).ToString("yyyy-MM-dd")
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

        // ==================== M5 基础信息与导入（演示夹具） ====================
        public Task<List<CommunityDto>> GetCommunitiesAsync(string keyword = null) =>
            Task.FromResult(new List<CommunityDto> { new CommunityDto { Id = 1, Name = "安怡小区", Address = "示例大道 1 号" } });

        public Task<List<BuildingDto>> GetBuildingsAsync(int? communityId = null) =>
            Task.FromResult(new List<BuildingDto> { new BuildingDto { Id = 1, CommunityId = 1, CommunityName = "安怡小区", BuildingNo = "1号楼", Floors = 6 } });

        public Task<List<UnitDto>> GetUnitsAsync(int? buildingId = null) =>
            Task.FromResult(new List<UnitDto> { new UnitDto { Id = 1, BuildingId = 1, BuildingNo = "1号楼", UnitNo = "1单元" } });

        public Task<BuildingDto> CreateBuildingAsync(BuildingRequest request) =>
            Task.FromResult(new BuildingDto { Id = 99, CommunityId = request.CommunityId, BuildingNo = request.BuildingNo, Floors = request.Floors });

        public Task<UnitDto> CreateUnitAsync(UnitRequest request) =>
            Task.FromResult(new UnitDto { Id = 99, BuildingId = request.BuildingId, UnitNo = request.UnitNo });

        public Task DeleteBuildingAsync(int id) => Task.CompletedTask;

        public Task DeleteUnitAsync(int id) => Task.CompletedTask;

        public Task<PageResult<PropertyDto>> QueryPropertiesAsync(BaseInfoQueryRequest request) =>
            Task.FromResult(new PageResult<PropertyDto>
            {
                PageIndex = request.PageIndex, PageSize = request.PageSize, Total = 1,
                Items = new List<PropertyDto> { new PropertyDto { Id = 1, UnitId = 1, UnitPath = "1号楼-1单元-101", RoomNo = "101", Area = 88.5m, Usage = PropertyUsage.Residential, Status = PropertyStatus.Occupied, OwnerName = "张伟", OwnerPhone = "13800000001", CurrentArrear = 0 } }
            });

        public Task<PropertyDto> CreatePropertyAsync(PropertyRequest request) =>
            Task.FromResult(new PropertyDto { Id = 99, BuildingId = request.BuildingId, UnitId = request.UnitId, RoomNo = request.RoomNo, Area = request.Area, Usage = request.Usage, Status = request.Status });

        public Task<PropertyDto> UpdatePropertyAsync(int id, PropertyRequest request) =>
            Task.FromResult(new PropertyDto { Id = id, BuildingId = request.BuildingId, UnitId = request.UnitId, RoomNo = request.RoomNo, Area = request.Area, Usage = request.Usage, Status = request.Status });

        public Task DeletePropertyAsync(int id) => Task.CompletedTask;

        public Task<PageResult<OwnerDto>> QueryOwnersAsync(BaseInfoQueryRequest request) =>
            Task.FromResult(new PageResult<OwnerDto>
            {
                PageIndex = request.PageIndex, PageSize = request.PageSize, Total = 1,
                Items = new List<OwnerDto> { new OwnerDto { Id = 1, Name = "张伟", IdCardType = OwnerIdCardType.IdCard, IdCard = "110101198501011234", Phone = "13800000001", Status = OwnerStatus.Living, StatusText = "在住", PropertyCount = 1 } }
            });

        public Task<OwnerDto> GetOwnerAsync(int id) =>
            Task.FromResult(new OwnerDto { Id = id, Name = "张伟", IdCardType = OwnerIdCardType.IdCard, IdCard = "110101198501011234", Phone = "13800000001", Status = OwnerStatus.Living, StatusText = "在住", PropertyCount = 1 });

        public Task<List<BaseChangeLogDto>> GetOwnerChangeLogsAsync(int id) =>
            Task.FromResult(new List<BaseChangeLogDto>());

        public Task<List<OwnerPropertyRelationDto>> GetOwnerRelationsAsync(int id) =>
            Task.FromResult(new List<OwnerPropertyRelationDto>());

        public Task<OwnerDto> CreateOwnerAsync(OwnerRequest request) =>
            Task.FromResult(new OwnerDto { Id = 99, Name = request.Name, Phone = request.Phone, Status = OwnerStatus.Living });

        public Task<OwnerDto> UpdateOwnerAsync(int id, OwnerRequest request) =>
            Task.FromResult(new OwnerDto { Id = id, Name = request.Name, Phone = request.Phone });

        public Task DeleteOwnerAsync(int id) => Task.CompletedTask;

        public Task<PageResult<OwnerPropertyRelationDto>> QueryRelationsAsync(BaseInfoQueryRequest request) =>
            Task.FromResult(new PageResult<OwnerPropertyRelationDto> { PageIndex = request.PageIndex, PageSize = request.PageSize, Total = 0, Items = new List<OwnerPropertyRelationDto>() });

        public Task<OwnerPropertyRelationDto> CreateRelationAsync(OwnerPropertyRelationRequest request) =>
            Task.FromResult(new OwnerPropertyRelationDto { Id = 99, PropertyId = request.PropertyId, OwnerId = request.OwnerId, RelType = request.RelType, Share = request.Share, EffectiveAt = request.EffectiveAt });

        public Task<OwnerPropertyRelationDto> UpdateRelationAsync(int id, OwnerPropertyRelationRequest request) =>
            Task.FromResult(new OwnerPropertyRelationDto { Id = id, PropertyId = request.PropertyId, OwnerId = request.OwnerId, RelType = request.RelType, Share = request.Share, EffectiveAt = request.EffectiveAt });

        public Task ReleaseRelationAsync(int id, string reason) => Task.CompletedTask;

        public Task<PageResult<ParkingSpaceDto>> QueryParkingsAsync(BaseInfoQueryRequest request) =>
            Task.FromResult(new PageResult<ParkingSpaceDto> { PageIndex = request.PageIndex, PageSize = request.PageSize, Total = 0, Items = new List<ParkingSpaceDto>() });

        public Task<ParkingSpaceDto> CreateParkingAsync(ParkingSpaceRequest request) =>
            Task.FromResult(new ParkingSpaceDto { Id = 99, SpaceNo = request.SpaceNo, SpaceType = request.SpaceType, Status = request.Status });

        public Task<ParkingSpaceDto> UpdateParkingAsync(int id, ParkingSpaceRequest request) =>
            Task.FromResult(new ParkingSpaceDto { Id = id, SpaceNo = request.SpaceNo, SpaceType = request.SpaceType, Status = request.Status });

        public Task DeleteParkingAsync(int id) => Task.CompletedTask;

        public Task<byte[]> DownloadBaseInfoTemplateAsync(ImportModule module) =>
            Task.FromResult(new byte[0]);

        public Task<ImportResultDto> ImportAsync(ImportRequest request) =>
            Task.FromResult(new ImportResultDto
            {
                Batch = new ImportLogDto { Id = 1, Module = request.Module, FileName = request.FileName, Total = 0, Success = 0, Fail = 0, Status = ImportStatus.Success, StatusText = "成功" },
                Errors = new List<ImportErrorItemDto>()
            });

        public Task<List<ImportLogDto>> GetImportLogsAsync() =>
            Task.FromResult(new List<ImportLogDto>());

        public Task<RecordBatchDeleteResultDto> BatchDeleteImportLogsAsync(RecordBatchDeleteRequest request) =>
            Task.FromResult(new RecordBatchDeleteResultDto
            {
                Deleted = request != null && request.Ids != null ? request.Ids.Count : 0
            });

        public Task<byte[]> DownloadImportErrorsAsync(int id) =>
            Task.FromResult(new byte[0]);

        public Task<ExportLogDto> ExportAsync(BaseInfoExportRequest request) =>
            Task.FromResult(new ExportLogDto { Id = 1, Module = request.ExportType, Format = ExportFormat.Excel, FilePath = "demo.xlsx", CreatedAt = DateTime.Now });

        public Task DownloadExportFileAsync(int id, string savePath) =>
            Task.FromResult(true);

        public Task<string> GetParamAsync(string key) =>
            Task.FromResult<string>(null);

        public Task SetParamAsync(string key, string value) =>
            Task.CompletedTask;

        // ==================== M6 人员组织（演示夹具） ====================
        private readonly List<EmployeeDto> _employees = new List<EmployeeDto>
        {
            Emp(8, 1, 1, "张强", "138****2233", new DateTime(2021, 3, 15), EmployeeStatus.Active, "安保部", "保安班长"),
            Emp(12, 1, 2, "李伟", "137****9080", new DateTime(2022, 6, 1), EmployeeStatus.Active, "安保部", "保安"),
            Emp(21, 2, 3, "王平安", "135****7712", new DateTime(2020, 11, 20), EmployeeStatus.Active, "工程部", "维修技工"),
            Emp(25, 3, 4, "赵敏", "136****5541", new DateTime(2023, 2, 13), EmployeeStatus.Active, "客服部", "客服专员"),
            Emp(31, 3, 5, "刘芳", "133****2276", new DateTime(2019, 8, 5), EmployeeStatus.Active, "客服部", "客服主管"),
            Emp(7, 1, 2, "孙浩", "188****6653", new DateTime(2021, 9, 1), EmployeeStatus.Resigned, "安保部", "保安")
        };

        private static EmployeeDto Emp(int id, int dept, int pos, string name, string phone, DateTime hire, EmployeeStatus status, string deptName, string posName)
        {
            return new EmployeeDto
            {
                Id = id, DeptId = dept, PositionId = pos, Name = name, Phone = phone, HireDate = hire, Status = status,
                EmpNo = "YG-" + id.ToString("000"), DeptName = deptName, PositionName = posName,
                StatusText = status == EmployeeStatus.Active ? "在岗" : (status == EmployeeStatus.OffDuty ? "离岗" : "离职"),
                PhoneMask = phone
            };
        }

        public Task<List<DepartmentDto>> GetDepartmentsAsync(string keyword = null) =>
            Task.FromResult(new List<DepartmentDto> { new DepartmentDto { Id = 1, Name = "安保部" }, new DepartmentDto { Id = 2, Name = "工程部" }, new DepartmentDto { Id = 3, Name = "客服部" } });

        public Task<DepartmentDto> CreateDepartmentAsync(DepartmentRequest request) =>
            Task.FromResult(new DepartmentDto { Id = 10, Name = request.Name, ParentId = request.ParentId });

        public Task<DepartmentDto> UpdateDepartmentAsync(int id, DepartmentRequest request) =>
            Task.FromResult(new DepartmentDto { Id = id, Name = request.Name, ParentId = request.ParentId });

        public Task DeleteDepartmentAsync(int id) => Task.CompletedTask;

        public Task<List<PositionDto>> GetPositionsAsync(int? deptId = null) =>
            Task.FromResult(new List<PositionDto>{ new PositionDto { Id = 1, DeptId = 1, Name = "保安班长" }, new PositionDto { Id = 2, DeptId = 1, Name = "保安" }, new PositionDto { Id = 3, DeptId = 2, Name = "维修技工" }, new PositionDto { Id = 4, DeptId = 3, Name = "客服专员" }, new PositionDto { Id = 5, DeptId = 3, Name = "客服主管" } });

        public Task<PositionDto> CreatePositionAsync(PositionRequest request) =>
            Task.FromResult(new PositionDto { Id = 99, DeptId = request.DeptId, Name = request.Name });

        public Task<PositionDto> UpdatePositionAsync(int id, PositionRequest request) =>
            Task.FromResult(new PositionDto { Id = id, DeptId = request.DeptId, Name = request.Name });

        public Task DeletePositionAsync(int id) => Task.CompletedTask;

        public Task<PageResult<EmployeeDto>> QueryEmployeesAsync(EmployeeQueryRequest request) =>
            Page(_employees.Where(e => !request.Status.HasValue || e.Status == request.Status.Value));

        public Task<EmployeeDto> GetEmployeeAsync(int id) =>
            Task.FromResult(_employees.FirstOrDefault(e => e.Id == id));

        public Task<EmployeeDto> CreateEmployeeAsync(EmployeeRequest request) =>
            Task.FromResult(new EmployeeDto { Id = 99, DeptId = request.DeptId, PositionId = request.PositionId, Name = request.Name, Phone = request.Phone, HireDate = request.HireDate, Status = EmployeeStatus.Active, EmpNo = "YG-099", StatusText = "在岗" });

        public Task<EmployeeDto> UpdateEmployeeAsync(int id, EmployeeRequest request) =>
            Task.FromResult(new EmployeeDto { Id = id, DeptId = request.DeptId, PositionId = request.PositionId, Name = request.Name, Phone = request.Phone, HireDate = request.HireDate, Status = EmployeeStatus.Active, EmpNo = "YG-" + id.ToString("000"), StatusText = "在岗" });

        public Task DeleteEmployeeAsync(int id) => Task.CompletedTask;

        public Task<EmployeeDto> ChangeEmployeeStatusAsync(int id, EmployeeStatusRequest request) =>
            Task.FromResult(new EmployeeDto { Id = id, Status = request.Status, EmpNo = "YG-" + id.ToString("000") });

        public Task<EmployeeDto> ResignEmployeeAsync(int id) =>
            Task.FromResult<EmployeeDto>(null);
        public Task<List<EmployeeStatusLogDto>> GetEmployeeStatusLogsAsync(int id) =>
            Task.FromResult(new List<EmployeeStatusLogDto>());

        public Task<List<ShiftDto>> GetShiftsAsync() =>
            Task.FromResult(new List<ShiftDto> { new ShiftDto { Id = 1, Name = "早班", StartTime = "08:00", EndTime = "16:00" }, new ShiftDto { Id = 2, Name = "中班", StartTime = "16:00", EndTime = "24:00" }, new ShiftDto { Id = 3, Name = "晚班", StartTime = "00:00", EndTime = "08:00" } });

        public Task<ShiftDto> CreateShiftAsync(ShiftRequest request) =>
            Task.FromResult(new ShiftDto { Id = 99, Name = request.Name, StartTime = request.StartTime, EndTime = request.EndTime });

        public Task<ShiftDto> UpdateShiftAsync(int id, ShiftRequest request) =>
            Task.FromResult(new ShiftDto { Id = id, Name = request.Name, StartTime = request.StartTime, EndTime = request.EndTime });

        public Task DeleteShiftAsync(int id) => Task.CompletedTask;

        public Task<List<ScheduleDto>> QuerySchedulesAsync(DateTime from, DateTime to, int? employeeId = null) =>
            Task.FromResult(new List<ScheduleDto>());

        public Task<SchedulePlanDto> GenerateSchedulesAsync(ScheduleGenerateRequest request) =>
            Task.FromResult(new SchedulePlanDto { Schedules = new List<ScheduleDto>(), Conflicts = new List<ScheduleConflictLogDto>() });

        public Task<SchedulePlanDto> PublishSchedulesAsync(SchedulePublishRequest request) =>
            Task.FromResult(new SchedulePlanDto { Schedules = new List<ScheduleDto>(), Conflicts = new List<ScheduleConflictLogDto>() });

        public Task DeleteScheduleAsync(int id) => Task.CompletedTask;

        public Task<int> DeleteSchedulesBatchAsync(ScheduleBatchDeleteRequest request) => Task.FromResult(0);

        public Task<List<ScheduleConflictLogDto>> GetScheduleConflictsAsync(DateTime from, DateTime to) =>
            Task.FromResult(new List<ScheduleConflictLogDto>());

        public Task<List<ScheduleTemplateDto>> GetScheduleTemplatesAsync() =>
            Task.FromResult(new List<ScheduleTemplateDto>());

        public Task<ScheduleTemplateDto> GetScheduleTemplateAsync(int id) =>
            Task.FromResult<ScheduleTemplateDto>(null);

        public Task<ScheduleTemplateDto> SaveScheduleTemplateAsync(ScheduleTemplateSaveRequest request) =>
            Task.FromResult(new ScheduleTemplateDto { Id = 1, Name = request.Name ?? "模板", FromDate = request.FromDate, ToDate = request.ToDate, ItemCount = 0 });

        public Task DeleteScheduleTemplateAsync(int id) => Task.CompletedTask;

        public Task<PageResult<AttendanceDto>> QueryAttendanceAsync(AttendanceQueryRequest request) =>
            Task.FromResult(new PageResult<AttendanceDto> { PageIndex = request.PageIndex, PageSize = request.PageSize, Total = 0, Items = new List<AttendanceDto>() });

        public Task<AttendanceDto> RecordAttendanceAsync(AttendanceRequest request) =>
            Task.FromResult(new AttendanceDto { Id = 1, EmployeeId = request.EmployeeId, WorkDate = request.WorkDate, CheckIn = request.CheckIn, CheckOut = request.CheckOut, Result = AttendanceResult.Recorded });

        public Task<AttendanceSummaryDto> GetAttendanceSummaryAsync(int? year = null, int? month = null, int? deptId = null) =>
            Task.FromResult(new AttendanceSummaryDto { Year = year ?? DateTime.Now.Year, Month = month ?? DateTime.Now.Month, AttendanceRate = 98.2, LateCount = 2, AbsentCount = 1, LeaveCount = 0, PendingReviewCount = 1, TotalCount = 40 });

        public Task<string> ExportAttendanceCsvAsync(int? year = null, int? month = null, int? deptId = null) =>
            Task.FromResult("日期,工号,姓名,班次,上班打卡,下班打卡,状态,审核状态");

        public Task<AttendanceDto> ReviewAttendanceAsync(int id, AttendanceReviewRequest request) =>
            Task.FromResult(new AttendanceDto { Id = id, Result = AttendanceResult.Reviewed, ReviewNote = request.ReviewNote });

        public Task<int> BatchReviewAttendanceAsync(AttendanceBatchReviewRequest request) =>
            Task.FromResult(request?.Ids?.Count ?? 0);

        // ==================== M6 便民电话簿（演示夹具） ====================
        public Task<List<PhoneCategoryDto>> GetPhoneCategoriesAsync() =>
            Task.FromResult(new List<PhoneCategoryDto>
            {
                new PhoneCategoryDto { Id = 1, Name = "物业服务中心", Sort = 1 },
                new PhoneCategoryDto { Id = 2, Name = "工程维修", Sort = 2 },
                new PhoneCategoryDto { Id = 3, Name = "紧急电话", Sort = 3 },
                new PhoneCategoryDto { Id = 4, Name = "政府 / 市政", Sort = 4 }
            });

        public Task<PhoneCategoryDto> CreatePhoneCategoryAsync(PhoneCategoryRequest request) =>
            Task.FromResult(new PhoneCategoryDto { Id = 99, Name = request.Name, Sort = request.Sort });

        public Task DeletePhoneCategoryAsync(int id) => Task.CompletedTask;

        public Task<List<PhoneTypeDto>> GetPhoneTypesAsync() =>
            Task.FromResult(new List<PhoneTypeDto> { new PhoneTypeDto { Id = 1, Name = "紧急", Sort = 0 }, new PhoneTypeDto { Id = 2, Name = "普通", Sort = 1 }, new PhoneTypeDto { Id = 3, Name = "员工通讯录", Sort = 2 } });

        public Task<PhoneTypeDto> CreatePhoneTypeAsync(PhoneTypeRequest request) =>
            Task.FromResult(new PhoneTypeDto { Id = 99, Name = request.Name, Sort = request.Sort });

        public Task<PhoneTypeDto> UpdatePhoneTypeAsync(int id, PhoneTypeRequest request) =>
            Task.FromResult(new PhoneTypeDto { Id = id, Name = request.Name, Sort = request.Sort });

        public Task DeletePhoneTypeAsync(int id) => Task.CompletedTask;

        public Task<PageResult<PhoneEntryDto>> QueryPhoneEntriesAsync(PhoneEntryQueryRequest request) =>
            Task.FromResult(new PageResult<PhoneEntryDto>
            {
                PageIndex = request.PageIndex, PageSize = request.PageSize, Total = 4,
                Items = new List<PhoneEntryDto>
                {
                    new PhoneEntryDto { Id = 1, CategoryId = 1, EntryType = PhoneEntryType.Normal, Name = "物业服务中心", Phone = "0571-88021234", Note = "24 小时值班", IsTop = true, Status = PhoneEntryStatus.Enabled, CategoryName = "物业服务中心", StatusText = "启用" },
                    new PhoneEntryDto { Id = 2, CategoryId = 2, EntryType = PhoneEntryType.Normal, Name = "电梯维保（急修）", Phone = "139****0087", Note = "迅达 · 30 分钟到场", IsTop = true, Status = PhoneEntryStatus.Enabled, CategoryName = "工程维修", StatusText = "启用" },
                    new PhoneEntryDto { Id = 3, CategoryId = 3, EntryType = PhoneEntryType.Emergency, Name = "火警 / 报警", Phone = "119 / 110", Note = "", IsTop = true, Status = PhoneEntryStatus.Enabled, CategoryName = "紧急电话", StatusText = "启用" },
                    new PhoneEntryDto { Id = 4, CategoryId = 2, EntryType = PhoneEntryType.Employee, Name = "水电班组 · 王平安", Phone = "135****7712", IsTop = false, Status = PhoneEntryStatus.Enabled, CategoryName = "工程维修", StatusText = "启用" }
                }
            });

        public Task<PhoneEntryDto> CreatePhoneEntryAsync(PhoneEntryRequest request) =>
            Task.FromResult(new PhoneEntryDto { Id = 99, CategoryId = request.CategoryId, EntryType = request.EntryType, Name = request.Name, Phone = request.Phone, Note = request.Note, IsTop = request.IsTop, Status = PhoneEntryStatus.Enabled });

        public Task<PhoneEntryDto> UpdatePhoneEntryAsync(int id, PhoneEntryRequest request) =>
            Task.FromResult(new PhoneEntryDto { Id = id, CategoryId = request.CategoryId, EntryType = request.EntryType, Name = request.Name, Phone = request.Phone, Note = request.Note, IsTop = request.IsTop, Status = PhoneEntryStatus.Enabled });

        public Task<PhoneEntryDto> SetPhoneEntryStatusAsync(int id, PhoneEntryStatus status) =>
            Task.FromResult(new PhoneEntryDto { Id = id, Status = status });

        public Task<PhoneEntryBatchStatusResultDto> BatchDisablePhoneEntriesAsync(PhoneEntryBatchStatusRequest request) =>
            Task.FromResult(new PhoneEntryBatchStatusResultDto { Disabled = request?.Ids?.Count ?? 0, Skipped = 0 });

        public Task<PhoneEntryDto> SetPhoneEntryTopAsync(int id, bool isTop) =>
            Task.FromResult(new PhoneEntryDto { Id = id, IsTop = isTop });

        public Task<EmployeeSyncResultDto> SyncEmployeePhoneEntriesAsync(int categoryId) =>
            Task.FromResult(new EmployeeSyncResultDto { TotalEmployees = 5, Synced = 5, Skipped = 0 });

        // ==================== M6 纠纷调解（演示夹具） ====================
        public Task<List<DisputeTypeDto>> GetDisputeTypesAsync() =>
            Task.FromResult(new List<DisputeTypeDto> { new DisputeTypeDto { Id = 1, Name = "漏水" }, new DisputeTypeDto { Id = 2, Name = "噪音" }, new DisputeTypeDto { Id = 3, Name = "装修" } });

        public Task<DisputeTypeDto> CreateDisputeTypeAsync(DisputeTypeRequest request) =>
            Task.FromResult(new DisputeTypeDto { Id = 99, Name = request.Name, Status = 0 });

        public Task DeleteDisputeTypeAsync(int id)
        {
            return Task.CompletedTask;
        }

        public Task<PageResult<DisputeCaseDto>> QueryDisputesAsync(DisputeQueryRequest request) =>
            Task.FromResult(new PageResult<DisputeCaseDto>
            {
                PageIndex = request.PageIndex, PageSize = request.PageSize, Total = 3,
                Items = new List<DisputeCaseDto>
                {
                    new DisputeCaseDto { Id = 6, CaseNo = "JF-2608-06", TypeName = "漏水", StatusText = "调解中", OccurTimeText = "08-26", PartySummary = "502 张先生 / 402 李女士", RecordCount = 1 },
                    new DisputeCaseDto { Id = 5, CaseNo = "JF-2608-05", TypeName = "噪音", StatusText = "调解中", OccurTimeText = "08-24", PartySummary = "1201 王先生 / 1101 赵小姐", RecordCount = 2 },
                    new DisputeCaseDto { Id = 3, CaseNo = "JF-2608-03", TypeName = "漏水", StatusText = "已结案", OccurTimeText = "08-18", PartySummary = "902 周先生 / 802 吴先生", RecordCount = 2, CloseTypeText = "调解成功" }
                }
            });

        public Task<DisputeCaseDetailDto> GetDisputeAsync(int id) =>
            Task.FromResult(new DisputeCaseDetailDto
            {
                Case = new DisputeCaseDto { Id = id, CaseNo = "JF-2608-06", TypeName = "漏水", StatusText = "调解中", MediatorName = "刘芳" },
                Parties = new List<DisputePartyDto> { new DisputePartyDto { PartyTypeText = "甲方", Name = "402 李女士" }, new DisputePartyDto { PartyTypeText = "乙方", Name = "502 张先生" } },
                Records = new List<DisputeRecordDto>()
            });

        public Task<DisputeCaseDto> CreateDisputeAsync(DisputeCaseCreateRequest request) =>
            Task.FromResult(new DisputeCaseDto { Id = 99, CaseNo = "JF-2608-99", StatusText = "待调解" });

        public Task<DisputeCaseDto> UpdateDisputeAsync(int id, DisputeCaseUpdateRequest request) =>
            Task.FromResult(new DisputeCaseDto { Id = id });

        public Task<DisputeRecordDto> AddDisputeRecordAsync(int caseId, DisputeRecordRequest request) =>
            Task.FromResult(new DisputeRecordDto { Id = 1, CaseId = caseId, Content = request.Content, Recorder = request.Recorder });

        public Task DeleteDisputeAsync(int id)
        {
            return Task.CompletedTask;
        }

        public Task<DisputeCaseDto> UpdateDisputeStatusAsync(int id, DisputeCaseStatusRequest request) =>
            Task.FromResult(new DisputeCaseDto { Id = id, Status = request.Status });

        public Task<DisputeCaseDto> CloseDisputeAsync(int id, DisputeCloseRequest request) =>
            Task.FromResult(new DisputeCaseDto { Id = id, Status = DisputeCaseStatus.Closed, CloseType = request.CloseType, CloseTypeText = "调解成功" });

        public Task<DisputeRecordDto> SupplementDisputeCaseAsync(int caseId, DisputeSupplementRequest request) =>
            Task.FromResult(new DisputeRecordDto { Id = 0, CaseId = caseId, RecordTime = DateTime.Now, Content = request != null ? request.Content : null, IsSupplement = true });

        public Task<List<DisputeMediatorDto>> GetMediatorRecommendationsAsync(int? typeId = null, int? propertyId = null) =>
            Task.FromResult(new List<DisputeMediatorDto>());

        public Task<DisputeStatisticsDto> GetDisputeStatisticsAsync() =>
            Task.FromResult(new DisputeStatisticsDto { Total = 23, Registered = 2, Handling = 3, Closed = 3 });

        public Task<ReportLogDto> ExportDisputeCloseReportAsync(int caseId, ExportFormat format) =>
            Task.FromResult(new ReportLogDto
            {
                Id = 1,
                ReportType = "dispute_close",
                Period = "JF-DEMO-" + caseId,
                Format = format,
                FilePath = format == ExportFormat.Excel ? "dispute_close_demo.xlsx" : "dispute_close_demo.pdf",
                CreatedAt = DateTime.Now
            });

        public Task DownloadReportFileAsync(int logId, string savePath) =>
            Task.CompletedTask;

        public Task<List<DisputeAttachmentDto>> GetDisputeAttachmentsAsync(int caseId) =>
            Task.FromResult(new List<DisputeAttachmentDto>());

        public Task<DisputeAttachmentDto> UploadDisputeAttachmentAsync(int caseId, string filePath) =>
            Task.FromResult(new DisputeAttachmentDto
            {
                Id = 1,
                CaseId = caseId,
                FileName = System.IO.Path.GetFileName(filePath),
                SizeBytes = 1024,
                SizeText = "1.0 KB",
                UploadedBy = "admin",
                UploadedAt = DateTime.Now,
                UploadedAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            });

        public Task DownloadDisputeAttachmentAsync(int caseId, int attachmentId, string savePath) =>
            Task.CompletedTask;

        public Task DeleteDisputeAttachmentAsync(int caseId, int attachmentId) =>
            Task.CompletedTask;

        // ==================== M6 应急处置（演示夹具） ====================
        private readonly List<EmergencySceneDto> _scenes = new List<EmergencySceneDto>
        {
            new EmergencySceneDto { Id = 1, Name = "火灾", Category = "消防", IconKey = "Icon.Flame", Status = 0, StepCount = 6, StatusText = "启用" },
            new EmergencySceneDto { Id = 2, Name = "电梯困人", Category = "特种设备", IconKey = "Icon.UserRound", Status = 0, StepCount = 5, StatusText = "启用" },
            new EmergencySceneDto { Id = 3, Name = "水浸 / 爆管", Category = "给排水", IconKey = "Icon.Droplet", Status = 0, StepCount = 5, StatusText = "启用" }
        };

        public Task<List<EmergencySceneDto>> GetEmergencyScenesAsync(string keyword = null) =>
            Task.FromResult(_scenes.ToList());

        public Task<EmergencySceneDto> CreateEmergencySceneAsync(EmergencySceneRequest request) =>
            Task.FromResult(new EmergencySceneDto { Id = 99, Name = request.Name, Category = request.Category, IconKey = request.IconKey, Status = 0, StatusText = "启用" });

        public Task<EmergencySceneDto> UpdateEmergencySceneAsync(int id, EmergencySceneRequest request) =>
            Task.FromResult(new EmergencySceneDto { Id = id, Name = request.Name, Category = request.Category, IconKey = request.IconKey, Status = 0, StatusText = "启用" });

        public Task DeleteEmergencySceneAsync(int id) => Task.CompletedTask;

        public Task<List<EmergencyStepDto>> GetEmergencyStepsAsync(int sceneId) =>
            Task.FromResult(new List<EmergencyStepDto>
            {
                new EmergencyStepDto { Id = 1, SceneId = sceneId, StepNo = 1, Content = "确认火情位置与火势", Role = "值班保安", TimeLimit = "立即", Action = "一键拨打 119", StatusText = "启用" }
            });

        public Task<EmergencyStepDto> CreateEmergencyStepAsync(int sceneId, EmergencyStepRequest request) =>
            Task.FromResult(new EmergencyStepDto { Id = 99, SceneId = sceneId, StepNo = request.StepNo, Content = request.Content, Role = request.Role, TimeLimit = request.TimeLimit, Action = request.Action, StatusText = "启用" });

        public Task<EmergencyStepDto> UpdateEmergencyStepAsync(int id, EmergencyStepRequest request) =>
            Task.FromResult(new EmergencyStepDto { Id = id, SceneId = request.SceneId, StepNo = request.StepNo, Content = request.Content, Role = request.Role, TimeLimit = request.TimeLimit, Action = request.Action, StatusText = "启用" });

        public Task DeleteEmergencyStepAsync(int id) => Task.CompletedTask;

        public Task<PageResult<EmergencyEventDto>> QueryEmergencyEventsAsync(EmergencyEventQueryRequest request) =>
            Task.FromResult(new PageResult<EmergencyEventDto>
            {
                PageIndex = request.PageIndex, PageSize = request.PageSize, Total = 2,
                Items = new List<EmergencyEventDto>
                {
                    new EmergencyEventDto { Id = 12, EventNo = "EM-2608-12", SceneName = "火灾", Location = "1栋2单元 3F 楼梯间", LevelText = "Ⅱ 级", EventTimeText = "08-28 14:26", MainPerson = "张强", ElapsedText = "已 38 分钟", StatusText = "处置中" },
                    new EmergencyEventDto { Id = 11, EventNo = "EM-2608-11", SceneName = "水浸", Location = "2栋地下车库 B1", LevelText = "Ⅲ 级", EventTimeText = "08-27 09:12", MainPerson = "王平安", ElapsedText = "已结案", StatusText = "已结案" }
                }
            });

        public Task<EmergencyEventDetailDto> GetEmergencyEventAsync(int id) =>
            Task.FromResult(new EmergencyEventDetailDto
            {
                Event = new EmergencyEventDto { Id = id, EventNo = "EM-2608-12", SceneName = "火灾", StatusText = "处置中", LevelText = "Ⅱ 级" },
                Steps = new List<EmergencyStepDto>(), Assignments = new List<EmergencyAssignDto>(), Records = new List<EmergencyRecordDto>()
            });

        public Task<EmergencyEventDetailDto> CreateEmergencyEventAsync(EmergencyEventCreateRequest request) =>
            Task.FromResult(new EmergencyEventDetailDto { Event = new EmergencyEventDto { Id = 99, EventNo = "EM-2608-99", StatusText = "已发起" } });

        public Task<EmergencyEventDetailDto> AssignEmergencyAsync(int id, EmergencyAssignRequest request) =>
            Task.FromResult(new EmergencyEventDetailDto { Event = new EmergencyEventDto { Id = id } });

        public Task<EmergencyRecordDto> AddEmergencyRecordAsync(int id, EmergencyRecordRequest request) =>
            Task.FromResult(new EmergencyRecordDto { Id = 1, EventId = id, Content = request.Content, Result = request.Result, Recorder = request.Recorder });

        public Task<EmergencyEventDetailDto> CloseEmergencyAsync(int id, EmergencyCloseRequest request) =>
            Task.FromResult(new EmergencyEventDetailDto { Event = new EmergencyEventDto { Id = id, Status = EmergencyEventStatus.Closed, StatusText = "已结案" } });

        public Task<List<EmergencyMatchDto>> GetEmergencyMatchesAsync(int sceneId) =>
            Task.FromResult(new List<EmergencyMatchDto>());

        public Task<EmergencySceneDto> SetEmergencySceneStatusAsync(int id, EmergencySceneStatusRequest request) =>
            Task.FromResult<EmergencySceneDto>(null);

        public Task ReorderEmergencyStepsAsync(int sceneId, List<int> orderedIds) =>
            Task.CompletedTask;

        public Task<EmergencyEventStatsDto> GetEmergencyEventStatsAsync() =>
            Task.FromResult(new EmergencyEventStatsDto());

        public Task CancelEmergencyEventAsync(int id) =>
            Task.CompletedTask;

        public Task<PageResult<EmergencyReviewDto>> QueryEmergencyReviewsAsync(PageRequest request) =>
            Task.FromResult(new PageResult<EmergencyReviewDto> { Items = new List<EmergencyReviewDto>() });

        public Task<EmergencyReviewDto> GetEmergencyReviewAsync(int eventId) =>
            Task.FromResult<EmergencyReviewDto>(null);

        public Task<EmergencyReviewDto> ReviewEmergencyAsync(int id, EmergencyReviewRequest request) =>
            Task.FromResult(new EmergencyReviewDto { Id = 1, EventId = id, Cause = request.Cause, Measure = request.Measure, CreatedAt = DateTime.Now });

        // ==================== M6 设备台账（演示夹具） ====================
        public Task<List<DeviceTypeDto>> GetDeviceTypesAsync() =>
            Task.FromResult(new List<DeviceTypeDto>
            {
                new DeviceTypeDto { Id = 1, Name = "电梯", MaintenanceCycle = 15 },
                new DeviceTypeDto { Id = 2, Name = "消防", MaintenanceCycle = 30 },
                new DeviceTypeDto { Id = 3, Name = "给排水", MaintenanceCycle = 30 }
            });

        public Task<DeviceTypeDto> CreateDeviceTypeAsync(DeviceTypeRequest request) =>
            Task.FromResult(new DeviceTypeDto { Id = 99, Name = request.Name, MaintenanceCycle = request.MaintenanceCycle });

        public Task DeleteDeviceTypeAsync(int id) => Task.CompletedTask;

        public Task<List<MaintainTypeDto>> GetMaintainTypesAsync(bool includeDisabled = false) =>
            Task.FromResult(new List<MaintainTypeDto>
            {
                new MaintainTypeDto { Id = 1, Name = "保养", Kind = 0, IsSystem = true },
                new MaintainTypeDto { Id = 2, Name = "年检", Kind = 1, IsSystem = true }
            });

        public Task<PageResult<DeviceDto>> QueryDevicesAsync(DeviceQueryRequest request) =>
            Task.FromResult(new PageResult<DeviceDto>
            {
                PageIndex = request.PageIndex, PageSize = request.PageSize, Total = 2,
                Items = new List<DeviceDto>
                {
                    new DeviceDto { Id = 87, DeviceNo = "EQP-0087", Name = "3 单元客梯", TypeName = "电梯", Location = "3栋3单元", BrandModel = "迅达 / S3300", Status = DeviceStatus.InUse, StatusText = "在用", NextMaintenanceText = "09-15" },
                    new DeviceDto { Id = 102, DeviceNo = "EQP-0102", Name = "消防泵组", TypeName = "消防", Location = "泵房 B1", BrandModel = "正压 / XBD6", Status = DeviceStatus.InUse, StatusText = "在用", NextMaintenanceText = "09-08" }
                }
            });

        public Task<DeviceDto> GetDeviceAsync(int id) =>
            Task.FromResult(new DeviceDto { Id = id, DeviceNo = "EQP-" + id.ToString("0000"), Name = "设备", StatusText = "在用" });

        public Task<DeviceDto> CreateDeviceAsync(DeviceRequest request) =>
            Task.FromResult(new DeviceDto { Id = 99, DeviceNo = "EQP-0099", Name = request.Name, TypeName = "类型", Status = DeviceStatus.InUse, StatusText = "在用" });

        public Task<DeviceDto> UpdateDeviceAsync(int id, DeviceRequest request) =>
            Task.FromResult(new DeviceDto { Id = id, Name = request.Name, Status = DeviceStatus.InUse, StatusText = "在用" });

        public Task DeleteDeviceAsync(int id) => Task.CompletedTask;

        public Task<DeviceDto> ChangeDeviceStatusAsync(int id, DeviceStatusRequest request) =>
            Task.FromResult(new DeviceDto { Id = id, Status = request.Status, StatusText = "维修中" });

        public Task<List<DeviceStatusLogDto>> GetDeviceStatusLogsAsync(int deviceId) =>
            Task.FromResult(new List<DeviceStatusLogDto>
            {
                new DeviceStatusLogDto { Id = 2, DeviceId = deviceId, OldStatus = (DeviceStatus)(-1), NewStatus = DeviceStatus.InUse, Reason = "登记", ChangedAt = DateTime.Today.AddDays(-30) },
                new DeviceStatusLogDto { Id = 1, DeviceId = deviceId, OldStatus = DeviceStatus.InUse, NewStatus = DeviceStatus.Disabled, Reason = "年度停用检修", ChangedAt = DateTime.Today.AddDays(-1) }
            });

        public Task<List<MaintenanceRecordDto>> GetMaintenanceAsync(int deviceId) =>
            Task.FromResult(new List<MaintenanceRecordDto>());

        public Task<MaintenanceRecordDto> AddMaintenanceAsync(int deviceId, MaintenanceRecordRequest request) =>
            Task.FromResult(new MaintenanceRecordDto { Id = 1, DeviceId = deviceId, MDate = request.MDate, Content = request.Content, Result = request.Result });

        public Task<List<InspectionRecordDto>> GetInspectionAsync(int deviceId) =>
            Task.FromResult(new List<InspectionRecordDto>());

        public Task<InspectionRecordDto> AddInspectionAsync(int deviceId, InspectionRecordRequest request) =>
            Task.FromResult(new InspectionRecordDto { Id = 1, DeviceId = deviceId, IDate = request.IDate, Result = request.Result });

        public Task<List<FaultRecordDto>> GetFaultsAsync(int deviceId) =>
            Task.FromResult(new List<FaultRecordDto>());

        public Task<FaultRecordDto> AddFaultAsync(int deviceId, FaultRecordRequest request) =>
            Task.FromResult(new FaultRecordDto { Id = 1, DeviceId = deviceId, Symptom = request.Symptom, Cause = request.Cause, Handle = request.Handle, FTime = request.FTime });

        public Task<List<VendorDto>> GetVendorsAsync() =>
            Task.FromResult(new List<VendorDto> { new VendorDto { Id = 1, Name = "安泰消防", Contact = "李工", Phone = "13800001234" } });

        public Task<VendorDto> CreateVendorAsync(VendorRequest request) =>
            Task.FromResult(new VendorDto { Id = 99, Name = request.Name, Contact = request.Contact, Phone = request.Phone });

        public Task DeleteVendorAsync(int id) => Task.CompletedTask;

        public Task<List<EquipmentReminderDto>> GetEquipmentRemindersAsync(int days = 30, string type = null, int status = -1) =>
            Task.FromResult(new List<EquipmentReminderDto>
            {
                new EquipmentReminderDto { ReminderId = 1, Type = "maintenance", DeviceId = 102, DeviceName = "消防泵组", DueAt = DateTime.Today.AddDays(11), Status = ReminderStatus.Pending },
                new EquipmentReminderDto { ReminderId = 2, Type = "maintenance", DeviceId = 87, DeviceName = "3 单元客梯", DueAt = DateTime.Today.AddDays(18), Status = ReminderStatus.Pending }
            });

        public Task<ReminderSummaryDto> GetEquipmentReminderSummaryAsync() =>
            Task.FromResult(new ReminderSummaryDto { Within30 = 2, Within7 = 1, MonthHandled = 3, OverdueUnhandled = 1 });

        public Task<EquipmentReminderDto> HandleEquipmentReminderAsync(int reminderId) =>
            Task.FromResult(new EquipmentReminderDto { ReminderId = reminderId, Type = "maintenance", DeviceName = "消防泵组", DueAt = DateTime.Today.AddDays(11), Status = ReminderStatus.Processed });

        public Task<EquipmentReminderDto> SaveEquipmentReminderTeamAsync(int reminderId, ReminderTeamRequest request) =>
            Task.FromResult(new EquipmentReminderDto
            {
                ReminderId = reminderId, Type = "maintenance", DeviceName = "消防泵组",
                DueAt = DateTime.Today.AddDays(11), Status = ReminderStatus.Pending,
                ResponsibleTeam = request == null || string.IsNullOrWhiteSpace(request.Team) ? "工程部" : request.Team.Trim(),
                ResponsibleTeamOverride = request == null ? string.Empty : request.Team
            });

        public Task<ReminderBatchDeleteResultDto> BatchDeleteEquipmentRemindersAsync(ReminderBatchDeleteRequest request) =>
            Task.FromResult(new ReminderBatchDeleteResultDto { Deleted = request == null || request.Ids == null ? 0 : request.Ids.Count, Blocked = new List<ReminderDeleteBlockedDto>() });

        public Task<List<DeviceCustomRecordDto>> GetDeviceCustomRecordsAsync(int deviceId) =>
            Task.FromResult(new List<DeviceCustomRecordDto>
            {
                new DeviceCustomRecordDto { Id = 1, DeviceId = deviceId, TypeName = "清洁保养", RDate = DateTime.Today.AddDays(-7), Content = "机房清洁与除尘", Result = "合格", Operator = "张工" }
            });

        public Task<DeviceCustomRecordDto> CreateDeviceCustomRecordAsync(int deviceId, DeviceCustomRecordRequest request) =>
            Task.FromResult(new DeviceCustomRecordDto { Id = 99, DeviceId = deviceId, TypeName = request.TypeName, RDate = request.RDate, Content = request.Content, Result = request.Result });

        public Task<DeviceCustomRecordDto> UpdateDeviceCustomRecordAsync(int recordId, DeviceCustomRecordRequest request) =>
            Task.FromResult(new DeviceCustomRecordDto { Id = recordId, DeviceId = request.DeviceId, TypeName = request.TypeName, RDate = request.RDate, Content = request.Content, Result = request.Result });

        public Task DeleteDeviceCustomRecordAsync(int recordId) => Task.CompletedTask;

        public Task<List<string>> GetCustomRecordTypesAsync() =>
            Task.FromResult(new List<string> { "清洁保养", "月度保养", "季度保养", "年检登记" });

        public Task<DeviceSummaryDto> GetDeviceSummaryAsync() =>
            Task.FromResult(new DeviceSummaryDto { Total = 2, InUse = 1, Repairing = 1, FaultCount = 1, DisabledOrScrapped = 0, TypeCount = 2 });

        public Task<EquipmentExportResultDto> ExportDevicesAsync(DeviceQueryRequest request) =>
            Task.FromResult(new EquipmentExportResultDto { Id = 1, FileName = "设备台账.xlsx", Format = "xlsx", Total = 2, ExportedAt = DateTime.Now });

        public Task<DeviceTypeDto> UpdateDeviceTypeAsync(int id, DeviceTypeRequest request) =>
            Task.FromResult(new DeviceTypeDto { Id = id, Name = request != null ? request.Name : null, MaintenanceCycle = request != null ? request.MaintenanceCycle : 0 });

        public Task<VendorDto> UpdateVendorAsync(int id, VendorRequest request) =>
            Task.FromResult(new VendorDto { Id = id, Name = request != null ? request.Name : null, Contact = request != null ? request.Contact : null, Phone = request != null ? request.Phone : null });

        public Task<List<FaultRecordDto>> GetAllFaultsAsync() =>
            Task.FromResult(new List<FaultRecordDto>());

        public Task<FaultRecordDto> HandleFaultAsync(int faultId, FaultHandleRequest request) =>
            Task.FromResult(new FaultRecordDto { Id = faultId, FaultNo = "WX-2608-01", StatusText = "已修复" });

        public Task<EquipmentExportResultDto> GenerateFaultWorkOrderAsync(int faultId) =>
            Task.FromResult(new EquipmentExportResultDto
            {
                Id = 0, Module = "equipment", FileName = "维修工单_WX-2608-01.xlsx",
                Total = 1, Format = "xlsx", ExportedAt = DateTime.Now
            });

        // ==================== M6 系统设置（演示夹具） ====================
        public Task<List<DictTypeDto>> GetSystemDictTypesAsync() =>
            Task.FromResult(new List<DictTypeDto>
            {
                new DictTypeDto { Id = 1, TypeCode = "charge_method", TypeName = "计费方式" },
                new DictTypeDto { Id = 2, TypeCode = "id_card_type", TypeName = "证件类型" },
                new DictTypeDto { Id = 3, TypeCode = "parking_type", TypeName = "车位类型" }
                , new DictTypeDto { Id = 4, TypeCode = "charge_variable", TypeName = "计量变量" }
            });

        public Task<DictTypeDto> CreateSystemDictTypeAsync(DictTypeRequest request) =>
            Task.FromResult(new DictTypeDto { TypeCode = request.TypeCode, TypeName = request.TypeName });

        public Task<List<DictItemDto>> GetSystemDictItemsAsync(string typeCode, bool includeDisabled = false, int? status = null) =>
            Task.FromResult(new List<DictItemDto>
            {
                new DictItemDto { Id = 1, TypeCode = typeCode, ItemCode = "JFFS-01", ItemName = "按面积计费", Remark = "按建筑面积", Sort = 1, Status = DictItemStatus.Enabled },
                new DictItemDto { Id = 2, TypeCode = typeCode, ItemCode = "JFFS-02", ItemName = "按户计费", Remark = "按户", Sort = 2, Status = DictItemStatus.Enabled }
            });

        public Task<DictItemDto> UpdateDictItemAsync(int id, DictItemRequest request) =>
            Task.FromResult(new DictItemDto { Id = id, TypeCode = request.TypeCode, ItemCode = request.ItemCode, ItemName = request.ItemName, Remark = request.Remark, Sort = request.Sort, Status = request.Status });

        public Task<DictItemDto> SetDictItemStatusAsync(int id, DictItemStatus status) =>
            Task.FromResult(new DictItemDto { Id = id, Status = status });

        public Task<DictItemBatchDeleteResultDto> BatchDeleteDictItemsAsync(DictItemBatchDeleteRequest request) =>
            Task.FromResult(new DictItemBatchDeleteResultDto
            {
                Deleted = request != null && request.Ids != null ? request.Ids.Count : 0,
                Blocked = new List<DictItemDeleteBlockedDto>()
            });

        public Task<RecordBatchDeleteResultDto> BatchDeleteAuditLogsAsync(RecordBatchDeleteRequest request) =>
            Task.FromResult(new RecordBatchDeleteResultDto
            {
                Deleted = request != null && request.Ids != null ? request.Ids.Count : 0
            });

        public Task<RecordBatchDeleteResultDto> BatchDeleteBackupRecordsAsync(RecordBatchDeleteRequest request) =>
            Task.FromResult(new RecordBatchDeleteResultDto
            {
                Deleted = request != null && request.Ids != null ? request.Ids.Count : 0
            });

        public Task<PurgeSoftDeletedResultDto> PurgeSoftDeletedAsync() =>
            Task.FromResult(new PurgeSoftDeletedResultDto { TotalPurged = 0 });

        public Task<List<ParamDto>> GetSystemParamsAsync() =>
            Task.FromResult(new List<ParamDto> { new ParamDto { Id = 1, ParamKey = "p_login_lock_count", ParamValue = "5" }, new ParamDto { Id = 2, ParamKey = "p_review_days", ParamValue = "3" } });

        public Task SetSystemParamAsync(string key, string value) => Task.CompletedTask;

        public Task<List<BackupDto>> GetSystemBackupsAsync() =>
            Task.FromResult(new List<BackupDto>());

        public Task<BackupDto> RunSystemBackupAsync(string note) =>
            RunSystemBackupAsync(note, null);

        public Task<BackupDto> RunSystemBackupAsync(string note, string targetPath) =>
            Task.FromResult(new BackupDto
            {
                Id = 1,
                FilePath = string.IsNullOrWhiteSpace(targetPath)
                    ? "C:\\ProgramData\\PropertyManagement\\backups\\demo.db"
                    : targetPath,
                Size = 1024,
                CreatedAt = DateTime.Now
            });

        public Task<BackupDto> RestoreSystemBackupAsync(BackupRestoreRequest request) =>
            Task.FromResult(new BackupDto { Id = request != null ? request.BackupId : 0, FilePath = "backup.db", Size = 1024, CreatedAt = DateTime.Now, Kind = "restore", Result = "成功（演练）" });

        public Task<BackupStatusDto> GetSystemBackupStatusAsync() =>
            Task.FromResult(new BackupStatusDto
            {
                RetainCount = 30,
                AutoDailyEnabled = true,
                PendingRestoreDrill = 1,
                DatabaseSizeText = "演示 2.3 GB",
                AttachmentsSizeText = "6.8 GB"
            });

        public Task<AuditExportDto> ExportAuditLogsAsync(AuditLogQueryRequest request) =>
            Task.FromResult(new AuditExportDto { Content = "时间,操作人,角色,模块,动作,对象,结果,IP", Total = 0 });

        public Task<PageResult<AuditLogDto>> QueryAuditLogsAsync(AuditLogQueryRequest request) =>
            Task.FromResult(new PageResult<AuditLogDto>
            {
                PageIndex = request.PageIndex, PageSize = request.PageSize, Total = 1,
                Items = new List<AuditLogDto> { new AuditLogDto { Id = 1, Action = "收款登记", TargetType = "财务收费", Detail = "SK-2026-00345 ¥1,280.00", CreatedAt = DateTime.Now } }
            });
    }
}
