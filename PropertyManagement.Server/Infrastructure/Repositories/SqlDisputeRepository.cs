using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Dispute;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>民事纠纷调解仓储 SQLite 实现（M6 D6-4，Dapper）。</summary>
    public class SqlDisputeRepository : IDisputeRepository
    {
        public List<DisputeTypeDto> ListTypes(IDbConnection connection)
        {
            return connection.Query<DisputeTypeDto>(
                "SELECT id, name, status FROM t_dispute_type WHERE del_flag = 0 AND status = 0 ORDER BY id").ToList();
        }

        public int InsertType(IDbConnection connection, IDbTransaction transaction, DisputeTypeDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_dispute_type (name, status, del_flag) VALUES (@Name, 0, 0); SELECT last_insert_rowid();",
                new { dto.Name }, transaction);
        }

        /// <summary>绾犵悍绫诲瀷涓嬫湁澶勭悊涓€佸凡缁撴妗堜欢鏁帮紙鍏ㄩ儴鐨勬秷鎭笉璁板叆锛夈€?/summary>
        public int CountCasesByType(IDbConnection connection, int typeId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_dispute_case WHERE del_flag = 0 AND type_id = @typeId",
                new { typeId });
        }

        /// <summary>杞垹闄ょ籂绾风被鍨嬶紙del_flag=1锛夛紝閬垮厤褰卞搷鍘嗗彶妗堜欢銆?/summary>
        public void DeleteType(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_dispute_type SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                new { id }, transaction);
        }

        private const string CaseBaseSql =
            "SELECT c.id, c.type_id AS TypeId, c.occur_time AS OccurTime, c.location, c.detail, c.status, " +
            "c.close_type AS CloseType, c.mediator_id AS MediatorId, c.closed_at AS ClosedAt, c.expected_at AS ExpectedAt, c.level, " +
            "c.close_summary AS CloseSummary, " +
            "COALESCE(t.name,'') AS TypeName, COALESCE(e.name,'') AS MediatorName, c.property_id AS PropertyId, " +
            "COALESCE(b.building_no,'') || '-' || COALESCE(u.unit_no,'') || '-' || COALESCE(p.room_no,'') AS PropertyPath " +
            "FROM t_dispute_case c " +
            "LEFT JOIN t_dispute_type t ON t.id = c.type_id " +
            "LEFT JOIN t_employee e ON e.id = c.mediator_id " +
            "LEFT JOIN t_property p ON p.id = c.property_id " +
            "LEFT JOIN t_unit u ON u.id = p.unit_id " +
            "LEFT JOIN t_building b ON b.id = u.building_id";

        public DisputeCaseDto GetCase(IDbConnection connection, int id)
        {
            var dto = connection.QueryFirstOrDefault<DisputeCaseDto>(
                CaseBaseSql + " WHERE c.id = @id AND c.del_flag = 0", new { id });
            if (dto == null) return null;
            dto.PartySummary = BuildPartySummary(connection, id);
            dto.RecordCount = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_dispute_record WHERE case_id = @id", new { id });
            return NormalizeCase(dto);
        }

        public PageResult<DisputeCaseDto> QueryCases(IDbConnection connection, DisputeQueryRequest query, out int total)
        {
            string where = "WHERE c.del_flag = 0";
            var p = new DynamicParameters();
            if (query.Status.HasValue) { where += " AND c.status = @status"; p.Add("status", (int)query.Status.Value); }
            if (query.TypeId.HasValue) { where += " AND c.type_id = @typeId"; p.Add("typeId", query.TypeId.Value); }
            if (!string.IsNullOrWhiteSpace(query.OwnerName))
            {
                string kw = "%" + EscapeLike(query.OwnerName.Trim()) + "%";
                where += " AND (c.detail LIKE @kw ESCAPE '\\' OR EXISTS (SELECT 1 FROM t_dispute_party dp WHERE dp.case_id = c.id AND dp.name LIKE @kw ESCAPE '\\'))";
                p.Add("kw", kw);
            }
            // 编号搜索：CaseNo = "JF-" + yyMM(occur_time) + "-" + id（与 NormalizeCase 一致）；同时保留当事人/电话模糊匹配
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                string kw = "%" + EscapeLike(query.Keyword.Trim()) + "%";
                where += " AND (('JF-'||substr(strftime('%Y',c.occur_time),3,2)||strftime('%m',c.occur_time)||'-'||c.id) LIKE @idkw ESCAPE '\\'" +
                         " OR c.detail LIKE @kw ESCAPE '\\'" +
                         " OR EXISTS (SELECT 1 FROM t_dispute_party dp WHERE dp.case_id = c.id AND (dp.name LIKE @kw ESCAPE '\\' OR dp.phone LIKE @kw ESCAPE '\\')))";
                p.Add("kw", kw); p.Add("idkw", kw);
            }
            if (query.From.HasValue) { where += " AND c.occur_time >= @from"; p.Add("from", query.From.Value.ToString("yyyy-MM-dd")); }
            // To 含当天：occur_time < date(@to,'+1 day')（occur_time 为 'yyyy-MM-dd HH:mm:ss' 文本，可直接比较）
            if (query.To.HasValue) { where += " AND c.occur_time < date(@to,'+1 day')"; p.Add("to", query.To.Value.ToString("yyyy-MM-dd")); }
            total = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_dispute_case c " + where, p);
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;
            p.Add("limit", pageSize); p.Add("offset", offset);
            var items = connection.Query<DisputeCaseDto>(
                CaseBaseSql + " " + where + " ORDER BY c.id DESC LIMIT @limit OFFSET @offset", p).Select(d => { d.PartySummary = BuildPartySummary(connection, d.Id); d.RecordCount = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_dispute_record WHERE case_id = @id", new { id = d.Id }); return NormalizeCase(d); }).ToList();
            return new PageResult<DisputeCaseDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }

        public int InsertCase(IDbConnection connection, IDbTransaction transaction, DisputeCaseDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_dispute_case (type_id, occur_time, location, detail, status, mediator_id, property_id, expected_at, level, del_flag) " +
                "VALUES (@TypeId, @OccurTime, @Location, @Detail, @Status, @MediatorId, @PropertyId, @ExpectedAt, @Level, 0); SELECT last_insert_rowid();",
                new { dto.TypeId, OccurTime = dto.OccurTime.ToString("yyyy-MM-dd HH:mm:ss"), dto.Location, dto.Detail, Status = (int)dto.Status, dto.MediatorId, dto.PropertyId, ExpectedAt = dto.ExpectedAt?.ToString("yyyy-MM-dd HH:mm:ss"), dto.Level }, transaction);
        }

        public void UpdateCase(IDbConnection connection, IDbTransaction transaction, DisputeCaseDto dto)
        {
            connection.Execute(
                "UPDATE t_dispute_case SET type_id = @TypeId, location = @Location, detail = @Detail, mediator_id = @MediatorId, " +
                "property_id = @PropertyId, expected_at = @ExpectedAt, level = @Level, status = @Status, close_type = @CloseType, " +
                "closed_at = @ClosedAt, close_summary = @CloseSummary, updated_at = datetime('now','localtime') " +
                "WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.TypeId, dto.Location, dto.Detail, dto.MediatorId, dto.PropertyId, ExpectedAt = dto.ExpectedAt?.ToString("yyyy-MM-dd HH:mm:ss"), dto.Level, Status = (int)dto.Status, CloseType = dto.CloseType, ClosedAt = dto.ClosedAt?.ToString("yyyy-MM-dd HH:mm:ss"), CloseSummary = dto.CloseSummary }, transaction);
        }

        /// <summary>软删纠纷案件（del_flag=1），列表与统计不再计入，历史数据保留。</summary>
        public void SoftDeleteCase(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_dispute_case SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                new { id }, transaction);
        }

        public List<DisputePartyDto> ListParties(IDbConnection connection, int caseId)
        {
            return connection.Query<DisputePartyDto>(
                "SELECT id, case_id AS CaseId, CAST(party_type AS TEXT) AS PartyType, owner_id AS OwnerId, name, phone " +
                "FROM t_dispute_party WHERE case_id = @caseId ORDER BY party_type", new { caseId })
                .Select(NormalizeParty).ToList();
        }

        public void InsertParty(IDbConnection connection, IDbTransaction transaction, DisputePartyDto dto)
        {
            connection.Execute(
                "INSERT INTO t_dispute_party (case_id, party_type, owner_id, name, phone) " +
                "VALUES (@CaseId, @PartyType, @OwnerId, @Name, @Phone)",
                new { dto.CaseId, dto.PartyType, dto.OwnerId, dto.Name, dto.Phone }, transaction);
        }

        public void DeletePartiesByCase(IDbConnection connection, IDbTransaction transaction, int caseId)
        {
            connection.Execute("DELETE FROM t_dispute_party WHERE case_id = @caseId", new { caseId }, transaction);
        }

        public List<DisputeRecordDto> ListRecords(IDbConnection connection, int caseId)
        {
            return connection.Query<DisputeRecordDto>(
                "SELECT id, case_id AS CaseId, record_time AS RecordTime, content, recorder, is_supplement AS IsSupplement, " +
                "COALESCE(method,'') AS Method, COALESCE(plan_summary,'') AS PlanSummary, " +
                "COALESCE(party_opinion,'') AS PartyOpinion, COALESCE(result,'') AS Result, " +
                "COALESCE(supplement_reason,'') AS SupplementReason " +
                "FROM t_dispute_record WHERE case_id = @caseId ORDER BY record_time, id", new { caseId }).ToList();
        }

        public int InsertRecord(IDbConnection connection, IDbTransaction transaction, DisputeRecordDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_dispute_record (case_id, record_time, content, recorder, is_supplement, method, plan_summary, party_opinion, result, supplement_reason) " +
                "VALUES (@CaseId, @RecordTime, @Content, @Recorder, @IsSupplement, @Method, @PlanSummary, @PartyOpinion, @Result, @SupplementReason); SELECT last_insert_rowid();",
                new { dto.CaseId, RecordTime = dto.RecordTime.ToString("yyyy-MM-dd HH:mm:ss"), dto.Content, dto.Recorder, IsSupplement = dto.IsSupplement ? 1 : 0,
                      Method = dto.Method ?? string.Empty, PlanSummary = dto.PlanSummary ?? string.Empty, PartyOpinion = dto.PartyOpinion ?? string.Empty,
                      Result = dto.Result ?? string.Empty, SupplementReason = dto.SupplementReason ?? string.Empty }, transaction);
        }

        public void InsertStatusLog(IDbConnection connection, IDbTransaction transaction, int caseId, int oldStatus, int newStatus)
        {
            connection.Execute(
                "INSERT INTO t_dispute_status_log (case_id, old_status, new_status) VALUES (@caseId, @oldStatus, @newStatus)",
                new { caseId, oldStatus, newStatus }, transaction);
        }

        public List<DisputeStatusLogDto> ListStatusLogs(IDbConnection connection, int caseId)
        {
            return connection.Query<DisputeStatusLogDto>(
                "SELECT id, case_id AS CaseId, old_status AS OldStatus, new_status AS NewStatus, changed_at AS ChangedAt " +
                "FROM t_dispute_status_log WHERE case_id = @caseId ORDER BY id DESC", new { caseId }).ToList();
        }

        public DisputeStatisticsDto Statistics(IDbConnection connection)
        {
            var stat = new DisputeStatisticsDto { ByType = new Dictionary<string, int>(), SuccessRateNote = "近12个月" };
            stat.Total = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_dispute_case WHERE del_flag = 0");
            stat.Registered = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_dispute_case WHERE del_flag = 0 AND status = 0");
            stat.Handling = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_dispute_case WHERE del_flag = 0 AND status = 1");
            stat.Closed = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_dispute_case WHERE del_flag = 0 AND status = 2");
            foreach (var row in connection.Query<TypeCountRow>("SELECT t.name AS Name, COUNT(c.id) AS Cnt FROM t_dispute_case c JOIN t_dispute_type t ON t.id = c.type_id WHERE c.del_flag = 0 GROUP BY t.name"))
            { stat.ByType[row.Name] = row.Cnt; }

            string monthStart = DateTime.Now.ToString("yyyy-MM-01");
            string lastMonthStart = DateTime.Now.AddMonths(-1).ToString("yyyy-MM-01");
            string twelveMonthsAgo = DateTime.Now.AddMonths(-12).ToString("yyyy-MM-dd HH:mm:ss");
            string overdueBefore = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd HH:mm:ss");
            // 本月新增 + 环比（上月新增）
            stat.MonthNew = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_dispute_case WHERE del_flag = 0 AND occur_time >= @monthStart", new { monthStart });
            int lastMonthNew = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_dispute_case WHERE del_flag = 0 AND occur_time >= @lastMonthStart AND occur_time < @monthStart",
                new { lastMonthStart, monthStart });
            stat.MonthNewDelta = stat.MonthNew - lastMonthNew;
            // 本月结案（统计卡"已结案·本月"标签）
            stat.MonthClosed = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_dispute_case WHERE del_flag = 0 AND status = 2 AND closed_at >= @monthStart", new { monthStart });
            // 超期：调解中且发生时间 < now-30天（PG-DIS-01"含超期"）
            stat.OverdueCount = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_dispute_case WHERE del_flag = 0 AND status = 1 AND occur_time < @overdueBefore", new { overdueBefore });
            // 调解成功率：近 12 个月结案中 close_type=调解成功(0) 占比
            int closedIn12 = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_dispute_case WHERE del_flag = 0 AND status = 2 AND closed_at >= @twelveMonthsAgo", new { twelveMonthsAgo });
            int successIn12 = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_dispute_case WHERE del_flag = 0 AND status = 2 AND close_type = 0 AND closed_at >= @twelveMonthsAgo", new { twelveMonthsAgo });
            stat.SuccessRate = closedIn12 == 0 ? 0 : Math.Round(successIn12 * 100.0 / closedIn12, 1);
            return stat;
        }

        private class TypeCountRow
        {
            public string Name { get; set; }
            public int Cnt { get; set; }
        }

        /// <summary>90 天内同房产同类案件数（含已结案，仅作软提示口径 BR-DIS-02 原型"提示并建议升级"）。</summary>
        public int CountActiveCasesByProperty(IDbConnection connection, int propertyId, int typeId, DateTime since)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_dispute_case WHERE del_flag = 0 AND property_id = @propertyId AND type_id = @typeId AND occur_time >= @since",
                new { propertyId, typeId, since = since.ToString("yyyy-MM-dd HH:mm:ss") });
        }

        public List<DisputeMediatorDto> RecommendMediators(IDbConnection connection, int? typeId, int? propertyId)
        {
            // 在岗员工（status=0 在职，del_flag=0）+ 其作为调解员的历史结案统计；
            // typeId 下推到 LEFT JOIN 条件：仅统计该类型的历史案件（原型"漏水类成功率92%"口径）。propertyId 暂不参与口径（原型"片区A"属性，留待片区模型）。
            const string sql =
                "SELECT e.id AS EmployeeId, e.name AS Name, COALESCE(pos.name,'') AS PositionName, COALESCE(d.name,'') AS DeptName, " +
                "COUNT(c.id) AS CaseCount, COALESCE(SUM(CASE WHEN c.close_type = 0 THEN 1 ELSE 0 END), 0) AS SuccessCount " +
                "FROM t_employee e " +
                "LEFT JOIN t_position pos ON pos.id = e.position_id " +
                "LEFT JOIN t_department d ON d.id = e.dept_id " +
                "LEFT JOIN t_dispute_case c ON c.mediator_id = e.id AND c.del_flag = 0 AND c.status = 2 " +
                "  AND (@typeId IS NULL OR c.type_id = @typeId) " +
                "WHERE e.del_flag = 0 AND e.status = 0 " +
                "GROUP BY e.id, e.name, pos.name, d.name " +
                "ORDER BY SuccessCount DESC, CaseCount DESC, e.id";
            var rows = connection.Query<MediatorRow>(sql, new { typeId }).ToList();
            return rows.Select(r => new DisputeMediatorDto
            {
                EmployeeId = r.EmployeeId,
                Name = r.Name ?? string.Empty,
                PositionName = r.PositionName ?? string.Empty,
                DeptName = r.DeptName ?? string.Empty,
                CaseCount = r.CaseCount,
                SuccessRate = r.CaseCount == 0 ? 0 : Math.Round(r.SuccessCount * 100.0 / r.CaseCount, 1)
            }).ToList();
        }

        private class MediatorRow
        {
            public int EmployeeId { get; set; }
            public string Name { get; set; }
            public string PositionName { get; set; }
            public string DeptName { get; set; }
            public int CaseCount { get; set; }
            public int SuccessCount { get; set; }
        }

        public void InsertReminder(IDbConnection connection, IDbTransaction transaction, string type, int targetId, DateTime dueAt)
        {
            connection.Execute(
                "INSERT INTO t_reminder (type, target_id, due_at, status) VALUES (@type, @targetId, @dueAt, 0)",
                new { type, targetId, dueAt = dueAt.ToString("yyyy-MM-dd HH:mm:ss") }, transaction);
        }

        private string BuildPartySummary(IDbConnection connection, int caseId)
        {
            var parties = connection.Query<DisputePartyDto>(
                "SELECT CAST(party_type AS TEXT) AS PartyType, name FROM t_dispute_party WHERE case_id = @caseId ORDER BY party_type", new { caseId }).ToList();
            if (parties.Count == 0) return string.Empty;
            return string.Join(" / ", parties.Select(x => x.Name ?? string.Empty));
        }

        private static DisputeCaseDto NormalizeCase(DisputeCaseDto dto)
        {
            if (dto == null) return null;
            dto.CaseNo = "JF-" + dto.OccurTime.ToString("yyMM") + "-" + dto.Id;
            dto.LevelText = dto.Level == 1 ? "较大" : (dto.Level == 2 ? "重大" : "一般");
            dto.StatusText = dto.Status == DisputeCaseStatus.Registered ? "待调解" : (dto.Status == DisputeCaseStatus.Handling ? "调解中" : "已结案");
            dto.CloseTypeText = dto.CloseType.HasValue ? (dto.CloseType.Value == DisputeCloseType.Mediated ? "调解成功" : (dto.CloseType.Value == DisputeCloseType.Settled ? "自行和解" : "转办")) : string.Empty;
            dto.OccurTimeText = dto.OccurTime.ToString("MM-dd");
            dto.ExpectedAtText = dto.ExpectedAt.HasValue ? dto.ExpectedAt.Value.ToString("yyyy-MM-dd") : string.Empty;
            // 超期标记：调解中且发生时间超过 30 天（PG-DIS-01，服务端统一口径）
            dto.IsOverdue = dto.Status == DisputeCaseStatus.Handling && dto.OccurTime < DateTime.Now.AddDays(-30);
            return dto;
        }

        private static DisputePartyDto NormalizeParty(DisputePartyDto p)
        {
            if (p == null) return null;
            // 兼容展示：party_type 0=甲方 1=乙方（乙方另兼容历史文本"乙方"）；历史数据修复 SQL 由中央统一执行
            p.PartyTypeText = string.Equals(p.PartyType, "1") || string.Equals(p.PartyType, "乙方", StringComparison.Ordinal) ? "乙方" : "甲方";
            return p;
        }

        /// <summary>LIKE 通配符转义（配合 ESCAPE '\'），防用户输入 %/_ 干扰匹配。</summary>
        private static string EscapeLike(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }
    }
}
