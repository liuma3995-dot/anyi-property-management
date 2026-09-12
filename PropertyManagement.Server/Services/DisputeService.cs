using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Dispute;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 民事纠纷调解服务（M6 D6-4，UC-DIS-001~007）。
    /// 业务规则：BR-DIS-01（状态流）、BR-DIS-02（结案需处理方案）、BR-DIS-03（调解员引用员工）、
    /// BR-DIS-04（结案后可补录）、BR-DIS-05（当事人可引用业主或外部登记）、P-08（结案类型）。
    /// </summary>
    public class DisputeService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IDisputeRepository _repo;
        private readonly IDisputeAttachmentRepository _attachments;

        public DisputeService()
            : this(new SqliteConnectionFactory(), new SqlDisputeRepository(), new SqlDisputeAttachmentRepository())
        {
        }

        public DisputeService(IDbConnectionFactory connectionFactory, IDisputeRepository repo)
            : this(connectionFactory, repo, new SqlDisputeAttachmentRepository())
        {
        }

        public DisputeService(IDbConnectionFactory connectionFactory, IDisputeRepository repo,
            IDisputeAttachmentRepository attachments)
        {
            _connectionFactory = connectionFactory;
            _repo = repo;
            _attachments = attachments;
        }

        public List<DisputeTypeDto> ListTypes() =>
            WithConnection(c => _repo.ListTypes(c));

        public DisputeTypeDto SaveType(DisputeTypeRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("纠纷类型名称不能为空");
                var dto = new DisputeTypeDto { Name = request.Name.Trim(), Status = 0 };
                dto.Id = _repo.InsertType(c, tx, dto);
                return dto;
            });

        /// <summary>删除纠纷类型（软删）：先检查是否被案件引用，有引用则拒绝并提示先停用，避免影响历史案件。</summary>
        public void DeleteType(int typeId) =>
            WithTransaction((c, tx) =>
            {
                var existing = _repo.ListTypes(c).FirstOrDefault(t => t.Id == typeId);
                if (existing == null) throw ApiException.NotFound("纠纷类型不存在");
                int used = _repo.CountCasesByType(c, typeId);
                if (used > 0)
                    throw ApiException.Conflict("该纠纷类型已被 " + used + " 个案件引用，请先停用后再删除");
                _repo.DeleteType(c, tx, typeId);
            });

        public PageResult<DisputeCaseDto> QueryCases(DisputeQueryRequest query) =>
            WithConnection(c => _repo.QueryCases(c, query ?? new DisputeQueryRequest { PageIndex = 1, PageSize = 20 }, out int total));

        /// <summary>
        /// 案件详情：不限制案件状态（已登记/调解中/已结案均可查），
        /// 支持处理页侧栏直进与跨页传参（PG-DIS-03 竞态防护的后端配合），并返回状态时间线。
        /// </summary>
        public DisputeCaseDetailDto GetCase(int id) =>
            WithConnection(c =>
            {
                var dto = _repo.GetCase(c, id) ?? throw ApiException.NotFound("纠纷案件不存在");
                return new DisputeCaseDetailDto
                {
                    Case = dto,
                    Parties = _repo.ListParties(c, id),
                    Records = _repo.ListRecords(c, id),
                    StatusLogs = _repo.ListStatusLogs(c, id),
                    // F1：调解协议扫描件清单随详情一并返回（避免处理页二次请求）
                    Attachments = _attachments.List(c, id).Select(a => DecorateAttachment(a)).ToList()
                };
            });

        private static DisputeAttachmentDto DecorateAttachment(DisputeAttachmentDto dto)
        {
            if (dto == null)
            {
                return null;
            }
            dto.SizeText = DisputeAttachmentService.FormatSize(dto.SizeBytes);
            dto.UploadedAtText = dto.UploadedAt.ToString("yyyy-MM-dd HH:mm");
            return dto;
        }

        public DisputeCaseDto CreateCase(DisputeCaseCreateRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request.TypeId <= 0) throw ApiException.ValidationFailed("请选择纠纷类型");
                if (request.OccurTime == default) throw ApiException.ValidationFailed("请选择发生时间");
                var dto = new DisputeCaseDto
                {
                    TypeId = request.TypeId, OccurTime = request.OccurTime,
                    Location = request.Location ?? string.Empty, Detail = request.Detail ?? string.Empty,
                    MediatorId = request.MediatorId, PropertyId = request.PropertyId, ExpectedAt = request.ExpectedAt,
                    Level = request.Level, Status = DisputeCaseStatus.Registered
                };
                // 90 天同房产同类纠纷 → 软提示（原型为"提示历史记录并建议升级"，非阻断；含已结案，"90天内≥2次"口径）
                string warning = null;
                if (dto.PropertyId.HasValue && dto.PropertyId.Value > 0)
                {
                    int recent = _repo.CountActiveCasesByProperty(c, dto.PropertyId.Value, dto.TypeId, DateTime.Now.AddDays(-90));
                    if (recent >= 2)
                        warning = "该房产 90 天内已存在 " + recent + " 起同类纠纷，建议升级处理";
                }
                dto.Id = _repo.InsertCase(c, tx, dto);
                _repo.InsertStatusLog(c, tx, dto.Id, -1, (int)DisputeCaseStatus.Registered);
                SaveParties(c, tx, dto.Id, request.Parties);
                var result = _repo.GetCase(c, dto.Id);
                result.WarningText = warning;
                return result;
            });

        public DisputeCaseDto UpdateCase(int id, DisputeCaseUpdateRequest request) =>
            WithTransaction((c, tx) =>
            {
                var existing = _repo.GetCase(c, id) ?? throw ApiException.NotFound("纠纷案件不存在");
                if (existing.Status == DisputeCaseStatus.Closed)
                    throw ApiException.Conflict("已结案案件不可修改，只能补录");
                if (request.TypeId.HasValue) existing.TypeId = request.TypeId.Value;
                if (request.Level.HasValue) existing.Level = request.Level.Value;
                if (request.Location != null) existing.Location = request.Location;
                if (request.Detail != null) existing.Detail = request.Detail;
                if (request.MediatorId.HasValue) existing.MediatorId = request.MediatorId;
                if (request.PropertyId.HasValue) existing.PropertyId = request.PropertyId;
                if (request.ExpectedAt.HasValue) existing.ExpectedAt = request.ExpectedAt;
                _repo.UpdateCase(c, tx, existing);
                if (request.Parties != null)
                {
                    _repo.DeletePartiesByCase(c, tx, id);
                    SaveParties(c, tx, id, request.Parties);
                }
                return _repo.GetCase(c, id);
            });

        /// <summary>删除纠纷案件（软删）：列表不再呈现，历史数据保留；任意状态均可删除。</summary>
        public void DeleteCase(int id) =>
            WithTransaction((c, tx) =>
            {
                var existing = _repo.GetCase(c, id) ?? throw ApiException.NotFound("纠纷案件不存在");
                _repo.SoftDeleteCase(c, tx, existing.Id);
            });

        /// <summary>新增处理记录（UC-DIS-003，仅调解中案件）。方案摘要与处理内容至少一项（BR-DIS-03 六列）。</summary>
        public DisputeRecordDto AddRecord(DisputeRecordRequest request) =>
            WithTransaction((c, tx) =>
            {
                var existing = _repo.GetCase(c, request.CaseId) ?? throw ApiException.NotFound("纠纷案件不存在");
                if (existing.Status != DisputeCaseStatus.Handling)
                    throw ApiException.Conflict("仅处理中的案件可新增处理记录（已结案需走补录）");
                // 防越权旁路：普通记录端点固定忽略客户端 IsSupplement，补录一律走 /supplement（BR-DIS-04）
                ValidateRecordContent(request.PlanSummary, request.Content);
                var dto = new DisputeRecordDto
                {
                    CaseId = request.CaseId, RecordTime = request.RecordTime == default ? DateTime.Now : request.RecordTime,
                    Content = request.Content ?? string.Empty, Recorder = request.Recorder ?? "系统管理员",
                    Method = request.Method, PlanSummary = request.PlanSummary, PartyOpinion = request.PartyOpinion, Result = request.Result,
                    IsSupplement = false
                };
                dto.Id = _repo.InsertRecord(c, tx, dto);
                return dto;
            });

        /// <summary>结案后补录（UC-DIS-007 / BR-DIS-04）：仅管理员，补录原因必填，落补录标记与原因留痕。</summary>
        public DisputeRecordDto SupplementRecord(int caseId, DisputeSupplementRequest request, string operatorName)
        {
            // 当前系统仅内置 admin 管理员账号，以用户名判定管理员（后续接入角色体系时替换为角色校验）
            if (!string.Equals((operatorName ?? string.Empty).Trim(), "admin", StringComparison.OrdinalIgnoreCase))
                throw ApiException.Forbidden("仅管理员可执行结案后补录（BR-DIS-04）");
            if (request == null || string.IsNullOrWhiteSpace(request.Reason))
                throw ApiException.ValidationFailed("补录原因不能为空（BR-DIS-04）");
            ValidateRecordContent(request.PlanSummary, request.Content);
            return WithTransaction((c, tx) =>
            {
                var existing = _repo.GetCase(c, caseId) ?? throw ApiException.NotFound("纠纷案件不存在");
                if (existing.Status != DisputeCaseStatus.Closed)
                    throw ApiException.Conflict("仅已结案案件可补录处理记录（BR-DIS-04）");
                var dto = new DisputeRecordDto
                {
                    CaseId = caseId, RecordTime = DateTime.Now,
                    Content = request.Content ?? string.Empty,
                    Method = request.Method, PlanSummary = request.PlanSummary, PartyOpinion = request.PartyOpinion, Result = request.Result,
                    Recorder = string.IsNullOrWhiteSpace(operatorName) ? "admin" : operatorName.Trim(),
                    IsSupplement = true, SupplementReason = request.Reason.Trim()
                };
                dto.Id = _repo.InsertRecord(c, tx, dto);
                return dto;
            });
        }

        /// <summary>调解员推荐（BR-DIS-03）：在岗员工 + 历史结案成功率。</summary>
        public List<DisputeMediatorDto> RecommendMediators(int? typeId, int? propertyId) =>
            WithConnection(c => _repo.RecommendMediators(c, typeId, propertyId));

        /// <summary>状态流转（BR-DIS-01 单向流：已登记→处理中；结案走 /close，不允许绕过）。</summary>
        public DisputeCaseDto UpdateStatus(int id, DisputeCaseStatusRequest request) =>
            WithTransaction((c, tx) =>
            {
                var existing = _repo.GetCase(c, id) ?? throw ApiException.NotFound("纠纷案件不存在");
                int oldStatus = (int)existing.Status;
                if (request.Status == DisputeCaseStatus.Handling && existing.Status == DisputeCaseStatus.Registered)
                {
                    existing.Status = DisputeCaseStatus.Handling;
                }
                else if (request.Status != existing.Status)
                {
                    throw ApiException.ValidationFailed("非法状态流转：纠纷状态仅允许 已登记→处理中→已结案（BR-DIS-01），结案请调用结案端点");
                }
                _repo.UpdateCase(c, tx, existing);
                if ((int)existing.Status != oldStatus)
                    _repo.InsertStatusLog(c, tx, id, oldStatus, (int)existing.Status);
                return _repo.GetCase(c, id);
            });

        /// <summary>结案（UC-DIS-005 / BR-DIS-01/02 / P-08）：仅调解中案件可结案，结案报告必填并持久化，结案生成复盘提醒。</summary>
        public DisputeCaseDto CloseCase(int id, DisputeCloseRequest request) =>
            WithTransaction((c, tx) =>
            {
                var existing = _repo.GetCase(c, id) ?? throw ApiException.NotFound("纠纷案件不存在");
                if (existing.Status == DisputeCaseStatus.Closed)
                    throw ApiException.Conflict("该案件已结案");
                if (existing.Status != DisputeCaseStatus.Handling)
                    throw ApiException.Conflict("已登记案件须先受理为调解中后再结案（BR-DIS-01 状态流）");
                if (!Enum.IsDefined(typeof(DisputeCloseType), request.CloseType))
                    throw ApiException.ValidationFailed("请选择有效的结案类型（调解成功/自行和解/转办）");
                if (string.IsNullOrWhiteSpace(request.Summary))
                    throw ApiException.ValidationFailed("结案报告不能为空");
                // BR-DIS-02：至少一条处理方案记录（方案摘要或内容非空任一）
                var records = _repo.ListRecords(c, id);
                if (!records.Any(r => !string.IsNullOrWhiteSpace(r.PlanSummary) || !string.IsNullOrWhiteSpace(r.Content)))
                    throw ApiException.Conflict("结案前必须存在至少一条处理方案记录（BR-DIS-02）");
                int oldStatus = (int)existing.Status;
                existing.Status = DisputeCaseStatus.Closed;
                existing.CloseType = request.CloseType;
                existing.ClosedAt = DateTime.Now;
                existing.CloseSummary = request.Summary.Trim();
                _repo.UpdateCase(c, tx, existing);
                _repo.InsertStatusLog(c, tx, id, oldStatus, (int)DisputeCaseStatus.Closed);
                // 结案生成复盘提醒（T6-4-4，7 天后到期；通知基建就位后由提醒中心推送客服主管）
                _repo.InsertReminder(c, tx, "dispute_review", id, DateTime.Now.AddDays(7));
                return _repo.GetCase(c, id);
            });

        public DisputeStatisticsDto Statistics() =>
            WithConnection(c => _repo.Statistics(c));

        private void SaveParties(IDbConnection c, IDbTransaction tx, int caseId, List<DisputePartyDto> parties)
        {
            if (parties == null) return;
            foreach (var p in parties)
            {
                if (string.IsNullOrWhiteSpace(p.Name)) continue;
                // P0 修复：客户端提交 "1"=乙方（或历史文本"乙方"），其余视为甲方；此前 "1" 被误判为甲方导致乙方全存成甲方
                _repo.InsertParty(c, tx, new DisputePartyDto
                {
                    CaseId = caseId,
                    PartyType = string.IsNullOrWhiteSpace(p.PartyType) ? "0" : ((p.PartyType.Trim() == "1" || p.PartyType.Trim() == "乙方") ? "1" : "0"),
                    OwnerId = p.OwnerId, Name = p.Name.Trim(), Phone = p.Phone ?? string.Empty
                });
            }
        }

        /// <summary>BR-DIS-03：方案摘要与处理内容至少填写一项（方案摘要为原型主列）。</summary>
        private static void ValidateRecordContent(string planSummary, string content)
        {
            if (string.IsNullOrWhiteSpace(planSummary) && string.IsNullOrWhiteSpace(content))
                throw ApiException.ValidationFailed("方案摘要与处理内容至少填写一项");
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
