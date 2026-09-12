using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Emergency;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>应急处置仓储 SQLite 实现（M6 D6-1，Dapper）。</summary>
    public class SqlEmergencyRepository : IEmergencyRepository
    {
        // ===================== 场景 =====================

        private const string SceneBaseSql =
            "SELECT s.id, s.name, s.category, s.icon_key AS IconKey, s.status, " +
            "(SELECT COUNT(1) FROM t_emergency_step st WHERE st.scene_id = s.id AND st.status = 0) AS StepCount " +
            "FROM t_emergency_scene s";

        public List<EmergencySceneDto> ListScenes(IDbConnection connection, string keyword, int? status)
        {
            string sql = SceneBaseSql + " WHERE s.del_flag = 0";
            var p = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(keyword)) { sql += " AND s.name LIKE @kw"; p.Add("kw", "%" + keyword.Trim() + "%"); }
            if (status.HasValue) { sql += " AND s.status = @status"; p.Add("status", status.Value); }
            sql += " ORDER BY s.id";
            var scenes = connection.Query<EmergencySceneDto>(sql, p).Select(SceneNormalize).ToList();
            FillSceneTimeLimit(connection, scenes);
            return scenes;
        }

        public EmergencySceneDto GetScene(IDbConnection connection, int id)
        {
            var dto = connection.QueryFirstOrDefault<EmergencySceneDto>(
                SceneBaseSql + " WHERE s.id = @id AND s.del_flag = 0", new { id });
            if (dto == null) return null;
            FillSceneTimeLimit(connection, new List<EmergencySceneDto> { SceneNormalize(dto) });
            return dto;
        }

        public int InsertScene(IDbConnection connection, IDbTransaction transaction, EmergencySceneDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_emergency_scene (name, category, icon_key, status, del_flag) VALUES (@Name, @Category, @IconKey, 0, 0); SELECT last_insert_rowid();",
                new { dto.Name, dto.Category, dto.IconKey }, transaction);
        }

        public void UpdateScene(IDbConnection connection, IDbTransaction transaction, EmergencySceneDto dto)
        {
            connection.Execute(
                "UPDATE t_emergency_scene SET name = @Name, category = @Category, icon_key = @IconKey, status = @Status, updated_at = datetime('now','localtime') " +
                "WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.Name, dto.Category, dto.IconKey, dto.Status }, transaction);
        }

        public void SetSceneStatus(IDbConnection connection, IDbTransaction transaction, int id, int status)
        {
            connection.Execute(
                "UPDATE t_emergency_scene SET status = @status, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                new { id, status }, transaction);
        }

        public int CountSceneEvents(IDbConnection connection, int sceneId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_emergency_event WHERE scene_id = @sceneId AND del_flag = 0 AND status IN (0, 1)", new { sceneId });
        }

        public void SoftDeleteScene(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_emergency_scene SET status = 1, del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        /// <summary>场景卡「N 步 · M 分钟」：按步骤 time_limit 解析分钟求和（解析不出仅显示步数）。</summary>
        private void FillSceneTimeLimit(IDbConnection connection, List<EmergencySceneDto> scenes)
        {
            if (scenes == null || scenes.Count == 0) return;
            var rows = connection.Query<StepTimeRow>(
                "SELECT scene_id AS SceneId, time_limit AS TimeLimit FROM t_emergency_step WHERE status = 0").ToList();
            var minutes = rows.GroupBy(r => r.SceneId)
                .ToDictionary(g => g.Key, g => g.Sum(x => ParseMinutes(x.TimeLimit) ?? 0));
            foreach (var scene in scenes)
            {
                int m;
                if (minutes.TryGetValue(scene.Id, out m) && m > 0) scene.StepMinutes = m;
                scene.TimeLimitText = scene.StepMinutes.HasValue
                    ? scene.StepCount + " 步 · " + scene.StepMinutes.Value + " 分钟"
                    : scene.StepCount + " 步";
            }
        }

        /// <summary>解析「N 分钟」时限为分钟（立即/当日/即时等返回 null）。</summary>
        private static int? ParseMinutes(string timeLimit)
        {
            if (string.IsNullOrEmpty(timeLimit)) return null;
            int idx = timeLimit.IndexOf("分钟", StringComparison.Ordinal);
            if (idx <= 0) return null;
            int start = idx;
            while (start > 0 && char.IsDigit(timeLimit[start - 1])) start--;
            int v;
            return int.TryParse(timeLimit.Substring(start, idx - start), out v) ? v : (int?)null;
        }

        private class StepTimeRow
        {
            public int SceneId { get; set; }
            public string TimeLimit { get; set; }
        }

        // ===================== 步骤 =====================

        public List<EmergencyStepDto> ListSteps(IDbConnection connection, int sceneId)
        {
            return connection.Query<EmergencyStepDto>(
                "SELECT id, scene_id AS SceneId, step_no AS StepNo, content, version_no AS VersionNo, status, role, " +
                "time_limit AS TimeLimit, action As Action FROM t_emergency_step WHERE scene_id = @sceneId AND status = 0 ORDER BY step_no",
                new { sceneId }).Select(StepNormalize).ToList();
        }

        public EmergencyStepDto GetStep(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<EmergencyStepDto>(
                "SELECT id, scene_id AS SceneId, step_no AS StepNo, content, version_no AS VersionNo, status, role, " +
                "time_limit AS TimeLimit, action As Action FROM t_emergency_step WHERE id = @id", new { id });
        }

        public int SupersedeStep(IDbConnection connection, IDbTransaction transaction, int id)
        {
            return connection.ExecuteScalar<int>(
                "UPDATE t_emergency_step SET status = 1, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND status = 0; SELECT (SELECT step_no FROM t_emergency_step WHERE id = @id);",
                new { id }, transaction);
        }

        public int GetMaxStepVersion(IDbConnection connection, IDbTransaction transaction, int sceneId, int stepNo)
        {
            // BR-EMG-05：版本号 v1/v2/...（历史行 status=1 一并计入，删除行 status=2 不计入）
            var versions = connection.Query<string>(
                "SELECT version_no FROM t_emergency_step WHERE scene_id = @sceneId AND step_no = @stepNo AND status IN (0,1)",
                new { sceneId, stepNo }, transaction).ToList();
            int max = 0;
            foreach (string v in versions)
            {
                string digits = (v ?? string.Empty).Trim().TrimStart('v', 'V');
                int n;
                if (int.TryParse(digits, out n) && n > max) max = n;
            }
            return max;
        }

        public void ReorderSteps(IDbConnection connection, IDbTransaction transaction, int sceneId, List<int> orderedIds)
        {
            if (orderedIds == null || orderedIds.Count == 0) return;
            for (int i = 0; i < orderedIds.Count; i++)
            {
                connection.Execute(
                    "UPDATE t_emergency_step SET step_no = @no, updated_at = datetime('now','localtime') " +
                    "WHERE id = @id AND scene_id = @sceneId AND status = 0",
                    new { no = i + 1, id = orderedIds[i], sceneId }, transaction);
            }
        }

        public int InsertStep(IDbConnection connection, IDbTransaction transaction, EmergencyStepDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_emergency_step (scene_id, step_no, content, version_no, status, role, time_limit, action) " +
                "VALUES (@SceneId, @StepNo, @Content, @VersionNo, 0, @Role, @TimeLimit, @Action); SELECT last_insert_rowid();",
                new { dto.SceneId, dto.StepNo, dto.Content, dto.VersionNo, dto.Role, dto.TimeLimit, dto.Action }, transaction);
        }

        public void UpdateStep(IDbConnection connection, IDbTransaction transaction, EmergencyStepDto dto)
        {
            connection.Execute(
                "UPDATE t_emergency_step SET step_no = @StepNo, content = @Content, version_no = @VersionNo, role = @Role, " +
                "time_limit = @TimeLimit, action = @Action, updated_at = datetime('now','localtime') WHERE id = @Id AND status = 0",
                new { dto.Id, dto.StepNo, dto.Content, dto.VersionNo, dto.Role, dto.TimeLimit, dto.Action }, transaction);
        }

        public void SoftDeleteStep(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_emergency_step SET status = 1, updated_at = datetime('now','localtime') WHERE id = @id AND status = 0", new { id }, transaction);
        }

        // ===================== 责任匹配候选（BR-EMG-03） =====================

        private const string DutyScheduleCondition =
            "s.work_date = @date AND s.status = 1 AND s.del_flag = 0 AND COALESCE(sh.name, '') <> '休'";

        public List<EmergencyMatchDto> ListDutyCandidates(IDbConnection connection, DateTime date)
        {
            // 在职员工（status=0）联当日已发布排班：班次为「休」不计在岗；OnDuty 优先排序
            string sql =
                "SELECT e.id AS EmployeeId, e.name AS Name, COALESCE(e.phone, '') AS Phone, " +
                "COALESCE(p.name, '') AS PositionName, " +
                "COALESCE((SELECT sh.name FROM t_schedule s LEFT JOIN t_shift sh ON sh.id = s.shift_id " +
                "WHERE s.employee_id = e.id AND " + DutyScheduleCondition + " ORDER BY s.id LIMIT 1), '') AS ShiftName, " +
                "(SELECT COUNT(1) FROM t_schedule s LEFT JOIN t_shift sh ON sh.id = s.shift_id " +
                "WHERE s.employee_id = e.id AND " + DutyScheduleCondition + ") AS OnDuty " +
                "FROM t_employee e LEFT JOIN t_position p ON p.id = e.position_id " +
                "WHERE e.del_flag = 0 AND e.status = 0 " +
                "ORDER BY OnDuty DESC, e.id";
            return connection.Query<EmergencyMatchDto>(sql, new { date = date.ToString("yyyy-MM-dd") })
                .Select(d => { d.Phone = MaskPhone(d.Phone); return d; }).ToList();
        }

        /// <summary>电话脱敏（138****2233，原型口径）；过短号码原样返回。</summary>
        private static string MaskPhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
            string digits = phone.Trim();
            if (digits.Length < 8) return digits;
            return digits.Substring(0, 3) + "****" + digits.Substring(digits.Length - 4);
        }

        public string GetParamValue(IDbConnection connection, string key)
        {
            return connection.ExecuteScalar<string>(
                "SELECT param_value FROM t_param WHERE param_key = @key", new { key });
        }

        // ===================== 事件 =====================

        private const string EventBaseSql =
            "SELECT e.id, e.scene_id AS SceneId, e.event_time AS EventTime, e.location, e.description, e.status, " +
            "e.step_version AS StepVersion, e.record_pending AS RecordPending, e.event_no AS EventNo, e.level, e.detail_location AS DetailLocation, " +
            "e.close_summary AS CloseSummary, e.closed_at AS ClosedAt, e.created_at AS CreatedAt, " +
            "COALESCE(s.name,'') AS SceneName " +
            "FROM t_emergency_event e LEFT JOIN t_emergency_scene s ON s.id = e.scene_id";

        public PageResult<EmergencyEventDto> QueryEvents(IDbConnection connection, EmergencyEventQueryRequest query, out int total)
        {
            string where = "WHERE e.del_flag = 0";
            var p = new DynamicParameters();
            if (query.Status.HasValue) { where += " AND e.status = @status"; p.Add("status", (int)query.Status.Value); }
            if (query.SceneId.HasValue) { where += " AND e.scene_id = @sceneId"; p.Add("sceneId", query.SceneId.Value); }
            if (query.Level.HasValue) { where += " AND e.level = @level"; p.Add("level", query.Level.Value); }
            if (query.From.HasValue) { where += " AND e.event_time >= @from"; p.Add("from", query.From.Value.ToString("yyyy-MM-dd")); }
            // To 含当天整天：与次日零点开区间比较（SQLite 文本比较下 'yyyy-MM-dd HH:mm' < 次日日期成立）
            if (query.To.HasValue) { where += " AND e.event_time < date(@to, '+1 day')"; p.Add("to", query.To.Value.ToString("yyyy-MM-dd")); }
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                string kw = "%" + query.Keyword.Trim() + "%";
                where += " AND (e.location LIKE @kw OR e.event_no LIKE @kw OR e.description LIKE @kw)";
                p.Add("kw", kw);
            }
            total = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_emergency_event e " + where, p);
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;
            p.Add("limit", pageSize); p.Add("offset", offset);
            var items = connection.Query<EmergencyEventDto>(
                EventBaseSql + " " + where + " ORDER BY e.id DESC LIMIT @limit OFFSET @offset", p)
                .Select(d => FillEventPerson(connection, d)).ToList();
            return new PageResult<EmergencyEventDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }

        public EmergencyEventDto GetEvent(IDbConnection connection, int id)
        {
            var dto = connection.QueryFirstOrDefault<EmergencyEventDto>(
                EventBaseSql + " WHERE e.id = @id AND e.del_flag = 0", new { id });
            if (dto == null) return null;
            return FillEventPerson(connection, dto);
        }

        public int InsertEvent(IDbConnection connection, IDbTransaction transaction, EmergencyEventDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_emergency_event (scene_id, event_time, location, description, status, step_version, del_flag, event_no, level, detail_location) " +
                "VALUES (@SceneId, @EventTime, @Location, @Description, @Status, @StepVersion, 0, @EventNo, @Level, @DetailLocation); SELECT last_insert_rowid();",
                new { dto.SceneId, EventTime = dto.EventTime, dto.Location, dto.Description, Status = (int)dto.Status, dto.StepVersion, dto.EventNo, dto.Level, dto.DetailLocation }, transaction);
        }

        public void UpdateEventNo(IDbConnection connection, IDbTransaction transaction, int id, string eventNo)
        {
            connection.Execute(
                "UPDATE t_emergency_event SET event_no = @eventNo, updated_at = datetime('now','localtime') WHERE id = @id",
                new { id, eventNo }, transaction);
        }

        public void UpdateEventStatus(IDbConnection connection, IDbTransaction transaction, int id, EmergencyEventStatus status, bool recordPending)
        {
            connection.Execute(
                "UPDATE t_emergency_event SET status = @status, record_pending = @recordPending, updated_at = datetime('now','localtime') WHERE id = @id",
                new { id, status = (int)status, recordPending = recordPending ? 1 : 0 }, transaction);
        }

        public void SetCloseSummary(IDbConnection connection, IDbTransaction transaction, int id, string summary)
        {
            // 结案留档同时落 closed_at（P-03 复盘时限与「本月已结案」统计口径）
            connection.Execute(
                "UPDATE t_emergency_event SET close_summary = @summary, closed_at = datetime('now','localtime'), updated_at = datetime('now','localtime') WHERE id = @id",
                new { id, summary }, transaction);
        }

        public int CancelEvent(IDbConnection connection, IDbTransaction transaction, int id)
        {
            // 误发起撤销：软删留痕，仅「已发起」且 created_at 60 秒内生效（返回受影响行数供服务层判定）
            return connection.Execute(
                "UPDATE t_emergency_event SET del_flag = 1, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND del_flag = 0 AND status = 0 " +
                "AND created_at >= datetime('now','localtime','-60 seconds')", new { id }, transaction);
        }

        public void InsertStatusLog(IDbConnection connection, IDbTransaction transaction, int eventId, int oldStatus, int newStatus, string action, string operatorName)
        {
            connection.Execute(
                "INSERT INTO t_event_status_log (event_id, old_status, new_status, operator, action) " +
                "VALUES (@eventId, @oldStatus, @newStatus, @operatorName, @action)",
                new { eventId, oldStatus, newStatus, operatorName = operatorName ?? string.Empty, action = action ?? string.Empty }, transaction);
        }

        public EmergencyEventStatsDto GetEventStats(IDbConnection connection)
        {
            var stats = new EmergencyEventStatsDto();
            stats.Ongoing = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_emergency_event WHERE del_flag = 0 AND status IN (0, 1)");
            // 处置超时：Ⅰ 级 >15 分钟、Ⅱ 级 >30 分钟（原型 PG-EMG-02/03 明确）、Ⅲ 级 >60 分钟
            stats.OvertimeCount = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_emergency_event WHERE del_flag = 0 AND status IN (0, 1) AND (" +
                "(level = 1 AND event_time < datetime('now','localtime','-15 minutes')) OR " +
                "(level = 2 AND event_time < datetime('now','localtime','-30 minutes')) OR " +
                "(level NOT IN (1, 2) AND event_time < datetime('now','localtime','-60 minutes')))");
            stats.TodayNew = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_emergency_event WHERE del_flag = 0 AND created_at >= date('now','localtime')");
            stats.MonthClosed = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_emergency_event WHERE del_flag = 0 " +
                "AND closed_at IS NOT NULL AND closed_at <> '' " +
                "AND closed_at >= date('now','localtime','start of month')");
            return stats;
        }

        public List<DateTime> ListUnreviewedClosedAt(IDbConnection connection)
        {
            return connection.Query<string>(
                "SELECT closed_at FROM t_emergency_event " +
                "WHERE del_flag = 0 AND closed_at IS NOT NULL AND closed_at <> '' " +
                "AND NOT EXISTS (SELECT 1 FROM t_emergency_review r WHERE r.event_id = t_emergency_event.id)")
                .Select(s => DateTime.Parse(s)).ToList();
        }

        // ===================== 指派 / 处置记录 =====================

        public List<EmergencyAssignDto> ListAssignments(IDbConnection connection, int eventId)
        {
            string sql =
                "SELECT a.id, a.event_id AS EventId, a.employee_id AS EmployeeId, COALESCE(e.name,'') AS EmployeeName, " +
                "a.assign_type AS AssignType, a.assigned_at AS AssignedAt, COALESCE(p.name,'') AS PositionName, " +
                "COALESCE(e.phone, '') AS Phone, " +
                "COALESCE((SELECT sh.name FROM t_schedule s LEFT JOIN t_shift sh ON sh.id = s.shift_id " +
                "WHERE s.employee_id = a.employee_id AND " + DutyScheduleCondition + " ORDER BY s.id LIMIT 1), '') AS ShiftName, " +
                "(SELECT COUNT(1) FROM t_schedule s LEFT JOIN t_shift sh ON sh.id = s.shift_id " +
                "WHERE s.employee_id = a.employee_id AND " + DutyScheduleCondition + ") AS OnDuty " +
                "FROM t_emergency_assign a LEFT JOIN t_employee e ON e.id = a.employee_id " +
                "LEFT JOIN t_position p ON p.id = e.position_id " +
                "WHERE a.event_id = @eventId ORDER BY a.id";
            return connection.Query<EmergencyAssignDto>(sql, new { eventId, date = DateTime.Today.ToString("yyyy-MM-dd") })
                .Select(d => { d.Phone = MaskPhone(d.Phone); return d; }).ToList();
        }

        public void InsertAssignment(IDbConnection connection, IDbTransaction transaction, EmergencyAssignDto dto)
        {
            connection.Execute(
                "INSERT INTO t_emergency_assign (event_id, employee_id, assign_type) VALUES (@EventId, @EmployeeId, @AssignType)",
                new { dto.EventId, dto.EmployeeId, AssignType = (int)dto.AssignType }, transaction);
        }

        public List<EmergencyRecordDto> ListRecords(IDbConnection connection, int eventId)
        {
            return connection.Query<EmergencyRecordDto>(
                "SELECT id, event_id AS EventId, record_time AS RecordTime, content, result, recorder, is_supplement AS IsSupplement " +
                "FROM t_emergency_record WHERE event_id = @eventId ORDER BY record_time", new { eventId }).ToList();
        }

        public int InsertRecord(IDbConnection connection, IDbTransaction transaction, EmergencyRecordDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_emergency_record (event_id, record_time, content, result, recorder, is_supplement) " +
                "VALUES (@EventId, @RecordTime, @Content, @Result, @Recorder, @IsSupplement); SELECT last_insert_rowid();",
                new { dto.EventId, RecordTime = dto.RecordTime, dto.Content, dto.Result, dto.Recorder, IsSupplement = dto.IsSupplement ? 1 : 0 }, transaction);
        }

        // ===================== 复盘 =====================

        private const string ReviewBaseSql =
            "SELECT r.id, r.event_id AS EventId, r.review_no AS ReviewNo, r.host_name AS HostName, r.plan_date AS PlanDate, " +
            "r.cause, r.measure, r.finish_at AS FinishAt, r.completion_rate AS CompletionRate, r.created_at AS CreatedAt, " +
            "COALESCE(e.event_no, '') AS EventNo " +
            "FROM t_emergency_review r LEFT JOIN t_emergency_event e ON e.id = r.event_id";

        public EmergencyReviewDto GetReview(IDbConnection connection, int eventId)
        {
            var dto = connection.QueryFirstOrDefault<EmergencyReviewDto>(
                ReviewBaseSql + " WHERE r.event_id = @eventId", new { eventId });
            if (dto == null) return null;
            FillReviewItems(connection, new List<EmergencyReviewDto> { dto });
            return dto;
        }

        public PageResult<EmergencyReviewDto> QueryReviews(IDbConnection connection, PageRequest query, out int total)
        {
            string where = "WHERE 1 = 1";
            var p = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                string kw = "%" + query.Keyword.Trim() + "%";
                where += " AND (r.review_no LIKE @kw OR e.event_no LIKE @kw OR r.host_name LIKE @kw OR e.location LIKE @kw)";
                p.Add("kw", kw);
            }
            total = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_emergency_review r LEFT JOIN t_emergency_event e ON e.id = r.event_id " + where, p);
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            p.Add("limit", pageSize); p.Add("offset", (pageIndex - 1) * pageSize);
            var items = connection.Query<EmergencyReviewDto>(
                ReviewBaseSql + " " + where + " ORDER BY r.id DESC LIMIT @limit OFFSET @offset", p).ToList();
            FillReviewItems(connection, items);
            return new PageResult<EmergencyReviewDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }

        public int UpsertReview(IDbConnection connection, IDbTransaction transaction, EmergencyReviewDto dto)
        {
            int existingId = connection.ExecuteScalar<int?>(
                "SELECT id FROM t_emergency_review WHERE event_id = @EventId", new { dto.EventId }) ?? 0;
            if (existingId == 0)
            {
                int reviewId = connection.ExecuteScalar<int>(
                    "INSERT INTO t_emergency_review (event_id, cause, measure, finish_at, host_name, plan_date, completion_rate) " +
                    "VALUES (@EventId, @Cause, @Measure, @FinishAt, @HostName, @PlanDate, @CompletionRate); SELECT last_insert_rowid();",
                    new { dto.EventId, dto.Cause, dto.Measure, FinishAt = ToDateText(dto.FinishAt), dto.HostName, PlanDate = ToDateText(dto.PlanDate), CompletionRate = dto.CompletionRate }, transaction);
                connection.Execute(
                    "UPDATE t_emergency_review SET review_no = @reviewNo WHERE id = @id",
                    new { id = reviewId, reviewNo = BuildReviewNo(reviewId) }, transaction);
                return reviewId;
            }
            // 重复提交为更新；历史行 review_no 为空时回填
            connection.Execute(
                "UPDATE t_emergency_review SET cause = @Cause, measure = @Measure, finish_at = @FinishAt, host_name = @HostName, " +
                "plan_date = @PlanDate, completion_rate = @CompletionRate, " +
                "review_no = COALESCE(NULLIF(review_no, ''), @ReviewNo) WHERE id = @Id",
                new { dto.Cause, dto.Measure, FinishAt = ToDateText(dto.FinishAt), dto.HostName, PlanDate = ToDateText(dto.PlanDate), CompletionRate = dto.CompletionRate, ReviewNo = BuildReviewNo(existingId), Id = existingId }, transaction);
            return existingId;
        }

        public void ReplaceReviewItems(IDbConnection connection, IDbTransaction transaction, int reviewId, List<EmergencyReviewItemDto> items)
        {
            connection.Execute(
                "DELETE FROM t_emergency_review_item WHERE review_id = @reviewId", new { reviewId }, transaction);
            if (items == null) return;
            int sort = 0;
            foreach (var item in items)
            {
                connection.Execute(
                    "INSERT INTO t_emergency_review_item (review_id, content, owner, due_date, status, sort) " +
                    "VALUES (@ReviewId, @Content, @Owner, @DueDate, @Status, @Sort)",
                    new { ReviewId = reviewId, item.Content, Owner = item.Owner ?? string.Empty, DueDate = ToDateText(item.DueDate), Status = item.Status, Sort = sort++ }, transaction);
            }
        }

        private static string BuildReviewNo(int reviewId)
        {
            return "FP-" + DateTime.Now.ToString("yyMM") + "-" + reviewId;
        }

        private static string ToDateText(DateTime? value)
        {
            return value == null ? null : value.Value.ToString("yyyy-MM-dd");
        }

        /// <summary>批量装载改进措施清单并推导复盘状态（避免列表页 N+1）。</summary>
        private void FillReviewItems(IDbConnection connection, List<EmergencyReviewDto> reviews)
        {
            foreach (var review in reviews) review.Items = new List<EmergencyReviewItemDto>();
            var ids = reviews.Select(r => r.Id).Distinct().ToList();
            if (ids.Count == 0) return;
            var items = connection.Query<EmergencyReviewItemDto>(
                "SELECT id, review_id AS ReviewId, content, owner, due_date AS DueDate, status, sort " +
                "FROM t_emergency_review_item WHERE review_id IN @ids ORDER BY sort, id", new { ids }).ToList();
            var map = reviews.ToDictionary(r => r.Id);
            foreach (var item in items)
            {
                item.StatusText = ItemStatusText(item.Status);
                EmergencyReviewDto review;
                if (map.TryGetValue(item.ReviewId, out review)) review.Items.Add(item);
            }
            foreach (var review in reviews) review.StatusText = ReviewStatusText(review.Items);
        }

        private static string ItemStatusText(int status)
        {
            return status == 1 ? "进行中" : (status == 2 ? "已完成" : "待开展");
        }

        private static string ReviewStatusText(List<EmergencyReviewItemDto> items)
        {
            if (items == null || items.Count == 0) return "待开展";
            if (items.All(i => i.Status == 2)) return "已完成";
            if (items.Any(i => i.Status == 1 || i.Status == 2)) return "进行中";
            return "待开展";
        }

        // ===================== 展示口径归一 =====================

        private static EmergencySceneDto SceneNormalize(EmergencySceneDto d)
        {
            if (d == null) return null;
            d.StatusText = d.Status == 1 ? "已停用" : "启用";
            return d;
        }

        private static EmergencyStepDto StepNormalize(EmergencyStepDto d)
        {
            if (d == null) return null;
            d.StatusText = d.Status == 0 ? "启用" : "停用";
            return d;
        }

        /// <summary>处置超时阈值（分钟）：Ⅰ 级 15、Ⅱ 级 30（原型 PG-EMG-02/03）、Ⅲ 级 60；旧数据 level&lt;=0 按 Ⅰ 级。</summary>
        internal static int OvertimeLimitMinutes(int level)
        {
            return level == 2 ? 30 : (level >= 3 ? 60 : 15);
        }

        private static EmergencyEventDto FillEventPerson(IDbConnection connection, EmergencyEventDto e)
        {
            if (e == null) return null;
            e.EventNo = string.IsNullOrEmpty(e.EventNo) ? "EM-" + e.Id.ToString("D4") : e.EventNo;
            e.StatusText = e.Status == EmergencyEventStatus.Initiated ? "已发起"
                : (e.Status == EmergencyEventStatus.Handling ? "处置中" : (e.Status == EmergencyEventStatus.Closed ? "已结案" : "已复盘"));
            // EmergencyEventLevel：1=Ⅰ 级（重大） 2=Ⅱ 级（较大） 3=Ⅲ 级（一般）；历史 0 按 Ⅰ 级
            e.LevelText = e.Level >= 3 ? "Ⅲ 级" : (e.Level == 2 ? "Ⅱ 级" : "Ⅰ 级");
            e.EventTimeText = e.EventTime.ToString("MM-dd HH:mm");
            var main = connection.QueryFirstOrDefault<EmergencyAssignDto>(
                "SELECT COALESCE(e2.name,'') AS EmployeeName FROM t_emergency_assign a LEFT JOIN t_employee e2 ON e2.id = a.employee_id " +
                "WHERE a.event_id = @eventId AND a.assign_type = 0 ORDER BY a.id LIMIT 1", new { eventId = e.Id });
            e.MainPerson = main?.EmployeeName ?? string.Empty;
            int elapsed = (int)(DateTime.Now - e.EventTime).TotalMinutes;
            e.IsOverdue = (e.Status == EmergencyEventStatus.Initiated || e.Status == EmergencyEventStatus.Handling)
                && elapsed > OvertimeLimitMinutes(e.Level);
            e.ElapsedText = e.Status == EmergencyEventStatus.Closed || e.Status == EmergencyEventStatus.Reviewed
                ? "已结案"
                : ("已 " + Math.Max(0, elapsed) + " 分钟");
            return e;
        }
    }
}
