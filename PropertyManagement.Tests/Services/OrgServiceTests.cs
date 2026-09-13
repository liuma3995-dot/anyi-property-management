using System;
using System.Collections.Generic;
using System.Linq;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Org;
using PropertyManagement.Server.Services;
using PropertyManagement.Tests.Infrastructure;
using Xunit;

namespace PropertyManagement.Tests.Services
{
    /// <summary>
    /// 人员组织服务单元测试（BR-ORG-01~07，BR-ORG-06 含与应急处置的跨模块联动）。
    /// </summary>
    public class OrgServiceTests : DbTestBase
    {
        private readonly OrgService _service = new OrgService();

        // ===================== BR-ORG-07 部门/岗位可扩展、层级不限 =====================

        [Fact]
        public void SaveDepartment_名称为空_抛ValidationFailed()
        {
            var ex = Assert.Throws<ApiException>(() =>
                _service.SaveDepartment(0, new DepartmentRequest { Name = "   " }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("部门名称不能为空", ex.Message);
        }

        [Fact]
        public void SaveDepartment_多层级部门_保存成功且层级可查()
        {
            int root = TestData.Department("测试集团总部");
            int child = TestData.Department("测试区域公司", root);
            int grandChild = TestData.Department("测试项目处", child);

            List<DepartmentDto> list = _service.ListDepartments(null);

            Assert.Contains(list, x => x.Id == root);
            Assert.Contains(list, x => x.Id == child && x.ParentId == root);
            Assert.Contains(list, x => x.Id == grandChild && x.ParentId == child);
        }

        // ===================== BR-ORG-02 离职档案保留 =====================

        [Fact]
        public void ResignEmployee_办理离职_档案保留且状态留痕()
        {
            int dept = TestData.Department("离职测试部");
            int pos = TestData.Position(dept, "测试岗位");
            int employeeId = TestData.Employee(dept, pos, "离职测试员工", "13800009901");

            var dto = _service.ResignEmployee(employeeId);

            Assert.Equal(EmployeeStatus.Resigned, dto.Status);
            Assert.Equal(0, ScalarInt("SELECT del_flag FROM t_employee WHERE id = @id", new { id = employeeId }));
            Assert.NotNull(_service.GetEmployee(employeeId)); // 档案仍可查（非物理删除）
            List<EmployeeStatusLogDto> logs = _service.ListEmployeeStatusLogs(employeeId);
            Assert.Equal(2, logs.Count); // 入职 + 离职
            Assert.Equal(EmployeeStatus.Resigned, logs[0].Status);
        }

        [Fact]
        public void ResignEmployee_重复离职_抛Conflict()
        {
            int dept = TestData.Department("离职测试部");
            int pos = TestData.Position(dept, "测试岗位");
            int employeeId = TestData.Employee(dept, pos, "重复离职员工", "13800009902");
            _service.ResignEmployee(employeeId);

            var ex = Assert.Throws<ApiException>(() => _service.ResignEmployee(employeeId));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("已办理离职", ex.Message);
        }

        [Fact]
        public void DeleteEmployee_在职员工_抛Conflict()
        {
            int dept = TestData.Department("离职测试部");
            int pos = TestData.Position(dept, "测试岗位");
            int employeeId = TestData.Employee(dept, pos, "在职员工", "13800009903");

            var ex = Assert.Throws<ApiException>(() => _service.DeleteEmployee(employeeId));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("请先办理离职", ex.Message);
        }

        [Fact]
        public void ChangeEmployeeStatus_直接置离职_抛Conflict并要求走离职流程()
        {
            int dept = TestData.Department("离职测试部");
            int pos = TestData.Position(dept, "测试岗位");
            int employeeId = TestData.Employee(dept, pos, "流转测试员工", "13800009904");

            var ex = Assert.Throws<ApiException>(() => _service.ChangeEmployeeStatus(employeeId,
                new EmployeeStatusRequest { Status = EmployeeStatus.Resigned }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("离职流程", ex.Message);
        }

        // ===================== BR-ORG-03 同一员工同一时间不能排两个班 =====================

        [Fact]
        public void GenerateSchedules_同人同日排两个班次_阻断保存并回滚()
        {
            int employeeId = NewSchedulableEmployee("双班冲突员工");
            int early = TestData.ShiftId("早班");
            int middle = TestData.ShiftId("中班");
            _service.GenerateSchedules(new ScheduleGenerateRequest
            {
                Items = new List<ScheduleRequest> { new ScheduleRequest { EmployeeId = employeeId, ShiftId = early, WorkDate = DateTime.Today } }
            });

            var ex = Assert.Throws<ApiException>(() => _service.GenerateSchedules(new ScheduleGenerateRequest
            {
                Items = new List<ScheduleRequest> { new ScheduleRequest { EmployeeId = employeeId, ShiftId = middle, WorkDate = DateTime.Today } }
            }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("双班", ex.Message);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_schedule WHERE employee_id = @id AND del_flag = 0", new { id = employeeId }));
        }

        [Fact]
        public void PublishSchedules_库内存在同人双班_阻断发布()
        {
            int employeeId = NewSchedulableEmployee("双班发布员工");
            int early = TestData.ShiftId("早班");
            int middle = TestData.ShiftId("中班");
            Execute("INSERT INTO t_schedule (employee_id, shift_id, work_date, status, del_flag) VALUES " +
                    "(@e, @s1, @d, 0, 0), (@e, @s2, @d, 0, 0)",
                new { e = employeeId, s1 = early, s2 = middle, d = DateTime.Today.ToString("yyyy-MM-dd") });

            var ex = Assert.Throws<ApiException>(() => _service.PublishSchedules(new SchedulePublishRequest
            {
                FromDate = DateTime.Today, ToDate = DateTime.Today, Force = true
            }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_schedule WHERE employee_id = @id AND status = 1", new { id = employeeId }));
        }

        // ===================== BR-ORG-04 缺员允许发布但必须标记缺口 =====================

        [Fact]
        public void PublishSchedules_存在缺员_默认阻断且强制发布写缺口标记()
        {
            int employeeId = NewSchedulableEmployee("缺员测试员工");
            int early = TestData.ShiftId("早班"); // seed 需求 2 人
            _service.GenerateSchedules(new ScheduleGenerateRequest
            {
                Items = new List<ScheduleRequest> { new ScheduleRequest { EmployeeId = employeeId, ShiftId = early, WorkDate = DateTime.Today } }
            });

            var blocked = Assert.Throws<ApiException>(() => _service.PublishSchedules(new SchedulePublishRequest
            {
                FromDate = DateTime.Today, ToDate = DateTime.Today
            }));
            Assert.Equal(ErrorCode.Conflict, blocked.Code);
            Assert.Contains("缺员", blocked.Message);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_schedule WHERE work_date = @d AND status = 1",
                new { d = DateTime.Today.ToString("yyyy-MM-dd") }));

            SchedulePlanDto plan = _service.PublishSchedules(new SchedulePublishRequest
            {
                FromDate = DateTime.Today, ToDate = DateTime.Today, Force = true
            });

            Assert.NotEmpty(plan.Understaffed);
            Assert.All(plan.Schedules, s => Assert.Equal(ScheduleStatus.Published, s.Status));
            Assert.True(ScalarInt("SELECT COUNT(1) FROM t_schedule_conflict_log WHERE conflict_type = '缺员'") > 0,
                "强制发布必须写缺员标记（BR-ORG-04）");
        }

        [Fact]
        public void PublishSchedules_存在缺员未强制_抛Conflict且排班仍为草稿()
        {
            int employeeId = NewSchedulableEmployee("缺员阻断员工");
            _service.GenerateSchedules(new ScheduleGenerateRequest
            {
                Items = new List<ScheduleRequest>
                {
                    new ScheduleRequest { EmployeeId = employeeId, ShiftId = TestData.ShiftId("早班"), WorkDate = DateTime.Today }
                }
            });

            var ex = Assert.Throws<ApiException>(() => _service.PublishSchedules(new SchedulePublishRequest
            {
                FromDate = DateTime.Today, ToDate = DateTime.Today, Force = false
            }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("缺员缺口", ex.Message);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_schedule WHERE employee_id = @id AND status = 1", new { id = employeeId }));
        }

        // ===================== BR-ORG-05 考勤异常需管理员审核 =====================

        [Fact]
        public void ReviewAttendance_迟到异常_审核通过并记录审核人()
        {
            int employeeId = NewSchedulableEmployee("考勤测试员工");
            PublishTodayShift(employeeId, "早班");
            var attendance = _service.RecordAttendance(new AttendanceRequest
            {
                EmployeeId = employeeId, WorkDate = DateTime.Today, CheckIn = "09:30", CheckOut = "16:05"
            });
            Assert.Equal(AttendanceResult.Late, attendance.Result);
            Assert.Null(attendance.ReviewBy);

            var reviewed = _service.ReviewAttendance(attendance.Id, new AttendanceReviewRequest
            {
                IsApproved = true, Result = AttendanceResult.Normal, ReviewNote = "已补卡，人工确认正常"
            }, "admin");

            Assert.Equal(AttendanceResult.Normal, reviewed.Result);
            Assert.NotNull(reviewed.ReviewBy);
            Assert.Equal("已补卡，人工确认正常", reviewed.ReviewNote);
        }

        [Fact]
        public void ReviewAttendance_重复审核_抛ValidationFailed()
        {
            int employeeId = NewSchedulableEmployee("重复审核员工");
            PublishTodayShift(employeeId, "早班");
            var attendance = _service.RecordAttendance(new AttendanceRequest
            {
                EmployeeId = employeeId, WorkDate = DateTime.Today, CheckIn = "09:30", CheckOut = "16:05"
            });
            _service.ReviewAttendance(attendance.Id, new AttendanceReviewRequest { IsApproved = true }, "admin");

            var ex = Assert.Throws<ApiException>(() => _service.ReviewAttendance(attendance.Id,
                new AttendanceReviewRequest { IsApproved = true }, "admin"));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("已审核", ex.Message);
        }

        // ===================== BR-ORG-06 在岗状态是应急匹配的实时来源 =====================

        [Fact]
        public void ChangeEmployeeStatus_转离岗_应急责任匹配不再返回该员工()
        {
            int zhangQiang = TestData.EmployeeIdByName("张强");
            int sceneId = TestData.SceneIdByName("火灾");
            var emergency = new EmergencyService();
            Assert.Contains(emergency.MatchResponsible(sceneId), m => m.EmployeeId == zhangQiang);

            var dto = _service.ChangeEmployeeStatus(zhangQiang, new EmployeeStatusRequest { Status = EmployeeStatus.OffDuty });

            Assert.Equal(EmployeeStatus.OffDuty, dto.Status);
            Assert.DoesNotContain(emergency.MatchResponsible(sceneId), m => m.EmployeeId == zhangQiang);
        }

        [Fact]
        public void ChangeEmployeeStatus_员工离职_应急候选人名单排除该员工()
        {
            int employeeId = TestData.EmployeeIdByName("陈勇");
            int sceneId = TestData.SceneIdByName("火灾");
            var emergency = new EmergencyService();
            Assert.Contains(emergency.MatchResponsible(sceneId), m => m.EmployeeId == employeeId);

            _service.ResignEmployee(employeeId);

            Assert.DoesNotContain(emergency.MatchResponsible(sceneId), m => m.EmployeeId == employeeId);
        }

        // ---------- helpers ----------

        private int NewSchedulableEmployee(string name)
        {
            int dept = TestData.Department(name + "部门");
            int pos = TestData.Position(dept, "值班员");
            return TestData.Employee(dept, pos, name, "138" + Math.Abs(name.GetHashCode() % 100000000).ToString("D8"));
        }

        private void PublishTodayShift(int employeeId, string shiftName)
        {
            _service.GenerateSchedules(new ScheduleGenerateRequest
            {
                Items = new List<ScheduleRequest>
                {
                    new ScheduleRequest { EmployeeId = employeeId, ShiftId = TestData.ShiftId(shiftName), WorkDate = DateTime.Today }
                }
            });
            _service.PublishSchedules(new SchedulePublishRequest
            {
                FromDate = DateTime.Today, ToDate = DateTime.Today, Force = true
            });
        }
    }
}
