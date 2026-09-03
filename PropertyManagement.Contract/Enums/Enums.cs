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
        Commercial = 1   // 商铺
    }

    /// <summary>房产入住状态。</summary>
    public enum PropertyStatus
    {
        Vacant = 0,    // 空置
        Occupied = 1   // 入住
    }

    /// <summary>车位类型。</summary>
    public enum ParkingSpaceType
    {
        Fixed = 0,     // 固定（产权）
        Temporary = 1  // 临时（人防等禁售）
    }

    /// <summary>业主-房产关系类型。</summary>
    public enum OwnerRelType
    {
        Self = 0,     // 自住
        Rent = 1      // 出租
    }

    /// <summary>员工状态（AM-05 §五）。</summary>
    public enum EmployeeStatus
    {
        Active = 0,    // 在职
        OffDuty = 1,   // 离岗
        Resigned = 2   // 离职
    }

    /// <summary>考勤结果（AM-05 §六）。</summary>
    public enum AttendanceResult
    {
        Recorded = 0,  // 已记录
        Normal = 1,    // 正常
        Abnormal = 2,  // 异常
        Reviewed = 3   // 已审核
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
        Reviewed = 3    // 已复盘
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
        Parking = 1   // 车位
    }
    /// <summary>报表导出格式（D-3：Excel/PDF）。</summary>
    public enum ExportFormat
    {
        Excel = 0,
        Pdf = 1
    }

    /// <summary>基础数据导入模块（UC-INF-006）。</summary>
    public enum ImportModule
    {
        Property = 0,      // 房产
        Owner = 1,         // 业主
        Parking = 2,       // 车位
        OwnerRelation = 3  // 业主-房产关系
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
