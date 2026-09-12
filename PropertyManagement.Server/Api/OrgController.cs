using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web.Http;
using Microsoft.Owin;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Org;
using PropertyManagement.Server.Api.Middleware;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>人员组织端点（M6 D6-2，UC-ORG-001~007 + BR-ORG-02~07）。</summary>
    [RoutePrefix("api/v1/org")]
    public class OrgController : ApiController
    {
        private readonly OrgService _service;

        public OrgController()
        {
            _service = new OrgService();
        }

        // ---------- 部门（UC-ORG-001） ----------
        [HttpGet] [Route("departments")]
        public ApiResponse<List<DepartmentDto>> ListDepartments(string keyword = null) =>
            ApiResponse<List<DepartmentDto>>.Ok(_service.ListDepartments(keyword));

        [HttpPost] [Route("departments")]
        public ApiResponse<DepartmentDto> CreateDepartment(DepartmentRequest request) =>
            ApiResponse<DepartmentDto>.Ok(_service.SaveDepartment(0, request));

        [HttpPut] [Route("departments/{id:int}")]
        public ApiResponse<DepartmentDto> UpdateDepartment(int id, DepartmentRequest request) =>
            ApiResponse<DepartmentDto>.Ok(_service.SaveDepartment(id, request));

        [HttpDelete] [Route("departments/{id:int}")]
        public ApiResponse<object> DeleteDepartment(int id) { _service.DeleteDepartment(id); return ApiResponse<object>.Ok(null); }

        // ---------- 岗位 ----------
        [HttpGet] [Route("positions")]
        public ApiResponse<List<PositionDto>> ListPositions(int? deptId = null) =>
            ApiResponse<List<PositionDto>>.Ok(_service.ListPositions(deptId));

        [HttpPost] [Route("positions")]
        public ApiResponse<PositionDto> CreatePosition(PositionRequest request) =>
            ApiResponse<PositionDto>.Ok(_service.SavePosition(0, request));

        [HttpPut] [Route("positions/{id:int}")]
        public ApiResponse<PositionDto> UpdatePosition(int id, PositionRequest request) =>
            ApiResponse<PositionDto>.Ok(_service.SavePosition(id, request));

        [HttpDelete] [Route("positions/{id:int}")]
        public ApiResponse<object> DeletePosition(int id) { _service.DeletePosition(id); return ApiResponse<object>.Ok(null); }

        // ---------- 员工（UC-ORG-002/005/006） ----------
        [HttpGet] [Route("employees")]
        public ApiResponse<PageResult<EmployeeDto>> QueryEmployees([FromUri] EmployeeQueryRequest request) =>
            ApiResponse<PageResult<EmployeeDto>>.Ok(_service.QueryEmployees(request ?? new EmployeeQueryRequest()));

        [HttpGet] [Route("employees/{id:int}")]
        public ApiResponse<EmployeeDto> GetEmployee(int id) => ApiResponse<EmployeeDto>.Ok(_service.GetEmployee(id));

        [HttpGet] [Route("employees/{id:int}/status-logs")]
        public ApiResponse<List<EmployeeStatusLogDto>> ListEmployeeStatusLogs(int id) =>
            ApiResponse<List<EmployeeStatusLogDto>>.Ok(_service.ListEmployeeStatusLogs(id));

        [HttpPost] [Route("employees")]
        public ApiResponse<EmployeeDto> CreateEmployee(EmployeeRequest request) =>
            ApiResponse<EmployeeDto>.Ok(_service.SaveEmployee(0, request));

        [HttpPut] [Route("employees/{id:int}")]
        public ApiResponse<EmployeeDto> UpdateEmployee(int id, EmployeeRequest request) =>
            ApiResponse<EmployeeDto>.Ok(_service.SaveEmployee(id, request));

        /// <summary>离职办理（BR-ORG-02）：档案保留 + 账号停用 + 电话簿条目联动停用；与 DELETE（删除档案）语义分离。</summary>
        [HttpPost] [Route("employees/{id:int}/resign")]
        public ApiResponse<EmployeeDto> ResignEmployee(int id) =>
            ApiResponse<EmployeeDto>.Ok(_service.ResignEmployee(id));

        /// <summary>删除员工档案（主管场景，del_flag=1）：仅允许对已离职员工执行。</summary>
        [HttpDelete] [Route("employees/{id:int}")]
        public ApiResponse<object> DeleteEmployee(int id) { _service.DeleteEmployee(id); return ApiResponse<object>.Ok(null); }

        [HttpPost] [Route("employees/{id:int}/status")]
        public ApiResponse<EmployeeDto> ChangeEmployeeStatus(int id, EmployeeStatusRequest request) =>
            ApiResponse<EmployeeDto>.Ok(_service.ChangeEmployeeStatus(id, request ?? new EmployeeStatusRequest { Status = EmployeeStatus.Active }));

        // ---------- 班次 ----------
        [HttpGet] [Route("shifts")]
        public ApiResponse<List<ShiftDto>> ListShifts() => ApiResponse<List<ShiftDto>>.Ok(_service.ListShifts());

        [HttpPost] [Route("shifts")]
        public ApiResponse<ShiftDto> CreateShift(ShiftRequest request) => ApiResponse<ShiftDto>.Ok(_service.SaveShift(0, request));

        [HttpPut] [Route("shifts/{id:int}")]
        public ApiResponse<ShiftDto> UpdateShift(int id, ShiftRequest request) => ApiResponse<ShiftDto>.Ok(_service.SaveShift(id, request));

        [HttpDelete] [Route("shifts/{id:int}")]
        public ApiResponse<object> DeleteShift(int id) { _service.DeleteShift(id); return ApiResponse<object>.Ok(null); }

        // ---------- 排班（UC-ORG-003，BR-ORG-03/04） ----------
        [HttpGet] [Route("schedules")]
        public ApiResponse<List<ScheduleDto>> QuerySchedules(DateTime? from, DateTime? to, int? employeeId = null)
        {
            DateTime fromDate = from ?? DateTime.Today;
            DateTime toDate = to ?? fromDate.AddDays(6);
            return ApiResponse<List<ScheduleDto>>.Ok(_service.QuerySchedules(fromDate, toDate, employeeId));
        }

        /// <summary>自动排班/批量保存排班：Items 为空时按周轮转自动生成草稿（返回冲突/缺员与推荐补班人选）。</summary>
        [HttpPost] [Route("schedules/generate")]
        public ApiResponse<SchedulePlanDto> GenerateSchedules(ScheduleGenerateRequest request) =>
            ApiResponse<SchedulePlanDto>.Ok(_service.GenerateSchedules(request));

        /// <summary>排班发布：双班冲突一律阻断（409）；缺员默认阻断，Force=true 强制标记发布（写缺员日志）。</summary>
        [HttpPost] [Route("schedules/publish")]
        public ApiResponse<SchedulePlanDto> PublishSchedules(SchedulePublishRequest request) =>
            ApiResponse<SchedulePlanDto>.Ok(_service.PublishSchedules(request));

        [HttpDelete] [Route("schedules/{id:int}")]
        public ApiResponse<object> DeleteSchedule(int id) { _service.DeleteSchedule(id); return ApiResponse<object>.Ok(null); }

        /// <summary>批量删除排班：按日期区间删除（可限定班次），返回删除条数。</summary>
        [HttpPost] [Route("schedules/batch-delete")]
        public ApiResponse<int> BatchDeleteSchedules(ScheduleBatchDeleteRequest request) =>
            ApiResponse<int>.Ok(_service.BatchDeleteSchedules(request));

        // ---------- 排班模板 ----------
        [HttpGet] [Route("schedule-templates")]
        public ApiResponse<List<ScheduleTemplateDto>> ListScheduleTemplates() =>
            ApiResponse<List<ScheduleTemplateDto>>.Ok(_service.ListScheduleTemplates());

        [HttpPost] [Route("schedule-templates")]
        public ApiResponse<ScheduleTemplateDto> SaveScheduleTemplate(ScheduleTemplateSaveRequest request) =>
            ApiResponse<ScheduleTemplateDto>.Ok(_service.SaveScheduleTemplate(request));

        [HttpGet] [Route("schedule-templates/{id:int}")]
        public ApiResponse<ScheduleTemplateDto> GetScheduleTemplate(int id) =>
            ApiResponse<ScheduleTemplateDto>.Ok(_service.GetScheduleTemplate(id));

        [HttpDelete] [Route("schedule-templates/{id:int}")]
        public ApiResponse<object> DeleteScheduleTemplate(int id) { _service.DeleteScheduleTemplate(id); return ApiResponse<object>.Ok(null); }

        [HttpGet] [Route("schedules/conflicts")]
        public ApiResponse<List<ScheduleConflictLogDto>> ListScheduleConflicts(DateTime? from, DateTime? to)
        {
            DateTime fromDate = from ?? DateTime.Today;
            DateTime toDate = to ?? fromDate.AddDays(6);
            return ApiResponse<List<ScheduleConflictLogDto>>.Ok(_service.ListScheduleConflicts(fromDate, toDate));
        }

        // ---------- 考勤（UC-ORG-004，BR-ORG-05） ----------
        [HttpGet] [Route("attendance")]
        public ApiResponse<PageResult<AttendanceDto>> QueryAttendance([FromUri] AttendanceQueryRequest request) =>
            ApiResponse<PageResult<AttendanceDto>>.Ok(_service.QueryAttendance(request ?? new AttendanceQueryRequest()));

        /// <summary>考勤统计卡（PG-ORG-03）：出勤率/迟到人次/缺卡人次/待审核补卡数。</summary>
        [HttpGet] [Route("attendance/summary")]
        public ApiResponse<AttendanceSummaryDto> AttendanceSummary(int year = 0, int month = 0, int? deptId = null) =>
            ApiResponse<AttendanceSummaryDto>.Ok(_service.AttendanceSummary(year, month, deptId));

        /// <summary>考勤月报导出（T6-2-5）：返回 CSV 文件（UTF-8 BOM，Excel 可直接打开）。</summary>
        [HttpGet] [Route("attendance/export")]
        public HttpResponseMessage ExportAttendance(int year = 0, int month = 0, int? deptId = null)
        {
            if (year <= 0) { year = DateTime.Today.Year; }
            if (month <= 0 || month > 12) { month = DateTime.Today.Month; }
            string csv = _service.ExportAttendanceCsv(year, month, deptId);
            byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            HttpResponseMessage response = Request.CreateResponse(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(bytes);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "考勤月报_" + year.ToString("0000") + "-" + month.ToString("00") + ".csv"
            };
            return response;
        }

        [HttpPost] [Route("attendance")]
        public ApiResponse<AttendanceDto> RecordAttendance(AttendanceRequest request) =>
            ApiResponse<AttendanceDto>.Ok(_service.RecordAttendance(request));

        [HttpPost] [Route("attendance/{id:int}/review")]
        public ApiResponse<AttendanceDto> ReviewAttendance(int id, AttendanceReviewRequest request) =>
            ApiResponse<AttendanceDto>.Ok(_service.ReviewAttendance(id, request ?? new AttendanceReviewRequest(), GetCurrentUsername()));

        /// <summary>批量审核考勤（页面工具栏「批量审核」）：选中异常/待补卡记录一键通过闭环，返回审核条数。</summary>
        [HttpPost] [Route("attendance/batch-review")]
        public ApiResponse<int> BatchReviewAttendance(AttendanceBatchReviewRequest request) =>
            ApiResponse<int>.Ok(_service.BatchReviewAttendance(request, GetCurrentUsername()));

        /// <summary>从 WebApi 注入的 OWIN 环境读取鉴权中间件写入的用户名。</summary>
        private string GetCurrentUsername()
        {
            object owinContextValue;
            if (Request.Properties.TryGetValue("MS_OwinContext", out owinContextValue))
            {
                var owinContext = owinContextValue as IOwinContext;
                if (owinContext != null)
                {
                    return owinContext.Get<string>(AuthMiddleware.UsernameEnvKey) ?? string.Empty;
                }
            }
            return string.Empty;
        }
    }
}
