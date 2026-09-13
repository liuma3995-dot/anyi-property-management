using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Equipment;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Contract.Org;
using PropertyManagement.Contract.PhoneBook;
using PropertyManagement.Contract.Dispute;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Tests.Infrastructure
{
    /// <summary>
    /// 测试数据构造器（Given）：统一走服务层公开入口写库，保证种子数据与生产路径一致
    /// （避免直接 INSERT 绕过业务规则导致"用例数据本身不合法"）。
    /// </summary>
    public static class TestData
    {
        // ---------- 设备台账（BR-EQP） ----------
        /// <summary>新增设备类型（P-04 默认保养周期），返回 id。</summary>
        public static int DeviceType(string name = "测试电梯", int cycle = 30)
        {
            var service = new EquipmentService();
            return service.SaveDeviceType(0, new DeviceTypeRequest { Name = name, MaintenanceCycle = cycle }).Id;
        }

        /// <summary>登记设备（默认在用），返回 id；nextMaintenance 非空时写入自定义下次保养日期。</summary>
        public static int Device(int typeId, string name = "1#测试电梯", DateTime? enableDate = null, DateTime? nextMaintenance = null)
        {
            var service = new EquipmentService();
            var dto = service.SaveDevice(0, new DeviceRequest
            {
                TypeId = typeId,
                Name = name,
                Location = "测试位置",
                EnableDate = enableDate ?? DateTime.Today,
                NextMaintenanceOverride = nextMaintenance
            });
            return dto.Id;
        }

        /// <summary>新增维保单位，返回 id。</summary>
        public static int Vendor(string name = "测试维保单位")
        {
            var service = new EquipmentService();
            return service.SaveVendor(0, new VendorRequest { Name = name, Contact = "张工", Phone = "13900000000" }).Id;
        }

        // ---------- 财务支出（BR-FIN-04/05，BR-EQP-06 支出引用） ----------
        /// <summary>新增支出分类，返回 id。</summary>
        public static int ExpenseCategory(string name = "设备维修费")
        {
            var service = new ExpenseService();
            return service.CreateCategory(new ExpenseCategoryRequest { Name = name }).Id;
        }

        /// <summary>登记支出；objects 非空时写入 t_expense_object_rel 关联，返回 id。</summary>
        public static int Expense(int categoryId, decimal amount, params ExpenseObjectRelDto[] objects)
        {
            var service = new ExpenseService();
            var dto = service.CreateExpense(new ExpenseCreateRequest
            {
                CategoryId = categoryId,
                Amount = amount,
                ExpenseDate = DateTime.Today,
                Note = "单元测试支出",
                Payee = "测试收款方",
                Objects = objects == null || objects.Length == 0 ? null : new List<ExpenseObjectRelDto>(objects)
            });
            return dto.Id;
        }

        /// <summary>支出关联对象（t_expense_object_rel）。</summary>
        public static ExpenseObjectRelDto Rel(ExpenseObjectType type, int objectId)
        {
            return new ExpenseObjectRelDto { ObjectType = type, ObjectId = objectId };
        }

        // ---------- 人员组织（BR-ORG） ----------
        /// <summary>新增部门（parentId 非空时挂在父部门下，覆盖 BR-ORG-07 不限层级）。</summary>
        public static int Department(string name = "测试部门", int? parentId = null)
        {
            var service = new OrgService();
            return service.SaveDepartment(0, new DepartmentRequest { Name = name, ParentId = parentId }).Id;
        }

        /// <summary>新增岗位。</summary>
        public static int Position(int deptId, string name = "测试岗位")
        {
            var service = new OrgService();
            return service.SavePosition(0, new PositionRequest { DeptId = deptId, Name = name }).Id;
        }

        /// <summary>新增员工（在职）。</summary>
        public static int Employee(int deptId, int positionId, string name = "测试员工", string phone = "13800001111")
        {
            var service = new OrgService();
            return service.SaveEmployee(0, new EmployeeRequest
            {
                DeptId = deptId, PositionId = positionId, Name = name, Phone = phone, HireDate = DateTime.Today.AddYears(-1)
            }).Id;
        }

        /// <summary>按名称取内置班次 id（seed：早班/中班/晚班/休）。</summary>
        public static int ShiftId(string name)
        {
            var service = new OrgService();
            return service.ListShifts().First(x => x.Name == name).Id;
        }

        /// <summary>按名称取 seed 员工 id（如 张强=安保部保安班长）。</summary>
        public static int EmployeeIdByName(string name)
        {
            using (var connection = TestDb.OpenConnection())
            {
                return connection.ExecuteScalar<int>(
                    "SELECT id FROM t_employee WHERE name = @name AND del_flag = 0 ORDER BY id LIMIT 1", new { name });
            }
        }

        /// <summary>按名称取 seed 应急场景 id（火灾/电梯困人/…）。</summary>
        public static int SceneIdByName(string name)
        {
            using (var connection = TestDb.OpenConnection())
            {
                return connection.ExecuteScalar<int>(
                    "SELECT id FROM t_emergency_scene WHERE name = @name AND del_flag = 0 ORDER BY id LIMIT 1", new { name });
            }
        }

        // ---------- 便民电话簿（BR-TEL） ----------
        /// <summary>新增电话分类。</summary>
        public static int PhoneCategory(string name = "测试分类")
        {
            var service = new PhoneBookService();
            return service.SaveCategory(0, new PhoneCategoryRequest { Name = name }).Id;
        }

        /// <summary>按名称取 seed 电话分类 id（物业服务中心/工程维修/紧急电话…）。</summary>
        public static int PhoneCategoryIdByName(string name)
        {
            using (var connection = TestDb.OpenConnection())
            {
                return connection.ExecuteScalar<int>(
                    "SELECT id FROM t_phone_category WHERE name = @name AND del_flag = 0 ORDER BY id LIMIT 1", new { name });
            }
        }

        // ---------- 纠纷调解（BR-DIS） ----------
        /// <summary>新增纠纷类型。</summary>
        public static int DisputeType(string name = "测试纠纷类型")
        {
            var service = new DisputeService();
            return service.SaveType(new DisputeTypeRequest { Name = name }).Id;
        }

        // ---------- 基础信息（BR-INF） ----------
        /// <summary>新增小区。</summary>
        public static int Community(string name = "测试小区")
        {
            var service = new BaseInfoService();
            return service.SaveCommunity(0, new CommunityRequest { Name = name, Address = "测试地址" }).Id;
        }

        /// <summary>新增楼栋。</summary>
        public static int Building(int communityId, string buildingNo = "1")
        {
            var service = new BaseInfoService();
            return service.SaveBuilding(0, new BuildingRequest { CommunityId = communityId, BuildingNo = buildingNo, Floors = 18 }).Id;
        }

        /// <summary>新增单元。</summary>
        public static int Unit(int buildingId, string unitNo = "1")
        {
            var service = new BaseInfoService();
            return service.SaveUnit(0, new UnitRequest { BuildingId = buildingId, UnitNo = unitNo }).Id;
        }

        /// <summary>新增房产（unitId 为空时按「楼栋 + 房号」唯一）。</summary>
        public static int Property(int buildingId, string roomNo = "101", int? unitId = null, decimal area = 100m)
        {
            var service = new BaseInfoService();
            return service.SaveProperty(0, new PropertyRequest
            {
                BuildingId = buildingId, UnitId = unitId, RoomNo = roomNo, Area = area,
                Usage = PropertyUsage.Residential, Status = PropertyStatus.Occupied
            }).Id;
        }

        /// <summary>新增业主。</summary>
        public static int Owner(string name = "测试业主", string phone = "13800001234")
        {
            var service = new BaseInfoService();
            return service.SaveOwner(0, new OwnerRequest
            {
                Name = name, Phone = phone, IdCardType = OwnerIdCardType.IdCard,
                IdCard = "110101199001011234", ResidentAddress = "测试地址", Status = OwnerStatus.Living
            }).Id;
        }

        /// <summary>建立业主-房产关系（BR-INF-02：一房仅一名业主）。</summary>
        public static int Relation(int propertyId, int ownerId, decimal share = 100m)
        {
            var service = new BaseInfoService();
            return service.SaveRelation(0, new OwnerPropertyRelationRequest
            {
                PropertyId = propertyId, OwnerId = ownerId, RelType = OwnerRelType.Owner,
                Share = share, EffectiveAt = DateTime.Today, Status = OwnerRelStatus.Active
            }).Id;
        }

        /// <summary>新增车位。</summary>
        public static int Parking(string spaceNo, ParkingSpaceType type = ParkingSpaceType.PropertyRight,
            ParkingSpaceStatus status = ParkingSpaceStatus.Vacant, int? propertyId = null, int? ownerId = null)
        {
            var service = new BaseInfoService();
            return service.SaveParking(0, new ParkingSpaceRequest
            {
                SpaceNo = spaceNo, Area = "A 区", SpaceType = type, Status = status, PropertyId = propertyId, OwnerId = ownerId
            }).Id;
        }

        // ---------- 财务收费（BR-FIN） ----------
        /// <summary>新增收费项目（methodCode：area 按建筑面积 / parking 车位 / card 张 / 其他按户）。</summary>
        public static int ChargeItem(string name, decimal unitPrice, string methodCode = "area",
            string category = "物业费", BillingCycleType cycleType = BillingCycleType.Monthly,
            ChargePayMode payMode = ChargePayMode.Monthly, string cycleName = null)
        {
            var service = new BillingService();
            return service.CreateChargeItem(new ChargeItemRequest
            {
                Name = name, Category = category, MethodCode = methodCode, MethodName = methodCode,
                UnitPrice = unitPrice, CycleType = cycleType, CycleName = cycleName, PayMode = payMode
            }).Id;
        }

        /// <summary>新增计费周期。</summary>
        public static int Cycle(BillingCycleType type = BillingCycleType.Monthly)
        {
            DateTime start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var service = new BillingService();
            return service.CreateCycle(new BillingCycleRequest
            {
                CycleType = type, StartDate = start, EndDate = start.AddMonths(1).AddDays(-1)
            }).Id;
        }
    }
}
