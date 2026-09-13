using System;
using System.Collections.Generic;
using System.Linq;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Equipment;
using PropertyManagement.Server.Services;
using PropertyManagement.Tests.Infrastructure;
using Xunit;

namespace PropertyManagement.Tests.Services
{
    /// <summary>
    /// 设备资产台账服务单元测试（BR-EQP-01~06 + §五 5.9 编排补充）。
    /// 断言口径：ApiException.Code + 消息关键字 + DTO 状态字段 + 留痕表行数（§四 4.3）。
    /// </summary>
    public class EquipmentServiceTests : DbTestBase
    {
        private readonly EquipmentService _service = new EquipmentService();

        // ===================== BR-EQP-01 设备状态机 =====================

        [Fact]
        public void ChangeDeviceStatus_在用转维修中_写状态留痕()
        {
            int typeId = TestData.DeviceType("电梯", 30);
            int deviceId = TestData.Device(typeId, "1#客梯");

            var dto = _service.ChangeDeviceStatus(deviceId, new DeviceStatusRequest { Status = DeviceStatus.Repairing });

            Assert.Equal(DeviceStatus.Repairing, dto.Status);
            List<DeviceStatusLogDto> logs = _service.ListDeviceStatusLogs(deviceId);
            Assert.Equal(2, logs.Count); // 登记 + 本次变更
            Assert.Equal(DeviceStatus.InUse, logs[0].OldStatus);
            Assert.Equal(DeviceStatus.Repairing, logs[0].NewStatus);
        }

        [Fact]
        public void ChangeDeviceStatus_报废为终态_再变更抛Conflict()
        {
            int typeId = TestData.DeviceType("电梯", 30);
            int deviceId = TestData.Device(typeId, "2#客梯");
            _service.ChangeDeviceStatus(deviceId, new DeviceStatusRequest { Status = DeviceStatus.Disabled, Reason = "待大修" });
            _service.ChangeDeviceStatus(deviceId, new DeviceStatusRequest { Status = DeviceStatus.Scrapped, Reason = "使用年限到期" });

            var ex = Assert.Throws<ApiException>(() =>
                _service.ChangeDeviceStatus(deviceId, new DeviceStatusRequest { Status = DeviceStatus.InUse }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("不允许的状态变更", ex.Message);
            Assert.Equal(DeviceStatus.Scrapped, _service.GetDevice(deviceId).Status); // 状态未被改写
        }

        [Fact]
        public void ChangeDeviceStatus_停用未填原因_抛ValidationFailed()
        {
            int typeId = TestData.DeviceType("电梯", 30);
            int deviceId = TestData.Device(typeId, "3#客梯");

            var ex = Assert.Throws<ApiException>(() =>
                _service.ChangeDeviceStatus(deviceId, new DeviceStatusRequest { Status = DeviceStatus.Disabled, Reason = "  " }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("停用必须填写原因", ex.Message);
            Assert.Equal(DeviceStatus.InUse, _service.GetDevice(deviceId).Status);
        }

        [Fact]
        public void ChangeDeviceStatus_非法状态值_抛Conflict()
        {
            int typeId = TestData.DeviceType("电梯", 30);
            int deviceId = TestData.Device(typeId, "4#客梯");

            var ex = Assert.Throws<ApiException>(() =>
                _service.ChangeDeviceStatus(deviceId, new DeviceStatusRequest { Status = (DeviceStatus)9 }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
        }

        // ===================== BR-EQP-02 保养按周期 / 年检按年度 =====================

        [Fact]
        public void QueryReminders_保养周期临近_物化待处理提醒()
        {
            int typeId = TestData.DeviceType("排污泵", 30);
            int deviceId = TestData.Device(typeId, "生活水泵", DateTime.Today.AddDays(-25)); // 投运 25 天 + 周期 30 天 → 5 天后到期

            List<EquipmentReminderDto> list = _service.QueryReminders(30, "maintenance", 0);

            EquipmentReminderDto item = Assert.Single(list);
            Assert.Equal(deviceId, item.DeviceId);
            Assert.Equal("maint_due", item.Type);
            Assert.Equal(5, item.RemainingDays);
            Assert.Equal(ReminderStatus.Pending, item.Status);
        }

        [Fact]
        public void QueryReminders_保养周期尚未进入窗口_不物化提醒()
        {
            int typeId = TestData.DeviceType("消防设施测试", 365);
            int deviceId = TestData.Device(typeId, "消防主机"); // 投运今天 + 周期 365 天 → 1 年后到期

            List<EquipmentReminderDto> list = _service.QueryReminders(30, "maintenance", -1);

            Assert.DoesNotContain(list, x => x.DeviceId == deviceId);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_reminder WHERE type = 'maint_due' AND target_id = @id", new { id = deviceId }));
        }

        [Fact]
        public void QueryReminders_年检逾期_按年度进入提醒集合()
        {
            int typeId = TestData.DeviceType("扶梯", 30);
            int deviceId = TestData.Device(typeId, "1#扶梯", DateTime.Today.AddDays(-400)); // 投运 400 天 → 年检逾期 35 天

            List<EquipmentReminderDto> list = _service.QueryReminders(30, "inspection", -1);

            EquipmentReminderDto item = Assert.Single(list);
            Assert.Equal(deviceId, item.DeviceId);
            Assert.Equal("inspect_due", item.Type);
            Assert.True(item.RemainingDays < 0, "年检应按年度推进，当前剩余天数=" + item.RemainingDays);
        }

        // ===================== BR-EQP-03 保养/年检不合格 → 转维修 + 关联故障 =====================

        [Fact]
        public void AddInspection_结果不合格_设备转维修中并生成故障单()
        {
            int typeId = TestData.DeviceType("扶梯", 30);
            int deviceId = TestData.Device(typeId, "2#扶梯");

            var dto = _service.AddInspection(new InspectionRecordRequest
            {
                DeviceId = deviceId, IDate = DateTime.Today, Result = "不合格", FailReason = "制动距离超标"
            });

            Assert.False(string.IsNullOrWhiteSpace(dto.FaultNo), "不合格年检应生成维修工单号");
            Assert.Equal(DeviceStatus.Repairing, _service.GetDevice(deviceId).Status);
            var fault = Assert.Single(_service.ListFaults(deviceId));
            Assert.Contains("年检不合格", fault.Symptom);
            Assert.Equal(0, fault.Status); // 待处理故障单
        }

        [Fact]
        public void AddInspection_不合格未填说明_抛ValidationFailed且不落库()
        {
            int typeId = TestData.DeviceType("扶梯", 30);
            int deviceId = TestData.Device(typeId, "3#扶梯");

            var ex = Assert.Throws<ApiException>(() => _service.AddInspection(new InspectionRecordRequest
            {
                DeviceId = deviceId, IDate = DateTime.Today, Result = "不合格", FailReason = " "
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("不合格说明", ex.Message);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_inspection_record WHERE device_id = @id", new { id = deviceId }));
            Assert.Equal(DeviceStatus.InUse, _service.GetDevice(deviceId).Status);
        }

        [Fact]
        public void AddMaintenance_结果不合格_设备转维修中并生成故障单()
        {
            int typeId = TestData.DeviceType("排污泵", 30);
            int deviceId = TestData.Device(typeId, "排污泵");

            var dto = _service.AddMaintenance(new MaintenanceRecordRequest
            {
                DeviceId = deviceId, MDate = DateTime.Today, Result = "不合格", FailReason = "电机绝缘不合格"
            });

            Assert.False(string.IsNullOrWhiteSpace(dto.FaultNo));
            Assert.Equal(DeviceStatus.Repairing, _service.GetDevice(deviceId).Status);
        }

        // ===================== BR-EQP-04 故障闭环 =====================

        [Fact]
        public void AddFault_缺故障现象_抛ValidationFailed()
        {
            int typeId = TestData.DeviceType("电梯", 30);
            int deviceId = TestData.Device(typeId, "5#客梯");

            var ex = Assert.Throws<ApiException>(() => _service.AddFault(new FaultRecordRequest
            {
                DeviceId = deviceId, FTime = DateTime.Now, Symptom = "  "
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("故障现象不能为空", ex.Message);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_fault_record WHERE device_id = @id", new { id = deviceId }));
        }

        [Fact]
        public void HandleFault_状态已修复但缺处理结果_抛ValidationFailed()
        {
            int typeId = TestData.DeviceType("电梯", 30);
            int deviceId = TestData.Device(typeId, "6#客梯");
            var fault = _service.AddFault(new FaultRecordRequest
            {
                DeviceId = deviceId, FTime = DateTime.Now, Symptom = "运行异响", Level = 0
            });

            var ex = Assert.Throws<ApiException>(() => _service.HandleFault(fault.Id,
                new FaultHandleRequest { Status = 2, Handle = "   " }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("处理结果不能为空", ex.Message);
            Assert.Equal(0, ScalarInt("SELECT status FROM t_fault_record WHERE id = @id", new { id = fault.Id }));
        }

        [Fact]
        public void HandleFault_补录处理结果_故障修复且设备回到在用()
        {
            int typeId = TestData.DeviceType("电梯", 30);
            int deviceId = TestData.Device(typeId, "7#客梯");
            var fault = _service.AddFault(new FaultRecordRequest
            {
                DeviceId = deviceId, FTime = DateTime.Now, Symptom = "轿厢抖动", Cause = "导靴磨损"
            });
            Assert.Equal(DeviceStatus.Repairing, _service.GetDevice(deviceId).Status); // 报修即转维修中

            var handled = _service.HandleFault(fault.Id,
                new FaultHandleRequest { Status = 2, Cause = "导靴磨损", Handle = "更换导靴并试运行" });

            Assert.Equal(2, handled.Status);
            Assert.Equal("已修复", handled.StatusText);
            Assert.Equal(DeviceStatus.InUse, _service.GetDevice(deviceId).Status);
        }

        // ===================== BR-EQP-05 设备类型可扩展 =====================

        [Fact]
        public void SaveDeviceType_同名类别_抛ValidationFailed()
        {
            TestData.DeviceType("智能水表", 30);

            var ex = Assert.Throws<ApiException>(() =>
                _service.SaveDeviceType(0, new DeviceTypeRequest { Name = "智能水表", MaintenanceCycle = 60 }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("设备类别已存在", ex.Message);
        }

        [Fact]
        public void SaveDeviceType_保养周期为0_抛ValidationFailed()
        {
            var ex = Assert.Throws<ApiException>(() =>
                _service.SaveDeviceType(0, new DeviceTypeRequest { Name = "新风系统", MaintenanceCycle = 0 }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("保养周期", ex.Message);
        }

        [Fact]
        public void SaveDeviceType_新增自定义类别_列表可查()
        {
            var dto = _service.SaveDeviceType(0, new DeviceTypeRequest { Name = "监控摄像头", MaintenanceCycle = 45 });

            Assert.True(dto.Id > 0);
            Assert.Contains(_service.ListDeviceTypes(), x => x.Id == dto.Id && x.MaintenanceCycle == 45);
        }

        // ===================== BR-EQP-06 维保单位复用与删除阻断（BUG-001 回归） =====================

        [Fact]
        public void DeleteVendor_未被任何记录引用_软删成功()
        {
            int vendorId = TestData.Vendor("无人引用维保");

            _service.DeleteVendor(vendorId);

            Assert.Equal(1, ScalarInt("SELECT del_flag FROM t_vendor WHERE id = @id", new { id = vendorId }));
            Assert.DoesNotContain(_service.ListVendors(), x => x.Id == vendorId);
        }

        [Fact]
        public void DeleteVendor_已被保养记录引用_抛Conflict()
        {
            int typeId = TestData.DeviceType("电梯", 30);
            int deviceId = TestData.Device(typeId, "8#客梯");
            int vendorId = TestData.Vendor("迅达测试维保");
            _service.AddMaintenance(new MaintenanceRecordRequest
            {
                DeviceId = deviceId, MDate = DateTime.Today, Result = "合格", VendorId = vendorId
            });

            var ex = Assert.Throws<ApiException>(() => _service.DeleteVendor(vendorId));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("引用", ex.Message);
            Assert.Equal(0, ScalarInt("SELECT del_flag FROM t_vendor WHERE id = @id", new { id = vendorId }));
            Assert.Contains(_service.ListVendors(), x => x.Id == vendorId);
        }

        [Fact]
        public void DeleteVendor_已被年检记录引用_抛Conflict()
        {
            int typeId = TestData.DeviceType("消防设施测试", 90);
            int deviceId = TestData.Device(typeId, "消防泵房");
            int vendorId = TestData.Vendor("消防测试维保");
            _service.AddInspection(new InspectionRecordRequest
            {
                DeviceId = deviceId, IDate = DateTime.Today, Result = "合格", VendorId = vendorId
            });

            var ex = Assert.Throws<ApiException>(() => _service.DeleteVendor(vendorId));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Equal(0, ScalarInt("SELECT del_flag FROM t_vendor WHERE id = @id", new { id = vendorId }));
        }

        [Fact]
        public void DeleteVendor_已被支出记录引用_抛Conflict()
        {
            int vendorId = TestData.Vendor("外包测试单位");
            int categoryId = TestData.ExpenseCategory();
            TestData.Expense(categoryId, 1200m, TestData.Rel(ExpenseObjectType.Vendor, vendorId));

            var ex = Assert.Throws<ApiException>(() => _service.DeleteVendor(vendorId));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Equal(0, ScalarInt("SELECT del_flag FROM t_vendor WHERE id = @id", new { id = vendorId }));
        }

        [Fact]
        public void DeleteVendor_支出已软删_视为无引用可删除()
        {
            int vendorId = TestData.Vendor("历史外包单位");
            int categoryId = TestData.ExpenseCategory();
            int expenseId = TestData.Expense(categoryId, 800m, TestData.Rel(ExpenseObjectType.Vendor, vendorId));
            new ExpenseService().DeleteExpense(expenseId);

            _service.DeleteVendor(vendorId);

            Assert.Equal(1, ScalarInt("SELECT del_flag FROM t_vendor WHERE id = @id", new { id = vendorId }));
        }

        [Fact]
        public void SaveVendor_同一单位被多台设备复用_记录保留执行方名称()
        {
            int typeId = TestData.DeviceType("电梯", 30);
            int vendorId = TestData.Vendor("复用测试维保");
            int first = TestData.Device(typeId, "9#客梯");
            int second = TestData.Device(typeId, "10#客梯");
            _service.AddMaintenance(new MaintenanceRecordRequest { DeviceId = first, MDate = DateTime.Today, Result = "合格", VendorId = vendorId });
            _service.AddMaintenance(new MaintenanceRecordRequest { DeviceId = second, MDate = DateTime.Today, Result = "合格", VendorId = vendorId });

            Assert.Single(_service.ListVendors().Where(x => x.Name == "复用测试维保"));
            var records = _service.ListMaintenance(first).Concat(_service.ListMaintenance(second)).ToList();
            Assert.Equal(2, records.Count);
            Assert.All(records, r => Assert.Equal("复用测试维保", r.VendorName));
        }

        // ===================== 5.9 服务层编排补充用例 =====================

        [Fact]
        public void SaveDevice_自定义下次保养日期_按值返回并标记来源()
        {
            int typeId = TestData.DeviceType("电梯", 30);
            DateTime custom = DateTime.Today.AddDays(15);

            var dto = _service.SaveDevice(0, new DeviceRequest
            {
                TypeId = typeId, Name = "11#客梯", EnableDate = DateTime.Today, NextMaintenanceOverride = custom
            });

            Assert.Equal(custom, dto.NextMaintenance);
            Assert.Equal("自定义", dto.NextMaintenanceSource);
            Assert.Equal("自定义", _service.GetDevice(dto.Id).NextMaintenanceSource);
        }

        [Fact]
        public void SaveDevice_非在用设备设置下次保养日期_抛Conflict()
        {
            int typeId = TestData.DeviceType("电梯", 30);
            int deviceId = TestData.Device(typeId, "12#客梯");
            _service.ChangeDeviceStatus(deviceId, new DeviceStatusRequest { Status = DeviceStatus.Disabled, Reason = "封存" });

            var ex = Assert.Throws<ApiException>(() => _service.SaveDevice(deviceId, new DeviceRequest
            {
                TypeId = typeId, Name = "12#客梯", NextMaintenanceOverride = DateTime.Today.AddDays(10)
            }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("仅在用设备可设置下次保养时间", ex.Message);
        }

        [Fact]
        public void BatchDeleteReminders_含待处理记录_整批拒绝并返回明细()
        {
            int typeId = TestData.DeviceType("排污泵", 30);
            TestData.Device(typeId, "1#水泵", DateTime.Today.AddDays(-25));
            TestData.Device(typeId, "2#水泵", DateTime.Today.AddDays(-28)); // 逾期
            List<EquipmentReminderDto> pending = _service.QueryReminders(30, "maintenance", 0);
            Assert.Equal(2, pending.Count);

            var result = _service.BatchDeleteReminders(new ReminderBatchDeleteRequest
            {
                Ids = pending.Select(x => x.ReminderId).ToList()
            });

            Assert.Equal(0, result.Deleted);
            Assert.Equal(2, result.Blocked.Count);
            Assert.Equal(2, ScalarInt("SELECT COUNT(1) FROM t_reminder WHERE del_flag = 0 AND type = 'maint_due'"));
        }

        [Fact]
        public void BatchDeleteReminders_全部已处理_软删且记录行保留()
        {
            int typeId = TestData.DeviceType("排污泵", 30);
            TestData.Device(typeId, "3#水泵", DateTime.Today.AddDays(-25));
            List<EquipmentReminderDto> pending = _service.QueryReminders(30, "maintenance", 0);
            int reminderId = Assert.Single(pending).ReminderId;
            var handled = _service.HandleReminder(reminderId);
            Assert.Equal(ReminderStatus.Processed, handled.Status);

            // 已处理记录仍保留在列表中（R6：标记已处理后数据行不消失）
            Assert.Contains(_service.QueryReminders(30, "maintenance", -1), x => x.ReminderId == reminderId);

            var result = _service.BatchDeleteReminders(new ReminderBatchDeleteRequest { Ids = new List<int> { reminderId } });

            Assert.Equal(1, result.Deleted);
            Assert.Empty(result.Blocked);
            Assert.Empty(_service.QueryReminders(30, "maintenance", -1)); // 软删后默认查询不再返回
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_reminder WHERE id = @id AND del_flag = 1", new { id = reminderId }));
        }

        [Fact]
        public void SaveReminderTeam_传空_恢复默认班组规则()
        {
            int typeId = TestData.DeviceType("排污泵", 30);
            TestData.Device(typeId, "4#水泵", DateTime.Today.AddDays(-25));
            int reminderId = Assert.Single(_service.QueryReminders(30, "maintenance", 0)).ReminderId;
            Assert.Equal("工程部", _service.QueryReminders(30, "maintenance", 0)[0].ResponsibleTeam);

            var assigned = _service.SaveReminderTeam(reminderId, new ReminderTeamRequest { Team = "专项小组" });
            Assert.Equal("专项小组", assigned.ResponsibleTeam);

            var restored = _service.SaveReminderTeam(reminderId, new ReminderTeamRequest { Team = "  " });

            Assert.Equal("工程部", restored.ResponsibleTeam); // 未逾期 → 默认工程部
            Assert.True(string.IsNullOrEmpty(restored.ResponsibleTeamOverride));
        }

        [Fact]
        public void ListMaintainTypes_默认_仅返回系统保养与年检两类()
        {
            List<MaintainTypeDto> types = _service.ListMaintainTypes();

            Assert.Equal(2, types.Count);
            Assert.All(types, t => Assert.True(t.IsSystem, "系统内置类型标记应为 true：" + t.Name));
            Assert.Contains(types, t => t.Name == "保养" && t.Kind == 0);
            Assert.Contains(types, t => t.Name == "年检" && t.Kind == 1);
            // 历史自定义类型（migration_025 预置）已软删下线，仅 includeDisabled 时可追溯
            Assert.Contains(_service.ListMaintainTypes(true), t => !t.IsSystem);
        }

        [Fact]
        public void SaveCustomRecord_新增_设备维度可查且类型候选去重()
        {
            int typeId = TestData.DeviceType("电梯", 30);
            int deviceId = TestData.Device(typeId, "13#客梯");

            _service.SaveCustomRecord(0, new DeviceCustomRecordRequest
            {
                DeviceId = deviceId, TypeName = "清洁保养", RDate = DateTime.Today, Content = "轿厢清洁", Result = "合格", Operator = "张工"
            });
            _service.SaveCustomRecord(0, new DeviceCustomRecordRequest
            {
                DeviceId = deviceId, TypeName = "清洁保养", RDate = DateTime.Today.AddDays(-1), Content = "导轨清洁", Result = "合格", Operator = "张工"
            });

            Assert.Equal(2, _service.ListCustomRecords(deviceId).Count);
            Assert.Single(_service.ListCustomRecordTypes().Where(x => x == "清洁保养"));
        }

        [Fact]
        public void SaveCustomRecord_费用为负_抛ValidationFailed()
        {
            int typeId = TestData.DeviceType("电梯", 30);
            int deviceId = TestData.Device(typeId, "14#客梯");

            var ex = Assert.Throws<ApiException>(() => _service.SaveCustomRecord(0, new DeviceCustomRecordRequest
            {
                DeviceId = deviceId, TypeName = "专项检修", RDate = DateTime.Today, Cost = -1m
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("费用不能为负数", ex.Message);
        }

        [Fact]
        public void HandleReminder_重复处置_抛ValidationFailed()
        {
            int typeId = TestData.DeviceType("排污泵", 30);
            TestData.Device(typeId, "5#水泵", DateTime.Today.AddDays(-25));
            int reminderId = Assert.Single(_service.QueryReminders(30, "maintenance", 0)).ReminderId;
            _service.HandleReminder(reminderId);

            var ex = Assert.Throws<ApiException>(() => _service.HandleReminder(reminderId));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("提醒已处理", ex.Message);
        }
    }
}
