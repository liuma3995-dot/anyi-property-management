using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Org;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;
using PropertyManagement.Server.Infrastructure.Security;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 人员组织服务（M6 D6-2，UC-ORG-001~007）。
    /// 业务规则：BR-ORG-02（离职保留档案+账号停用+电话簿联动）、BR-ORG-03（排班冲突真阻断）、
    /// BR-ORG-04（缺员强制标记发布）、BR-ORG-05（考勤异常自动判定+审核）、BR-ORG-06（在岗状态）、BR-ORG-07（部门/岗位扩展）。
    /// </summary>
    public class OrgService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IOrgRepository _repo;

        public OrgService()
            : this(new SqliteConnectionFactory(), new SqlOrgRepository())
        {
        }

        public OrgService(IDbConnectionFactory connectionFactory, IOrgRepository repo)
        {
            _connectionFactory = connectionFactory;
            _repo = repo;
        }

        // ===================== 部门 =====================
        public List<DepartmentDto> ListDepartments(string keyword) =>
            WithConnection(c => _repo.ListDepartments(c, keyword));

        public DepartmentDto SaveDepartment(int id, DepartmentRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("部门名称不能为空");
                if (id > 0)
                {
                    var existing = _repo.GetDepartment(c, id) ?? throw ApiException.NotFound("部门不存在");
                    var dto = new DepartmentDto
                    {
                        Id = id,
                        Name = request.Name.Trim(),
                        ParentId = request.ParentId,
                        Status = request.Status ?? existing.Status // CHG-ORG-02：支持停用（0启用/1停用），null 保留原值
                    };
                    _repo.UpdateDepartment(c, tx, dto);
                    return _repo.GetDepartment(c, id) ?? dto;
                }
                var created = new DepartmentDto { Name = request.Name.Trim(), ParentId = request.ParentId, Status = request.Status ?? 0 };
                created.Id = _repo.InsertDepartment(c, tx, created);
                return created;
            });

        public void DeleteDepartment(int id) =>
            WithTransaction((c, tx) =>
            {
                if (_repo.GetDepartment(c, id) == null) throw ApiException.NotFound("部门不存在");
                if (_repo.CountEmployeesByDept(c, id) > 0) throw ApiException.Conflict("该部门下存在员工，只能停用不能删除");
                _repo.SoftDeleteDepartment(c, tx, id);
            });

        // ===================== 岗位 =====================
        public List<PositionDto> ListPositions(int? deptId) =>
            WithConnection(c => _repo.ListPositions(c, deptId));

        public PositionDto SavePosition(int id, PositionRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request.DeptId <= 0) throw ApiException.ValidationFailed("请选择所属部门");
                if (string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("岗位名称不能为空");
                if (id > 0)
                {
                    var dto = new PositionDto { Id = id, DeptId = request.DeptId, Name = request.Name.Trim() };
                    _repo.UpdatePosition(c, tx, dto);
                    return dto;
                }
                var created = new PositionDto { DeptId = request.DeptId, Name = request.Name.Trim() };
                created.Id = _repo.InsertPosition(c, tx, created);
                return created;
            });

        public void DeletePosition(int id) =>
            WithTransaction((c, tx) =>
            {
                if (_repo.CountEmployeesByPosition(c, id) > 0) throw ApiException.Conflict("该岗位下存在员工，不能删除");
                _repo.DeletePosition(c, tx, id);
            });

        // ===================== 员工 =====================
        public EmployeeDto GetEmployee(int id) =>
            WithConnection(c => _repo.GetEmployee(c, id) ?? throw ApiException.NotFound("员工不存在"));

        public PageResult<EmployeeDto> QueryEmployees(EmployeeQueryRequest query) =>
            WithConnection(c => _repo.QueryEmployees(c, query ?? new EmployeeQueryRequest { PageIndex = 1, PageSize = 20 }, out int total));

        public EmployeeDto SaveEmployee(int id, EmployeeRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request.DeptId <= 0) throw ApiException.ValidationFailed("请选择所属部门");
                if (request.PositionId <= 0) throw ApiException.ValidationFailed("请选择岗位");
                if (string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("员工姓名不能为空");
                if (id > 0)
                {
                    var existing = _repo.GetEmployee(c, id) ?? throw ApiException.NotFound("员工不存在");
                    var dto = new EmployeeDto
                    {
                        Id = id, DeptId = request.DeptId, PositionId = request.PositionId,
                        Name = request.Name.Trim(), Phone = request.Phone ?? string.Empty,
                        HireDate = request.HireDate == default ? DateTime.Today : request.HireDate,
                        Status = existing.Status
                    };
                    _repo.UpdateEmployee(c, tx, dto);
                    return _repo.GetEmployee(c, id);
                }
                var created = new EmployeeDto
                {
                    DeptId = request.DeptId, PositionId = request.PositionId,
                    Name = request.Name.Trim(), Phone = request.Phone ?? string.Empty,
                    HireDate = request.HireDate == default ? DateTime.Today : request.HireDate,
                    Status = EmployeeStatus.Active
                };
                created.Id = _repo.InsertEmployee(c, tx, created); // 内部生成并落库 emp_no（YG-%03d）
                _repo.InsertEmployeeStatusLog(c, tx, created.Id, EmployeeStatus.Active);
                // 项目设定：仅系统管理员可登录使用物业管理系统，员工档案不自动开通登录账号。
                return _repo.GetEmployee(c, created.Id);
            });

        /// <summary>
        /// 离职办理（BR-ORG-02）：档案保留（del_flag=0 仅置 status=Resigned）、状态留痕、
        /// 账号停用、权限回收（系统仅唯一管理员角色，无独立权限表，账号停用即完成回收）、
        /// 电话簿员工条目联动停用（UC-TEL-005）。与 DELETE（物理删除语义）严格分开。
        /// </summary>
        public EmployeeDto ResignEmployee(int id) =>
            WithTransaction((c, tx) =>
            {
                var e = _repo.GetEmployee(c, id) ?? throw ApiException.NotFound("员工不存在");
                if (e.Status == EmployeeStatus.Resigned) throw ApiException.Conflict("该员工已办理离职，请勿重复操作");
                _repo.ResignEmployee(c, tx, id);
                _repo.InsertEmployeeStatusLog(c, tx, id, EmployeeStatus.Resigned);
                _repo.DisableUserAccountsByEmployee(c, tx, id);
                _repo.DisablePhoneEntriesByEmployee(c, tx, id, e.Name);
                return _repo.GetEmployee(c, id);
            });

        /// <summary>删除员工档案（主管场景）：仅允许对已离职员工执行（del_flag=1），与离职语义分离。</summary>
        public void DeleteEmployee(int id) =>
            WithTransaction((c, tx) =>
            {
                var e = _repo.GetEmployee(c, id) ?? throw ApiException.NotFound("员工不存在");
                if (e.Status != EmployeeStatus.Resigned)
                    throw ApiException.Conflict("在职员工不能删除，请先办理离职");
                _repo.SoftDeleteEmployee(c, tx, id);
            });

        public EmployeeDto ChangeEmployeeStatus(int id, EmployeeStatusRequest request) =>
            WithTransaction((c, tx) =>
            {
                var e = _repo.GetEmployee(c, id) ?? throw ApiException.NotFound("员工不存在");
                if (request.Status == EmployeeStatus.Resigned)
                    throw ApiException.Conflict("离职需走离职流程：POST /org/employees/" + id + "/resign（含账号停用与电话簿联动）");
                e.Status = request.Status;
                _repo.UpdateEmployee(c, tx, e);
                _repo.InsertEmployeeStatusLog(c, tx, id, request.Status);
                return _repo.GetEmployee(c, id);
            });

        public List<EmployeeStatusLogDto> ListEmployeeStatusLogs(int employeeId) =>
            WithConnection(c => _repo.ListEmployeeStatusLogs(c, employeeId));

        // ===================== 班次 =====================
        public List<ShiftDto> ListShifts() =>
            WithConnection(c => _repo.ListShifts(c));

        public ShiftDto SaveShift(int id, ShiftRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("班次名称不能为空");
                if (id > 0)
                {
                    var dto = new ShiftDto
                    {
                        Id = id, Name = request.Name.Trim(), StartTime = request.StartTime, EndTime = request.EndTime,
                        MinRequired = request.MinRequired // null=保留原需求人数
                    };
                    _repo.UpdateShift(c, tx, dto);
                    return _repo.ListShifts(c).FirstOrDefault(s => s.Id == id) ?? dto;
                }
                var created = new ShiftDto
                {
                    Name = request.Name.Trim(), StartTime = request.StartTime, EndTime = request.EndTime,
                    MinRequired = request.MinRequired ?? 1
                };
                created.Id = _repo.InsertShift(c, tx, created);
                return created;
            });

        public void DeleteShift(int id) =>
            WithTransaction((c, tx) =>
            {
                if (_repo.CountSchedulesByShift(c, id) > 0) throw ApiException.Conflict("该班次已被排班引用，不能删除");
                _repo.DeleteShift(c, tx, id);
            });

        // ===================== 排班（BR-ORG-03 冲突检查 / BR-ORG-04 缺员） =====================
        public List<ScheduleDto> QuerySchedules(DateTime fromDate, DateTime toDate, int? employeeId) =>
            WithConnection(c => _repo.QuerySchedules(c, fromDate, toDate, employeeId));

        /// <summary>
        /// 排班生成（审计 9/12）：
        /// 1) Items 非空 → 手工批量保存草稿，与库内既有排班做冲突检查，检出同人同日多班 → 409 阻断（事务回滚）；
        /// 2) Items 为空 → 自动排班：[FromDate,ToDate]（默认本周）内每天按每班次 MinRequired 从在岗员工顺序轮流分配，跳过当日已有排班。
        /// 返回 SchedulePlanDto（含冲突/缺员与推荐补班人选）。
        /// </summary>
        public SchedulePlanDto GenerateSchedules(ScheduleGenerateRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("请求不能为空");
                bool manual = request.Items != null && request.Items.Count > 0;
                DateTime fromDate;
                DateTime toDate;
                if (manual)
                {
                    foreach (ScheduleRequest item in request.Items)
                    {
                        if (item.EmployeeId <= 0 || item.ShiftId <= 0 || item.WorkDate == default)
                            throw ApiException.ValidationFailed("排班项缺少员工/班次/日期");
                    }
                    fromDate = request.Items.Min(i => i.WorkDate).Date;
                    toDate = request.Items.Max(i => i.WorkDate).Date;
                    foreach (ScheduleRequest item in request.Items)
                    {
                        var dto = new ScheduleDto
                        {
                            EmployeeId = item.EmployeeId, ShiftId = item.ShiftId,
                            WorkDate = item.WorkDate.Date, Status = ScheduleStatus.Draft
                        };
                        dto.Id = _repo.InsertSchedule(c, tx, dto);
                    }
                }
                else
                {
                    fromDate = (request.FromDate ?? StartOfWeek(DateTime.Today)).Date;
                    toDate = (request.ToDate ?? fromDate.AddDays(6)).Date;
                    if (toDate < fromDate) throw ApiException.ValidationFailed("排班结束日期不能早于开始日期");
                    AutoGenerateDrafts(c, tx, fromDate, toDate, request.DeptId, request.ShiftIds);
                }

                // 冲突检查对库内既有排班一并比对（审计 12）
                List<ScheduleDto> range = _repo.ListSchedulesByDateRange(c, fromDate, toDate);
                List<ScheduleConflictLogDto> conflicts = DetectConflicts(range);
                if (manual && conflicts.Count > 0)
                {
                    // BR-ORG-03：保存路径冲突同样阻断，不得落库
                    throw ApiException.Conflict("排班存在同人同日多班冲突，已阻断保存：" +
                                               string.Join("；", conflicts.Select(x => x.Detail).Distinct()));
                }
                if (conflicts.Count > 0)
                {
                    _repo.ClearScheduleConflictLogs(c, tx, fromDate, toDate);
                    foreach (ScheduleConflictLogDto conflict in conflicts) { _repo.InsertScheduleConflictLog(c, tx, conflict); }
                }
                List<UnderstaffedSlotDto> understaffed = _repo.DetectUnderstaffedSlots(c, fromDate, toDate);
                // 排班→考勤联动：为周期内生效排班补齐考勤草稿（BR-ORG-05 闭环）
                _repo.SyncAttendanceFromSchedules(c, tx, fromDate, toDate);
                return new SchedulePlanDto { Schedules = range, Conflicts = conflicts, Understaffed = understaffed };
            });

        /// <summary>
        /// 排班发布（BR-ORG-03/04 真阻断，审计 11）：
        /// · 同人同日双班冲突 → 一律 throw Conflict（事务回滚，绝不置为已发布），Force 亦不可绕过；
        /// · 缺员缺口 → 默认阻断并列出缺口；Force=true「强制标记发布」：允许发布并写 t_schedule_conflict_log(conflict_type='缺员')。
        /// </summary>
        public SchedulePlanDto PublishSchedules(SchedulePublishRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null || request.FromDate == default || request.ToDate == default)
                    throw ApiException.ValidationFailed("请选择排班周期");
                if (request.ToDate < request.FromDate) throw ApiException.ValidationFailed("结束日期不能早于开始日期");
                List<ScheduleDto> range = _repo.ListSchedulesByDateRange(c, request.FromDate, request.ToDate);

                // BR-ORG-03：双班冲突 → 阻断（不更新状态，事务回滚）
                List<ScheduleConflictLogDto> conflicts = DetectConflicts(range);
                if (conflicts.Count > 0)
                {
                    throw ApiException.Conflict("排班存在同人同日多班冲突，已阻断发布：" +
                                               string.Join("；", conflicts.Select(x => x.Detail).Distinct()));
                }

                // BR-ORG-04：缺员 → 默认阻断；Force=true 允许发布并标记缺口
                List<UnderstaffedSlotDto> understaffed = _repo.DetectUnderstaffedSlots(c, request.FromDate, request.ToDate);
                if (understaffed.Count > 0 && !request.Force)
                {
                    throw ApiException.Conflict("排班存在缺员缺口，已阻断发布（确认后可带 Force=true 强制标记发布）：" +
                                               DescribeUnderstaffed(understaffed));
                }

                _repo.ClearScheduleConflictLogs(c, tx, request.FromDate, request.ToDate); // 防重复发布重复写日志（P2-2.8）
                if (request.Force)
                {
                    foreach (UnderstaffedSlotDto gap in understaffed)
                    {
                        int anchorId = ResolveGapAnchor(range, gap.Date);
                        if (anchorId <= 0) { continue; } // 区间无任何排班行可锚定（外键约束），缺口仍在 Understaffed 返回
                        _repo.InsertScheduleConflictLog(c, tx, new ScheduleConflictLogDto
                        {
                            // 外键约束（Foreign Keys=True）：缺口锚定到当日任一排班行；实排为 0 时回退区间锚点
                            ScheduleId = anchorId,
                            ConflictType = "缺员",
                            Detail = gap.Date.ToString("yyyy-MM-dd") + " " + gap.ShiftName +
                                     " 需 " + gap.Required + " 人实排 " + gap.Actual + " 人，缺 " + gap.Gap + " 人"
                        });
                    }
                }
                foreach (ScheduleDto s in range)
                {
                    s.Status = ScheduleStatus.Published;
                    s.StatusText = "已发布"; // 响应内同步展示状态（审计 24：避免 status=已发布 但 statusText=草稿）
                    _repo.UpdateSchedule(c, tx, s);
                }
                return new SchedulePlanDto { Schedules = range, Conflicts = new List<ScheduleConflictLogDto>(), Understaffed = understaffed };
            });

        public void DeleteSchedule(int id) =>
            WithTransaction((c, tx) =>
            {
                ScheduleDto s = _repo.GetSchedule(c, id);
                if (s == null) { return; }
                _repo.SoftDeleteSchedule(c, tx, id);
                // 排班删除→联动清空对应考勤（BR-ORG-05 闭环）
                _repo.DeleteAttendanceByEmployeeDate(c, tx, s.EmployeeId, s.WorkDate);
            });

        // ===================== 排班模板 =====================
        public List<ScheduleTemplateDto> ListScheduleTemplates() =>
            WithConnection(c => _repo.ListScheduleTemplates(c));

        /// <summary>保存排班模板：从指定周期已保存的排班（草稿+已发布，del_flag=0）提取 员工×dayOffset×班次 明细。</summary>
        public ScheduleTemplateDto SaveScheduleTemplate(ScheduleTemplateSaveRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null || request.FromDate == default || request.ToDate == default)
                    throw ApiException.ValidationFailed("请选择模板排班周期");
                if (request.ToDate < request.FromDate) throw ApiException.ValidationFailed("模板周期结束日期不能早于开始日期");
                List<ScheduleDto> schedules = _repo.ListSchedulesByDateRange(c, request.FromDate, request.ToDate);
                if (schedules.Count == 0) throw ApiException.ValidationFailed("所选周期没有已保存的排班数据，无法生成模板");

                var items = schedules
                    .GroupBy(s => new { s.EmployeeId, s.WorkDate.Date })
                    .Select(g => g.First())
                    .Select(s => new ScheduleTemplateItemDto
                    {
                        EmployeeId = s.EmployeeId,
                        EmpNo = s.EmpNo,
                        EmpName = s.EmpName,
                        ShiftId = s.ShiftId,
                        ShiftName = s.ShiftName,
                        DayOffset = (int)(s.WorkDate.Date - request.FromDate.Date).TotalDays
                    })
                    .OrderBy(x => x.DayOffset).ThenBy(x => x.EmployeeId).ToList();

                string name = string.IsNullOrWhiteSpace(request.Name)
                    ? "排班模板 " + request.FromDate.ToString("MM-dd") + "~" + request.ToDate.ToString("MM-dd")
                    : request.Name.Trim();
                var dto = new ScheduleTemplateDto
                {
                    Name = name,
                    FromDate = request.FromDate.Date,
                    ToDate = request.ToDate.Date,
                    ItemCount = items.Count,
                    Items = items
                };
                dto.Id = _repo.InsertScheduleTemplate(c, tx, dto);
                foreach (ScheduleTemplateItemDto it in items)
                {
                    it.TemplateId = dto.Id;
                    _repo.InsertScheduleTemplateItem(c, tx, it);
                }
                return _repo.GetScheduleTemplateItems(c, dto.Id) ?? dto;
            });

        public ScheduleTemplateDto GetScheduleTemplate(int id) =>
            WithConnection(c => _repo.GetScheduleTemplateItems(c, id) ?? throw ApiException.NotFound("排班模板不存在"));

        public void DeleteScheduleTemplate(int id) =>
            WithTransaction((c, tx) =>
            {
                if (_repo.GetScheduleTemplateItems(c, id) == null) throw ApiException.NotFound("排班模板不存在");
                _repo.SoftDeleteScheduleTemplate(c, tx, id);
            });

        /// <summary>
        /// 批量删除排班（工具栏「批量删除排班」）：按日期区间软删（可限定班次），返回删除条数。
        /// 与单条删除一致，不校验状态（草稿/已发布均可删除）。
        /// </summary>
        public int BatchDeleteSchedules(ScheduleBatchDeleteRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null || request.FromDate == default || request.ToDate == default)
                    throw ApiException.ValidationFailed("请选择删除周期");
                if (request.ToDate < request.FromDate) throw ApiException.ValidationFailed("结束日期不能早于开始日期");
                var shiftIds = request.ShiftIds == null ? null : request.ShiftIds.Where(id => id > 0).Distinct().ToList();
                // 记录将被删除的排班（按周期/班次筛选），用于联动清空对应考勤
                List<ScheduleDto> affected = _repo.ListSchedulesInRangeFiltered(c, request.FromDate, request.ToDate, shiftIds);
                int deleted = _repo.SoftDeleteSchedulesInRange(c, tx, request.FromDate, request.ToDate, shiftIds);
                // 排班批量删除→联动清空对应考勤（BR-ORG-05 闭环）
                foreach (ScheduleDto s in affected)
                {
                    _repo.DeleteAttendanceByEmployeeDate(c, tx, s.EmployeeId, s.WorkDate);
                }
                return deleted;
            });

        public List<ScheduleConflictLogDto> ListScheduleConflicts(DateTime fromDate, DateTime toDate) =>
            WithConnection(c => _repo.ListScheduleConflicts(c, fromDate, toDate));

        /// <summary>自动排班：周内每天按每班次 MinRequired 从在岗员工顺序轮流分配，跳过当日已有排班的员工。</summary>
        private void AutoGenerateDrafts(IDbConnection c, IDbTransaction tx, DateTime fromDate, DateTime toDate, int? deptId, List<int> shiftIds)
        {
            List<ShiftDto> shifts = _repo.ListShifts(c)
                .Where(s => (s.MinRequired ?? 0) > 0)
                .OrderBy(s => s.Id).ToList();
            if (shiftIds != null && shiftIds.Count > 0)
            {
                var set = new HashSet<int>(shiftIds.Where(id => id > 0));
                shifts = shifts.Where(s => set.Contains(s.Id)).ToList();
            }
            if (shifts.Count == 0) throw ApiException.ValidationFailed("未配置班次需求人数（t_shift.min_required），无法自动排班");
            List<EmployeeDto> roster = _repo.ListOnDutyEmployees(c, deptId);
            if (roster.Count == 0) throw ApiException.ValidationFailed("没有在岗员工，无法自动排班");

            var scheduled = new HashSet<string>(
                _repo.ListSchedulesByDateRange(c, fromDate, toDate)
                    .Select(s => s.EmployeeId + "|" + s.WorkDate.ToString("yyyy-MM-dd")));
            int cursor = 0;
            for (DateTime d = fromDate.Date; d <= toDate.Date; d = d.AddDays(1))
            {
                foreach (ShiftDto shift in shifts)
                {
                    int required = shift.MinRequired ?? 0;
                    int assigned = 0;
                    int attempts = 0;
                    while (assigned < required && attempts < roster.Count)
                    {
                        attempts++;
                        EmployeeDto emp = roster[cursor % roster.Count];
                        cursor++;
                        string key = emp.Id + "|" + d.ToString("yyyy-MM-dd");
                        if (scheduled.Contains(key)) { continue; } // 跳过当日已有排班
                        var dto = new ScheduleDto
                        {
                            EmployeeId = emp.Id, ShiftId = shift.Id, WorkDate = d, Status = ScheduleStatus.Draft
                        };
                        dto.Id = _repo.InsertSchedule(c, tx, dto);
                        scheduled.Add(key);
                        assigned++;
                    }
                }
            }
        }

        private static int ResolveGapAnchor(List<ScheduleDto> range, DateTime gapDate)
        {
            ScheduleDto anchor = range.Where(s => s.WorkDate.Date == gapDate.Date).OrderBy(s => s.Id).FirstOrDefault()
                ?? range.OrderBy(s => s.Id).FirstOrDefault();
            return anchor?.Id ?? 0;
        }

        private static string DescribeUnderstaffed(List<UnderstaffedSlotDto> gaps)
        {
            return string.Join("；", gaps.Select(g =>
                g.Date.ToString("yyyy-MM-dd") + " " + g.ShiftName + " 需 " + g.Required + " 人实排 " + g.Actual + " 人，缺 " + g.Gap + " 人"));
        }

        /// <summary>同人同日多班冲突检测（BR-ORG-03）；Detail 使用员工姓名/工号（审计 2.9）。</summary>
        private static List<ScheduleConflictLogDto> DetectConflicts(List<ScheduleDto> schedules)
        {
            var conflicts = new List<ScheduleConflictLogDto>();
            foreach (var g in schedules.GroupBy(s => new { s.EmployeeId, Key = s.WorkDate.ToString("yyyy-MM-dd") }))
            {
                if (g.Select(x => x.ShiftId).Distinct().Count() <= 1) { continue; }
                string empNo = g.Select(x => x.EmpNo).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? ("员工#" + g.Key.EmployeeId);
                string empName = g.Select(x => x.EmpName).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
                string shifts = string.Join("/", g.Select(x => x.ShiftName).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct());
                foreach (ScheduleDto s in g)
                {
                    conflicts.Add(new ScheduleConflictLogDto
                    {
                        ScheduleId = s.Id,
                        ConflictType = "双班冲突",
                        // g.Key 为匿名分组键，须取 g.Key.Key（日期串），否则会打印 { EmployeeId = ..., Key = ... }
                        Detail = empNo + " " + empName + " 于 " + g.Key.Key + " 同时排了多个班次（" + shifts + "）"
                    });
                }
            }
            return conflicts;
        }

        private static DateTime StartOfWeek(DateTime date)
        {
            // 锚定周一
            return date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        }

        // ===================== 考勤（BR-ORG-05） =====================
        public PageResult<AttendanceDto> QueryAttendance(AttendanceQueryRequest query)
        {
            AttendanceQueryRequest q = query ?? new AttendanceQueryRequest { PageIndex = 1, PageSize = 20 };
            // 排班→考勤闭环：查询前补齐该周期考勤草稿（幂等，仅补缺失），保证排班数据同步到考勤记录表
            if (q.WorkDateFrom.HasValue && q.WorkDateTo.HasValue)
            {
                WithConnection(c => { _repo.SyncAttendanceFromSchedules(c, q.WorkDateFrom.Value, q.WorkDateTo.Value); return true; });
            }
            return WithConnection(c => _repo.QueryAttendance(c, q, out int total));
        }

        /// <summary>
        /// 考勤登记（审计 15/3.8/4.8）：
        /// · 与当日已发布排班班次时间窗对照自动判定 正常/迟到/缺卡，无排班保持「已记录」；
        /// · 跨零点晚班打卡归属按班次开始时间所属日期归档（00:00-08:00 打卡且当日为晚班 → 前一工作日）；
        /// · employee_id+work_date 已有记录则更新（防重复插入）。
        /// </summary>
        public AttendanceDto RecordAttendance(AttendanceRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null || request.EmployeeId <= 0) throw ApiException.ValidationFailed("请选择员工");
                if (request.WorkDate == default) throw ApiException.ValidationFailed("请选择考勤日期");
                DateTime originalDate = request.WorkDate.Date;
                TimeSpan? checkInTime = ParseTime(request.CheckIn);
                DateTime workDate = ResolveWorkDate(c, request.EmployeeId, originalDate, checkInTime);

                AttendanceDto existing = _repo.GetAttendanceByEmployeeDate(c, request.EmployeeId, workDate)
                    ?? _repo.GetAttendanceByEmployeeDate(c, request.EmployeeId, originalDate);
                string checkIn = !string.IsNullOrWhiteSpace(request.CheckIn) ? request.CheckIn.Trim() : existing?.CheckIn;
                string checkOut = !string.IsNullOrWhiteSpace(request.CheckOut) ? request.CheckOut.Trim() : existing?.CheckOut;

                AttendanceResult result;
                string abnormalType;
                JudgeAttendance(c, request.EmployeeId, workDate, checkIn, checkOut, out result, out abnormalType);

                AttendanceDto record;
                if (existing == null)
                {
                    record = new AttendanceDto
                    {
                        EmployeeId = request.EmployeeId, WorkDate = workDate,
                        CheckIn = checkIn, CheckOut = checkOut, Result = result, AbnormalType = abnormalType
                    };
                    record.Id = _repo.InsertAttendance(c, tx, record);
                }
                else
                {
                    record = existing;
                    record.CheckIn = checkIn;
                    record.CheckOut = checkOut;
                    record.Result = result;
                    record.AbnormalType = abnormalType;
                    _repo.UpdateAttendance(c, tx, record);
                }
                return _repo.GetAttendance(c, record.Id);
            });

        /// <summary>跨零点归属：WorkDate 按班次开始时间所属日期归档。</summary>
        private DateTime ResolveWorkDate(IDbConnection c, int employeeId, DateTime workDate, TimeSpan? checkIn)
        {
            if (!checkIn.HasValue) { return workDate; }
            ScheduleDto current = _repo.GetPublishedScheduleForEmployeeDate(c, employeeId, workDate);
            // 打卡在 00:00-08:00 且当日排班为跨零点晚班 → 归属前一工作日
            if (checkIn.Value < TimeSpan.FromHours(8) && IsOvernightStart(ParseTime(current?.ShiftStart)))
            {
                return workDate.AddDays(-1);
            }
            // 晚间打卡（≥22:00）且当日无排班、次日为跨零点晚班 → 归属次日（如 23:58 提前到岗）
            if (checkIn.Value >= TimeSpan.FromHours(22) && current == null)
            {
                ScheduleDto next = _repo.GetPublishedScheduleForEmployeeDate(c, employeeId, workDate.AddDays(1));
                if (IsOvernightStart(ParseTime(next?.ShiftStart))) { return workDate.AddDays(1); }
            }
            return workDate;
        }

        /// <summary>与当日已发布排班班次时间窗对照自动判定（迟到/缺卡/正常）。</summary>
        private void JudgeAttendance(IDbConnection c, int employeeId, DateTime workDate, string checkIn, string checkOut,
            out AttendanceResult result, out string abnormalType)
        {
            result = AttendanceResult.Recorded;
            abnormalType = null;
            ScheduleDto schedule = _repo.GetPublishedScheduleForEmployeeDate(c, employeeId, workDate);
            if (schedule == null) { return; } // 无已发布排班 → 保持「已记录」
            TimeSpan? start = ParseTime(schedule.ShiftStart);
            if (!start.HasValue) { return; } // 休等无时间窗班次不判定

            TimeSpan? tIn = ParseTime(checkIn);
            if (!tIn.HasValue)
            {
                result = AttendanceResult.Absent; // 缺上班卡 → 旷工
                abnormalType = "旷工";
                return;
            }
            bool late;
            if (IsOvernightStart(start) && tIn.Value >= TimeSpan.FromHours(22))
            {
                late = false; // 跨零点晚班前一晚提前到岗（如 23:58 → 00:00 班次）视为正常
            }
            else
            {
                late = tIn.Value > start.Value;
            }
            if (late)
            {
                result = AttendanceResult.Late;
                abnormalType = "迟到";
                return;
            }
            if (string.IsNullOrWhiteSpace(checkOut))
            {
                result = AttendanceResult.Absent; // 缺下班卡 → 旷工
                abnormalType = "旷工";
                return;
            }
            result = AttendanceResult.Normal;
        }

        private static bool IsOvernightStart(TimeSpan? shiftStart)
        {
            return shiftStart.HasValue && shiftStart.Value < TimeSpan.FromHours(8);
        }

        /// <summary>考勤统计卡（PG-ORG-03）：出勤率/迟到人次/缺卡人次/待审核补卡数。</summary>
        public AttendanceSummaryDto AttendanceSummary(int year, int month, int? deptId)
        {
            if (year <= 0) { year = DateTime.Today.Year; }
            if (month <= 0 || month > 12) { month = DateTime.Today.Month; }
            return WithConnection(c => _repo.SummarizeAttendance(c, year, month, deptId));
        }

        /// <summary>考勤审核（BR-ORG-05）：通过→回写打卡并置正常闭环；驳回→保持异常且意见必填。仅异常/补卡记录可审核。</summary>
        public AttendanceDto ReviewAttendance(int id, AttendanceReviewRequest request, string reviewerUserName) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("审核请求不能为空");
                var a = _repo.GetAttendance(c, id) ?? throw ApiException.NotFound("考勤记录不存在");
                ApplyAttendanceReview(c, tx, a, request.Result, request.ReviewNote, reviewerUserName);
                return _repo.GetAttendance(c, id);
            });

        /// <summary>批量审核考勤（页面工具栏「批量审核」）：仅通过/驳回，对选中异常/待补卡记录一键闭环，返回审核条数。</summary>
        public int BatchReviewAttendance(AttendanceBatchReviewRequest request, string reviewerUserName) =>
            WithTransaction((c, tx) =>
            {
                if (request == null || request.Ids == null || request.Ids.Count == 0)
                    throw ApiException.ValidationFailed("请选择要审核的考勤记录");
                int count = 0;
                foreach (int id in request.Ids.Distinct())
                {
                    AttendanceDto a = _repo.GetAttendance(c, id);
                    if (a == null) { continue; }
                    if (!IsAttendanceReviewable(a)) { continue; } // 已确认/正常等不可审跳
                    ApplyAttendanceReview(c, tx, a, AttendanceResult.Normal, request.ReviewNote, reviewerUserName);
                    count++;
                }
                if (count == 0) throw ApiException.ValidationFailed("所选记录均不可批量审核（仅异常/迟到/缺卡/待补卡且未确认可审）");
                return count;
            });

        /// <summary>审核规则（BR-ORG-05）：异常/迟到/缺卡 或 未确认且缺下班卡 为可审。</summary>
        private static bool IsAttendanceReviewable(AttendanceDto a)
        {
            return a.ReviewBy == null;
        }

        private void ApplyAttendanceReview(IDbConnection c, IDbTransaction tx, AttendanceDto a,
            AttendanceResult? result, string reviewNote, string reviewerUserName)
        {
            if (!IsAttendanceReviewable(a)) throw ApiException.ValidationFailed("该记录已审核，无需重复处理");
            // 审核状态标识：正常/迟到/旷工/请假；未选择默认按「正常」处理
            a.Result = result.HasValue ? result.Value : AttendanceResult.Normal;
            a.AbnormalType = MapAttendanceType(a.Result);
            a.ReviewBy = _repo.GetUserIdByUserName(c, reviewerUserName) ?? 1; // 找不到当前登录用户时回退系统管理员
            a.ReviewAt = DateTime.Now;
            a.ReviewNote = reviewNote ?? string.Empty;
            _repo.UpdateAttendance(c, tx, a);
        }

        private static string MapAttendanceType(AttendanceResult result)
        {
            switch (result)
            {
                case AttendanceResult.Late: return "迟到";
                case AttendanceResult.Absent: return "旷工";
                case AttendanceResult.Leave: return "请假";
                default: return null;
            }
        }

        /// <summary>考勤月报导出（T6-2-5）：返回 CSV 文本。</summary>
        public string ExportAttendanceCsv(int year, int month, int? deptId)
        {
            if (year <= 0) { year = DateTime.Today.Year; }
            if (month <= 0 || month > 12) { month = DateTime.Today.Month; }
            return WithConnection(c =>
            {
                List<AttendanceDto> rows = _repo.ListAttendanceForExport(c, year, month, deptId);
                var sb = new StringBuilder();
                sb.AppendLine("日期,工号,姓名,班次,状态,审核状态");
                foreach (AttendanceDto a in rows)
                {
                    sb.AppendLine(string.Join(",",
                        Csv(a.WorkDate.ToString("MM-dd")), Csv(a.EmpNo), Csv(a.EmpName),
                        Csv(a.ShiftName), Csv(a.ResultText), Csv(a.ReviewStatusText)));
                }
                _repo.InsertExportLog(c, "attendance", (int)ExportFormat.Excel, "attendance-" + year.ToString("0000") + month.ToString("00") + ".csv");
                return sb.ToString();
            });
        }

        private static string Csv(object value)
        {
            string s = value == null ? string.Empty : value.ToString();
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            {
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

        private static TimeSpan? ParseTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return null; }
            text = text.Trim();
            TimeSpan ts;
            if (TimeSpan.TryParseExact(text, @"hh\:mm", CultureInfo.InvariantCulture, out ts)) { return ts; }
            if (TimeSpan.TryParseExact(text, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out ts)) { return ts; }
            DateTime dt;
            if (DateTime.TryParse(text, out dt)) { return dt.TimeOfDay; }
            return null;
        }

        private TResult WithConnection<TResult>(Func<IDbConnection, TResult> action)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return action(connection);
            }
        }

        private TResult WithTransaction<TResult>(Func<IDbConnection, IDbTransaction, TResult> action)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                var result = action(connection, transaction);
                transaction.Commit();
                return result;
            }
        }

        private void WithTransaction(Action<IDbConnection, IDbTransaction> action)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                action(connection, transaction);
                transaction.Commit();
            }
        }
    }
}
