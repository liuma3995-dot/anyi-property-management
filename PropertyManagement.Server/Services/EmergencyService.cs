using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Emergency;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 应急处置服务（M6 D6-1，UC-EMG-001~008）。
    /// 业务规则：BR-EMG-01（必须关联场景、停用不可发起）、BR-EMG-02（处置完成方可结案、复盘可后补）、
    /// BR-EMG-03（责任匹配引用排班/在岗，无匹配人工指派，P-07 不阻塞、发起人默认第一处置人）、
    /// BR-EMG-04（记录含时间/操作/结果）、BR-EMG-05（步骤版本化）、
    /// BR-EMG-06（结案归档后不得删除只可补录，补录强制 is_supplement 标记）。
    /// </summary>
    public class EmergencyService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IEmergencyRepository _repo;

        /// <summary>撤销终态：EmergencyEventStatus.Cancelled=4（枚举值待中央契约补充，先按数值留痕）。</summary>
        private const int CancelledStatusValue = 4;

        /// <summary>P-03 事故复盘时限（工作日）参数键。</summary>
        private const string ReviewDeadlineWorkdaysKey = "emergency.review.deadline.workdays";

        public EmergencyService()
            : this(new SqliteConnectionFactory(), new SqlEmergencyRepository())
        {
        }

        public EmergencyService(IDbConnectionFactory connectionFactory, IEmergencyRepository repo)
        {
            _connectionFactory = connectionFactory;
            _repo = repo;
        }

        // ===================== 场景与步骤 =====================

        public List<EmergencySceneDto> ListScenes(string keyword, int? status) =>
            WithConnection(c => _repo.ListScenes(c, keyword, status));

        public EmergencySceneDto SaveScene(int id, EmergencySceneRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("场景名称不能为空");
                if (id > 0)
                {
                    var existing = _repo.GetScene(c, id) ?? throw ApiException.NotFound("场景不存在");
                    var dto = new EmergencySceneDto
                    {
                        Id = id,
                        Name = request.Name.Trim(),
                        Category = request.Category,
                        IconKey = NormalizeIcon(request.IconKey),
                        Status = request.Status.HasValue ? (request.Status.Value == 1 ? 1 : 0) : existing.Status
                    };
                    _repo.UpdateScene(c, tx, dto);
                    return _repo.GetScene(c, id);
                }
                var created = new EmergencySceneDto { Name = request.Name.Trim(), Category = request.Category, IconKey = NormalizeIcon(request.IconKey), Status = 0 };
                created.Id = _repo.InsertScene(c, tx, created);
                return _repo.GetScene(c, created.Id);
            });

        /// <summary>场景图标归一化：空值回退为默认警笛图标（Icon.Siren）。</summary>
        private static string NormalizeIcon(string iconKey)
        {
            if (string.IsNullOrWhiteSpace(iconKey)) return "Icon.Siren";
            string key = iconKey.Trim();
            return key.StartsWith("Icon.", StringComparison.Ordinal) ? key : "Icon." + key.TrimStart('.');
        }

        /// <summary>场景启用/停用（BR-EMG-01：停用后不可新发起）。</summary>
        public EmergencySceneDto SetSceneStatus(int id, int status) =>
            WithTransaction((c, tx) =>
            {
                if (status != 0 && status != 1) throw ApiException.ValidationFailed("场景状态无效（0 启用 / 1 停用）");
                if (_repo.GetScene(c, id) == null) throw ApiException.NotFound("场景不存在");
                _repo.SetSceneStatus(c, tx, id, status);
                return _repo.GetScene(c, id);
            });

        public void DeleteScene(int id) =>
            WithTransaction((c, tx) =>
            {
                if (_repo.GetScene(c, id) == null) throw ApiException.NotFound("场景不存在");
                if (_repo.CountSceneEvents(c, id) > 0)
                    throw ApiException.Conflict("该场景存在进行中的应急事件，不可删除；已结案/已复盘的事件不影响场景删除");
                _repo.SoftDeleteScene(c, tx, id);
            });

        public List<EmergencyStepDto> ListSteps(int sceneId) =>
            WithConnection(c => _repo.ListSteps(c, sceneId));

        public EmergencyStepDto SaveStep(int id, int sceneId, EmergencyStepRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (sceneId <= 0) throw ApiException.ValidationFailed("请选择场景");
                if (string.IsNullOrWhiteSpace(request.Content)) throw ApiException.ValidationFailed("步骤内容不能为空");
                if (id > 0)
                {
                    // BR-EMG-05 版本化：更新不覆盖历史 —— 旧版本留档（status=1），插入新版本行，version_no 递增
                    var old = _repo.GetStep(c, id) ?? throw ApiException.NotFound("步骤不存在");
                    if (old.Status != 0) throw ApiException.ValidationFailed("该步骤版本已归档，不可编辑");
                    int stepNo = request.StepNo > 0 ? request.StepNo : old.StepNo;
                    int next = _repo.GetMaxStepVersion(c, tx, old.SceneId, stepNo) + 1;
                    _repo.SupersedeStep(c, tx, id);
                    var created = new EmergencyStepDto
                    {
                        SceneId = old.SceneId, StepNo = stepNo, Content = request.Content,
                        VersionNo = "v" + next, Status = 0, Role = request.Role, TimeLimit = request.TimeLimit, Action = request.Action
                    };
                    created.Id = _repo.InsertStep(c, tx, created);
                    return created;
                }
                int v = _repo.GetMaxStepVersion(c, tx, sceneId, request.StepNo) + 1;
                var added = new EmergencyStepDto
                {
                    SceneId = sceneId, StepNo = request.StepNo, Content = request.Content,
                    VersionNo = "v" + v, Status = 0, Role = request.Role, TimeLimit = request.TimeLimit, Action = request.Action
                };
                added.Id = _repo.InsertStep(c, tx, added);
                return added;
            });

        public void DeleteStep(int id) =>
            WithTransaction((c, tx) => _repo.SoftDeleteStep(c, tx, id));

        /// <summary>步骤拖拽排序持久化（需包含场景全部启用步骤，T6-1-2）。</summary>
        public void ReorderSteps(int sceneId, List<int> orderedIds) =>
            WithTransaction((c, tx) =>
            {
                if (_repo.GetScene(c, sceneId) == null) throw ApiException.NotFound("场景不存在");
                if (orderedIds == null || orderedIds.Count == 0) throw ApiException.ValidationFailed("请提供步骤顺序");
                var active = _repo.ListSteps(c, sceneId);
                var activeIds = new HashSet<int>(active.Select(s => s.Id));
                foreach (int stepId in orderedIds)
                    if (!activeIds.Contains(stepId))
                        throw ApiException.ValidationFailed("存在不属于该场景的步骤（id=" + stepId + "）");
                if (orderedIds.Distinct().Count() != orderedIds.Count)
                    throw ApiException.ValidationFailed("步骤顺序存在重复项");
                if (orderedIds.Count != active.Count)
                    throw ApiException.ValidationFailed("步骤顺序不完整（需包含场景全部步骤）");
                _repo.ReorderSteps(c, tx, sceneId, orderedIds);
            });

        // ===================== 责任匹配（BR-EMG-03） =====================

        /// <summary>按场景步骤责任角色 + 当日已发布排班 + 在岗匹配（发起页面板，T6-1-4）。</summary>
        public List<EmergencyMatchDto> MatchResponsible(int sceneId) =>
            WithConnection(c =>
            {
                if (_repo.GetScene(c, sceneId) == null) throw ApiException.NotFound("场景不存在");
                return MatchByRole(c, sceneId, new HashSet<int>());
            });

        private List<EmergencyMatchDto> MatchByRole(IDbConnection c, int sceneId, HashSet<int> excludeIds, List<EmergencyMatchDto> candidates = null)
        {
            if (candidates == null) candidates = _repo.ListDutyCandidates(c, DateTime.Today);
            var result = new List<EmergencyMatchDto>();
            var assigned = new HashSet<int>(excludeIds);
            foreach (var step in _repo.ListSteps(c, sceneId))
            {
                foreach (string role in SplitRoles(step.Role))
                {
                    var hit = candidates.FirstOrDefault(e => !assigned.Contains(e.EmployeeId) && MatchRole(role, e.PositionName));
                    if (hit == null) continue; // P-07：单角色无人匹配不阻塞，回空供人工指派
                    assigned.Add(hit.EmployeeId);
                    result.Add(new EmergencyMatchDto
                    {
                        EmployeeId = hit.EmployeeId, Name = hit.Name, PositionName = hit.PositionName,
                        Phone = hit.Phone, ShiftName = hit.ShiftName, OnDuty = hit.OnDuty, Role = role
                    });
                }
            }
            return result;
        }

        private static IEnumerable<string> SplitRoles(string role)
        {
            if (string.IsNullOrWhiteSpace(role)) yield break;
            foreach (string token in role.Split(new[] { '、', '，', ',', '/', '／', ';', '；', '与', '和', '或', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = token.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        /// <summary>角色 ↔ 岗位名称匹配：部门近义词 + 职级关键字（如 秩序班长→保安班长、工程值班→工程值班）。</summary>
        private static bool MatchRole(string role, string positionName)
        {
            if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(positionName)) return false;
            if (role.Contains("维保")) return false; // 维保单位为外部厂商，不在内部员工中匹配
            if (role.Contains("全体") || role.Contains("全员")) return true; // 全体值班 → 所有在岗人员
            string[] deptKeys;
            if (role.Contains("秩序") || role.Contains("保安") || role.Contains("安保") || role.Contains("监控"))
                deptKeys = new[] { "保安", "秩序", "安保", "监控" };
            else if (role.Contains("工程")) deptKeys = new[] { "工程" };
            else if (role.Contains("客服")) deptKeys = new[] { "客服" };
            else if (role.Contains("保洁")) deptKeys = new[] { "保洁" };
            else deptKeys = null; // 未识别部门 → 不按部门过滤
            if (deptKeys != null && !deptKeys.Any(positionName.Contains)) return false;
            if (role.Contains("班长")) return positionName.Contains("班长");
            if (role.Contains("主管")) return positionName.Contains("主管");
            if (role.Contains("负责人")) return positionName.Contains("负责人");
            if (role.Contains("值班")) return positionName.Contains("值班");
            return true;
        }

        // ===================== 事件 =====================

        public PageResult<EmergencyEventDto> QueryEvents(EmergencyEventQueryRequest query) =>
            WithConnection(c => _repo.QueryEvents(c, query ?? new EmergencyEventQueryRequest { PageIndex = 1, PageSize = 20 }, out int total));

        public EmergencyEventDetailDto GetEvent(int id) =>
            WithConnection(c => BuildEventDetail(c, id));

        public EmergencyEventDetailDto CreateEvent(EmergencyEventCreateRequest request, string operatorName) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("请求不能为空");
            if (request.SceneId <= 0) throw ApiException.ValidationFailed("请选择应急场景");
                var scene = _repo.GetScene(c, request.SceneId) ?? throw ApiException.NotFound("应急场景不存在");
            if (scene.Status != 0) throw ApiException.Conflict("场景已停用，不可发起应急事件");
                if (request.Level < 1 || request.Level > 3) throw ApiException.ValidationFailed("事件级别无效（1=Ⅰ 级、2=Ⅱ 级、3=Ⅲ 级）");
                var evt = new EmergencyEventDto
                {
                    SceneId = request.SceneId,
                    EventTime = request.EventTime == default ? DateTime.Now : request.EventTime,
                    Location = request.Location ?? string.Empty,
                    Description = request.Description ?? string.Empty,
                    DetailLocation = request.DetailLocation ?? string.Empty,
                    Level = request.Level,
                    Status = EmergencyEventStatus.Initiated,
                    RecordPending = true,
                    StepVersion = "v1"
                };
                evt.Id = _repo.InsertEvent(c, tx, evt);
                // 编码规则与原型一致：EM-yyMM-序号；InsertEvent 后立即回填落库，保证 DB 非空、关键字搜索可用
                evt.EventNo = "EM-" + DateTime.Now.ToString("yyMM") + "-" + evt.Id;
                _repo.UpdateEventNo(c, tx, evt.Id, evt.EventNo);
                _repo.UpdateEventStatus(c, tx, evt.Id, EmergencyEventStatus.Initiated, true);
                _repo.InsertStatusLog(c, tx, evt.Id, -1, (int)EmergencyEventStatus.Initiated, "发起", operatorName);
                // BR-EMG-03 + P-07：按「步骤角色 + 当日已发布排班 + 在岗」自动匹配，无人匹配不阻塞；
                // 发起人（请求指定或当前登录用户）默认为第一处置人
                var candidates = _repo.ListDutyCandidates(c, DateTime.Today);
                var assigned = new HashSet<int>();
                string initiator = !string.IsNullOrWhiteSpace(request.InitiatorName) ? request.InitiatorName.Trim() : (operatorName ?? string.Empty).Trim();
                var initiatorEmployee = candidates.FirstOrDefault(e => e.Name == initiator);
                if (initiatorEmployee != null)
                {
                    _repo.InsertAssignment(c, tx, new EmergencyAssignDto { EventId = evt.Id, EmployeeId = initiatorEmployee.EmployeeId, AssignType = EmergencyAssignType.Responsible });
                    assigned.Add(initiatorEmployee.EmployeeId);
                }
                foreach (var match in MatchByRole(c, scene.Id, assigned, candidates))
                {
                    _repo.InsertAssignment(c, tx, new EmergencyAssignDto { EventId = evt.Id, EmployeeId = match.EmployeeId, AssignType = EmergencyAssignType.Responsible });
                }
                return BuildEventDetail(c, evt.Id);
            });

        public EmergencyEventDetailDto AssignResponsible(int id, EmergencyAssignRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (_repo.GetEvent(c, id) == null) throw ApiException.NotFound("应急事件不存在");
                if (request.ResponsibleIds != null)
                    foreach (var empId in request.ResponsibleIds)
                        _repo.InsertAssignment(c, tx, new EmergencyAssignDto { EventId = id, EmployeeId = empId, AssignType = EmergencyAssignType.Responsible });
                if (request.DutyIds != null)
                    foreach (var empId in request.DutyIds)
                        _repo.InsertAssignment(c, tx, new EmergencyAssignDto { EventId = id, EmployeeId = empId, AssignType = EmergencyAssignType.Duty });
                return BuildEventDetail(c, id);
            });

        /// <summary>误发起撤销：仅「已发起」且发起后 60 秒内，软删留痕（原型 PG-EMG-02）。</summary>
        public void CancelEvent(int id, string operatorName) =>
            WithTransaction((c, tx) =>
            {
                var evt = _repo.GetEvent(c, id) ?? throw ApiException.NotFound("应急事件不存在");
                if (evt.Status != EmergencyEventStatus.Initiated)
                    throw ApiException.Conflict("仅「已发起」状态的事件可撤销");
                int rows = _repo.CancelEvent(c, tx, id);
                if (rows == 0)
                    throw ApiException.Conflict("仅发起后 60 秒内可撤销（留痕）");
                _repo.InsertStatusLog(c, tx, id, (int)evt.Status, CancelledStatusValue, "撤销", operatorName);
            });

        public EmergencyRecordDto AddRecord(int id, EmergencyRecordRequest request, string operatorName) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("请求不能为空");
                var evt = _repo.GetEvent(c, id) ?? throw ApiException.NotFound("应急事件不存在");
                bool isSupplement = request.IsSupplement;
                if (evt.Status == EmergencyEventStatus.Closed || evt.Status == EmergencyEventStatus.Reviewed)
                {
                    // BR-EMG-06：归档后不得删除，只能补录（补录通道须带 IsSupplement 标记）
                    if (!isSupplement)
                throw ApiException.Conflict("已结案事件不可新增处置记录，请使用补录");
                    isSupplement = true;
                }
            if (string.IsNullOrWhiteSpace(request.Content)) throw ApiException.ValidationFailed("处置记录内容不能为空");
            if (string.IsNullOrWhiteSpace(request.Result)) throw ApiException.ValidationFailed("处置结果不能为空（记录必含时间/操作/结果）");
                var dto = new EmergencyRecordDto
                {
                    EventId = id,
                    RecordTime = request.RecordTime == default ? DateTime.Now : request.RecordTime,
                    Content = request.Content,
                    Result = request.Result,
                    IsSupplement = isSupplement,
                    Recorder = !string.IsNullOrWhiteSpace(request.Recorder)
                        ? request.Recorder
                        : (!string.IsNullOrWhiteSpace(operatorName) ? operatorName : "系统管理员")
                };
                dto.Id = _repo.InsertRecord(c, tx, dto);
                if (evt.Status == EmergencyEventStatus.Initiated)
                {
                    _repo.UpdateEventStatus(c, tx, id, EmergencyEventStatus.Handling, false);
                    _repo.InsertStatusLog(c, tx, id, (int)EmergencyEventStatus.Initiated, (int)EmergencyEventStatus.Handling, "处置", operatorName);
                }
                return dto;
            });

        public EmergencyEventDetailDto CloseEvent(int id, EmergencyCloseRequest request, string operatorName) =>
            WithTransaction((c, tx) =>
            {
                var evt = _repo.GetEvent(c, id) ?? throw ApiException.NotFound("应急事件不存在");
                if (evt.Status == EmergencyEventStatus.Closed || evt.Status == EmergencyEventStatus.Reviewed)
                    throw ApiException.Conflict("该事件已结案（归档后不可删除，只可补录）");
                if (request == null || string.IsNullOrWhiteSpace(request.Summary))
                throw ApiException.ValidationFailed("结案必填处置结果与物资消耗");
                int records = _repo.ListRecords(c, id).Count;
                if (records < 1)
                throw ApiException.Conflict("处置完成方可结案，至少需一条处置记录");
                _repo.SetCloseSummary(c, tx, id, request.Summary.Trim());
                _repo.UpdateEventStatus(c, tx, id, EmergencyEventStatus.Closed, false);
                _repo.InsertStatusLog(c, tx, id, (int)evt.Status, (int)EmergencyEventStatus.Closed, "结案", operatorName);
                return BuildEventDetail(c, id);
            });

        private EmergencyEventDetailDto BuildEventDetail(IDbConnection c, int id)
        {
            var evt = _repo.GetEvent(c, id) ?? throw ApiException.NotFound("应急事件不存在");
            return new EmergencyEventDetailDto
            {
                Event = evt,
                Steps = _repo.ListSteps(c, evt.SceneId),
                Assignments = _repo.ListAssignments(c, id),
                Records = _repo.ListRecords(c, id),
                Review = _repo.GetReview(c, id)
            };
        }

        // ===================== 复盘（UC-EMG-007，P-03） =====================

        public EmergencyReviewDto ReviewEvent(int id, EmergencyReviewRequest request, string operatorName) =>
            WithTransaction((c, tx) =>
            {
                var evt = _repo.GetEvent(c, id) ?? throw ApiException.NotFound("应急事件不存在");
                if (evt.Status != EmergencyEventStatus.Closed && evt.Status != EmergencyEventStatus.Reviewed)
                throw ApiException.Conflict("仅已结案事件可录入复盘（处置完成结案后方可复盘，归档后可补录复盘）");
                if (request == null) throw ApiException.ValidationFailed("请求不能为空");
                if (request.Items != null && request.Items.Any(i => i != null && string.IsNullOrWhiteSpace(i.Content)))
                    throw ApiException.ValidationFailed("改进措施内容不能为空");
                var items = (request.Items ?? new List<EmergencyReviewItemRequest>())
                    .Where(i => i != null && !string.IsNullOrWhiteSpace(i.Content))
                    .Select(i => new EmergencyReviewItemDto { Content = i.Content.Trim(), Owner = i.Owner, DueDate = i.DueDate, Status = i.Status })
                    .ToList();
                int? completionRate = request.CompletionRate;
                if (!completionRate.HasValue && items.Count > 0)
                    completionRate = (int)Math.Round(100.0 * items.Count(i => i.Status == 2) / items.Count);
                if (completionRate.HasValue) completionRate = Math.Max(0, Math.Min(100, completionRate.Value));
                var dto = new EmergencyReviewDto
                {
                    EventId = id,
                    Cause = request.Cause,
                    Measure = request.Measure,
                    FinishAt = DateTime.Now,
                    HostName = !string.IsNullOrWhiteSpace(request.HostName) ? request.HostName : operatorName,
                    PlanDate = request.PlanDate ?? DateTime.Today,
                    CompletionRate = completionRate
                };
                int reviewId = _repo.UpsertReview(c, tx, dto);
                _repo.ReplaceReviewItems(c, tx, reviewId, items);
                _repo.UpdateEventStatus(c, tx, id, EmergencyEventStatus.Reviewed, false);
                _repo.InsertStatusLog(c, tx, id, (int)evt.Status, (int)EmergencyEventStatus.Reviewed, "复盘", operatorName);
                return _repo.GetReview(c, id);
            });

        public PageResult<EmergencyReviewDto> QueryReviews(PageRequest query) =>
            WithConnection(c => _repo.QueryReviews(c, query ?? new PageRequest(), out int total));

        public EmergencyReviewDto GetReview(int eventId) =>
            WithConnection(c => _repo.GetReview(c, eventId) ?? throw ApiException.NotFound("该事件尚未录入复盘"));

        // ===================== 统计（T6-1-7，P-03） =====================

        public EmergencyEventStatsDto GetEventStats() =>
            WithConnection(c =>
            {
                var stats = _repo.GetEventStats(c);
                int workdays = ParseParamInt(_repo.GetParamValue(c, ReviewDeadlineWorkdaysKey), 3);
                DateTime today = DateTime.Today;
                stats.ReviewPending = _repo.ListUnreviewedClosedAt(c)
                    .Count(closedAt => today > AddWorkdays(closedAt.Date, workdays));
                return stats;
            });

        private static int ParseParamInt(string value, int fallback)
        {
            int n;
            return int.TryParse(value, out n) && n >= 0 ? n : fallback;
        }

        /// <summary>P-03：从起始日（不含）起加 n 个工作日（周六日不计，法定节假日暂不纳入）。</summary>
        private static DateTime AddWorkdays(DateTime start, int workdays)
        {
            DateTime d = start;
            int added = 0;
            while (added < workdays)
            {
                d = d.AddDays(1);
                if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday) added++;
            }
            return d;
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
