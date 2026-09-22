namespace PropertyManagement.Contract.Enums
{
    /// <summary>账单状态（AM-05 §一，BR-FIN-01）。</summary>
    public enum BillStatus
    {
        Pending = 0,   // 待缴
        Partial = 1,   // 部分缴
        Overdue = 2,   // 逾期
        Paid = 3,      // 已缴
        Reversed = 4,   // 已冲正
        Draft = 5       // 草稿（CHG-M4-01：D4-1 生成后未发布）
    }

    /// <summary>收费项目计费模式（BR-FIN-03，字典/枚举双轨）。</summary>
    public enum ChargePayMode
    {
        Yearly = 0,     // 按年
        Monthly = 1,    // 按月
        Temporary = 2   // 临时
    }

    /// <summary>计费周期类型（T4F-1-5：新增 一次性/自定义）。</summary>
    public enum BillingCycleType
    {
        Yearly = 0,   // 按年
        Monthly = 1,  // 按月
        OneTime = 2,  // 一次性（T4F-1-5）
        Custom = 3    // 自定义周期（周期名见 t_charge_item.cycle_name，T4F-1-5）
    }

    /// <summary>收款方式。</summary>
    public enum PayMethod
    {
        Cash = 0,          // 现金
        Transfer = 1,      // 转账
        WeChat = 2,        // 微信
        BankTransfer = 3,  // 银行转账
        Pos = 4            // POS
    }

    /// <summary>缴费记录状态。</summary>
    public enum PaymentStatus
    {
        Normal = 0,    // 正常
        Reversed = 1   // 冲正
    }

    /// <summary>退款/减免/调整类型（BR-FIN-06）。</summary>
    public enum RefundType
    {
        Refund = 0,     // 退款
        Discount = 1,   // 减免
        Adjustment = 2  // 调整
    }

    /// <summary>房产用途。</summary>
    public enum PropertyUsage
    {
        Residential = 0, // 住宅
        Commercial = 1,  // 商铺
        Vacant = 2       // 空置（v1.1.2 CHG-48：收费规格「适用条件 → 房产用途」需要能按空置定价）
    }

    /// <summary>房产入住状态。</summary>
    public enum PropertyStatus
    {
        Vacant = 0,      // 空置
        Occupied = 1,    // 入住
        Renovating = 2   // 装修中
    }

    /// <summary>车位类型。</summary>
    public enum ParkingSpaceType
    {
        PropertyRight = 0, // 产权（固定）
        CivilDefense = 1,  // 普通（v1.1.2 CHG-48：对外文案由「人防」改为「普通」；沿用原值 1 与禁售约束）
        Temporary = 2      // 临时（租用）
    }

    /// <summary>车位上锁状态（PG-INF-04，BR-INF-03）。</summary>
    public enum ParkingSpaceStatus
    {
        Owned = 0,     // 已售
        Rented = 1,    // 已租
        Vacant = 2,    // 空置
        Repairing = 3  // 维修中
    }

    /// <summary>车位租金计费模式（PG-INF-04，BR-INF-03）。</summary>
    public enum RentMode
    {
        Monthly = 0,   // 按月
        Yearly = 1     // 按年
    }

    /// <summary>业主-房产关系类型。</summary>
    public enum OwnerRelType
    {
        Owner = 0,      // 业主
        CoOwner = 1,    // 共有人
        RentRecord = 2  // 租户备案
    }

    /// <summary>业主-房产关系状态（PG-INF-03，租约到期前 30 天→即将到期）。</summary>
    public enum OwnerRelStatus
    {
        Active = 0,   // 有效
        Expiring = 1, // 即将到期
        Released = 2  // 已解除
    }

    /// <summary>业主状态（PG-INF-02：在住/搬离）。</summary>
    public enum OwnerStatus
    {
        Living = 0,    // 在住
        MovedOut = 1   // 搬离
    }

    /// <summary>员工状态（AM-05 §五）。</summary>
    public enum EmployeeStatus
    {
        Active = 0,    // 在职
        OffDuty = 1,   // 离岗
        Resigned = 2,  // 离职
        Vacation = 3   // 休假（CHG-ORG-01 在岗状态枚举）
    }

    /// <summary>考勤结果（AM-05 §六）。</summary>
    public enum AttendanceResult
    {
        Recorded = 0,  // 已记录
        Normal = 1,    // 正常
        Abnormal = 2,  // 异常
        Reviewed = 3,  // 已审核
        Late = 4,      // 迟到
        Absent = 5,    // 旷工
        Leave = 6      // 请假
    }

    /// <summary>应急事件级别（PG-EMG-02/03：Ⅰ/Ⅱ/Ⅲ 级）。</summary>
    public enum EmergencyEventLevel
    {
        Level1 = 1,    // Ⅰ 级（重大）
        Level2 = 2,    // Ⅱ 级（较大）
        Level3 = 3     // Ⅲ 级（一般）
    }

    /// <summary>排班状态。</summary>
    public enum ScheduleStatus
    {
        Draft = 0,     // 草稿
        Published = 1, // 已发布
        Cancelled = 2  // 已取消
    }

    /// <summary>应急事件状态（AM-05 §二）。</summary>
    public enum EmergencyEventStatus
    {
        Initiated = 0,  // 已发起
        Handling = 1,   // 处置中
        Closed = 2,     // 已结案
        Reviewed = 3,   // 已复盘
        Cancelled = 4   // 已撤销（1 分钟内误发起撤销留痕）
    }

    /// <summary>应急指派类型（责任人/值班人，UC-EMG-004）。</summary>
    public enum EmergencyAssignType
    {
        Responsible = 0, // 责任人
        Duty = 1         // 值班人
    }

    /// <summary>纠纷案件状态（AM-05 §三）。</summary>
    public enum DisputeCaseStatus
    {
        Registered = 0, // 已登记
        Handling = 1,   // 处理中
        Closed = 2      // 已结案
    }

    /// <summary>纠纷结案类型（P-08：调解成功/自行和解/转办）。</summary>
    public enum DisputeCloseType
    {
        Mediated = 0,    // 调解成功
        Settled = 1,     // 自行和解
        Transferred = 2  // 转办
    }

    /// <summary>设备状态（AM-05 §四）。</summary>
    public enum DeviceStatus
    {
        InUse = 0,      // 在用
        Repairing = 1,  // 维修中
        Disabled = 2,   // 停用
        Scrapped = 3    // 报废
    }

    /// <summary>电话条目状态（AM-05 §七）。</summary>
    public enum PhoneEntryStatus
    {
        Enabled = 0,  // 启用
        Disabled = 1  // 停用
    }

    /// <summary>电话条目类型（紧急/普通/员工通讯录，UC-TEL-002/005）。</summary>
    public enum PhoneEntryType
    {
        Emergency = 0, // 紧急（置顶禁删 BR-TEL-03）
        Normal = 1,    // 普通
        Employee = 2   // 员工通讯录同步
    }

    /// <summary>提醒状态（仪表盘，UC-COM-006）。</summary>
    public enum ReminderStatus
    {
        Pending = 0,   // 待处理
        Processed = 1  // 已处理
    }


    /// <summary>收费项目适用对象类型（CHG-M4-09：D4-1 账单生成按对象类型取候选，避免物业费/停车费混生成）。</summary>
    public enum ChargeObjectType
    {
        Property = 0, // 房产
        Parking = 1,  // 车位
        /// <summary>CHG-v1.1.0-16：业主（直缴：办卡费/清理费/维修费等面向业主本人的收费项目）。</summary>
        Owner = 2,
        /// <summary>
        /// CHG-v1.1.0-17：自定义缴费对象（租户/广告商/外部单位等无基础信息档案的对象），
        /// 具体名称由 charge_object 字典项（t_charge_item.object_code）决定；该类项目暂不支持批量出账。
        /// </summary>
        Custom = 3
    }

    /// <summary>
    /// CHG-v1.1.2-26：计量变量取值来源（t_charge_variable.source）。
    /// 档案自动 / 周期派生为系统内置专用；用户自建仅开放 手填 / 固定值。
    /// </summary>
    public enum ChargeVariableSource
    {
        Archive = 0,       // 档案自动（房产面积、户数等，代码级绑定档案字段）
        CycleDerived = 1,  // 周期派生（月数、天数，由计费周期自动折算）
        Manual = 2,        // 手填（出账时由用户输入）
        Fixed = 3          // 固定值（不显示输入项，如「数量 = 1」等价于按户 / 按次）
    }

    /// <summary>CHG-v1.1.2-26：计量变量值类型。</summary>
    public enum ChargeVariableValueType
    {
        Integer = 0,  // 整数
        Decimal = 1   // 小数
    }

    /// <summary>CHG-v1.1.2-26：计量变量适用对象范围。</summary>
    public enum ChargeVariableScope
    {
        Property = 0,   // 房产
        Parking = 1,    // 车位
        Owner = 2,      // 业主
        Custom = 3,     // 自定义缴费对象
        Any = 4         // 不限
    }
    /// <summary>报表导出格式（D-3：Excel/PDF）。</summary>
    public enum ExportFormat
    {
        Excel = 0,
        Pdf = 1,
        Csv = 2
    }

    /// <summary>基础数据导入模块（UC-INF-006）。</summary>
    public enum ImportModule
    {
        Property = 0,      // 房产
        Owner = 1,         // 业主
        Parking = 2,       // 车位
        OwnerRelation = 3  // 业主-房产关系
    }

    /// <summary>基础数据导入批次状态（PG-INF-05，校验通过前不落库）。</summary>
    public enum ImportStatus
    {
        Processing = 0,   // 校验中
        Success = 1,      // 成功
        PartialSuccess = 2, // 部分成功（部分行校验失败）
        Failed = 3        // 失败
    }

    /// <summary>
    /// 导入回执行处理结果（CHG-v1.2.0-01）。
    /// 背景：重复数据改走「覆盖处理」后，用户拿不到「这一批到底覆盖了谁」的凭据，
    /// 同名业主被覆盖后无法追溯 → 每次导入都落一份逐行回执（新增/覆盖/失败）。
    /// </summary>
    public enum ImportRowResult
    {
        Inserted = 0,   // 新增
        Updated = 1,    // 覆盖（命中既有记录）
        Failed = 2      // 失败（校验未通过，未入库）
    }

    /// <summary>业主证件类型（PG-INF-02）。</summary>
    public enum OwnerIdCardType
    {
        IdCard = 0,   // 身份证
        Passport = 1, // 护照
        Hukou = 2,    // 户口簿
        Other = 3     // 其他
    }

    /// <summary>登录账号状态。</summary>
    public enum UserStatus
    {
        Active = 0,   // 正常
        Locked = 1,   // 锁定（P-02 失败 5 次/30 分钟）
        Disabled = 2  // 停用
    }

    /// <summary>字典项状态。</summary>
    public enum DictItemStatus
    {
        Enabled = 0,
        Disabled = 1
    }

    /// <summary>基础信息变更对象类型（t_base_change_log）。</summary>
    public enum BaseChangeObjectType
    {
        Owner = 0,     // 业主（含联系方式变更）
        Property = 1,  // 房产
        Relation = 2,  // 业主-房产关系
        Parking = 3    // 车位
    }

    /// <summary>支出关联对象类型（t_expense_object_rel）。</summary>
    public enum ExpenseObjectType
    {
        Employee = 0, // 员工
        Device = 1,   // 设备
        Vendor = 2    // 维保单位
    }
}
