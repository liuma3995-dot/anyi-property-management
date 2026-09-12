using System;
using System.Collections.Generic;
using System.Data;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Emergency;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>应急处置仓储（M6 D6-1，UC-EMG-001~008 + BR-EMG-01~06）。</summary>
    public interface IEmergencyRepository
    {
        List<EmergencySceneDto> ListScenes(IDbConnection connection, string keyword, int? status);
        EmergencySceneDto GetScene(IDbConnection connection, int id);
        int InsertScene(IDbConnection connection, IDbTransaction transaction, EmergencySceneDto dto);
        void UpdateScene(IDbConnection connection, IDbTransaction transaction, EmergencySceneDto dto);
        void SetSceneStatus(IDbConnection connection, IDbTransaction transaction, int id, int status);
        /// <summary>场景被【进行中】事件引用计数（status∈0,1 且 del_flag=0，删除前校验 409；已结案/已复盘不影响）。</summary>
        int CountSceneEvents(IDbConnection connection, int sceneId);
        void SoftDeleteScene(IDbConnection connection, IDbTransaction transaction, int id);

        List<EmergencyStepDto> ListSteps(IDbConnection connection, int sceneId);
        EmergencyStepDto GetStep(IDbConnection connection, int id);
        int InsertStep(IDbConnection connection, IDbTransaction transaction, EmergencyStepDto dto);
        void UpdateStep(IDbConnection connection, IDbTransaction transaction, EmergencyStepDto dto);
        void SoftDeleteStep(IDbConnection connection, IDbTransaction transaction, int id);
        /// <summary>BR-EMG-05：旧版本留档（status=1），返回被留档行的 step_no。</summary>
        int SupersedeStep(IDbConnection connection, IDbTransaction transaction, int id);
        /// <summary>BR-EMG-05：取同一场景同一序号下的最大版本号（如 v3）。</summary>
        int GetMaxStepVersion(IDbConnection connection, IDbTransaction transaction, int sceneId, int stepNo);
        /// <summary>步骤拖拽排序持久化：按给定 id 顺序重排 step_no（原地更新，不产生新版本）。</summary>
        void ReorderSteps(IDbConnection connection, IDbTransaction transaction, int sceneId, List<int> orderedIds);

        /// <summary>BR-EMG-03：在职员工 + 当日已发布排班候选（OnDuty 在岗优先，跨读 t_schedule/t_position/t_shift）。</summary>
        List<EmergencyMatchDto> ListDutyCandidates(IDbConnection connection, DateTime date);
        /// <summary>读取系统参数（如 P-03 emergency.review.deadline.workdays）。</summary>
        string GetParamValue(IDbConnection connection, string key);

        PageResult<EmergencyEventDto> QueryEvents(IDbConnection connection, EmergencyEventQueryRequest query, out int total);
        EmergencyEventDto GetEvent(IDbConnection connection, int id);
        int InsertEvent(IDbConnection connection, IDbTransaction transaction, EmergencyEventDto dto);
        /// <summary>事件编号落库（InsertEvent 后回填 EM-yyMM-id，保证 DB 非空、关键字搜索可用）。</summary>
        void UpdateEventNo(IDbConnection connection, IDbTransaction transaction, int id, string eventNo);
        void UpdateEventStatus(IDbConnection connection, IDbTransaction transaction, int id, EmergencyEventStatus status, bool recordPending);
        /// <summary>结案留档：落 close_summary 并同步写 closed_at（BR-EMG-02/P-03）。</summary>
        void SetCloseSummary(IDbConnection connection, IDbTransaction transaction, int id, string summary);
        /// <summary>撤销：软删（del_flag=1），仅「已发起」且 created_at 60 秒内生效，返回受影响行数。</summary>
        int CancelEvent(IDbConnection connection, IDbTransaction transaction, int id);
        /// <summary>状态日志留痕（migration_019：operator/action 列）。</summary>
        void InsertStatusLog(IDbConnection connection, IDbTransaction transaction, int eventId, int oldStatus, int newStatus, string action, string operatorName);

        /// <summary>工作台汇总统计（进行中/超时/今日新增/本月已结案，T6-1-7）。</summary>
        EmergencyEventStatsDto GetEventStats(IDbConnection connection);
        /// <summary>已结案且无复盘事件的 closed_at 集合（P-03 工作日超期计算用）。</summary>
        List<DateTime> ListUnreviewedClosedAt(IDbConnection connection);

        List<EmergencyAssignDto> ListAssignments(IDbConnection connection, int eventId);
        void InsertAssignment(IDbConnection connection, IDbTransaction transaction, EmergencyAssignDto dto);
        List<EmergencyRecordDto> ListRecords(IDbConnection connection, int eventId);
        int InsertRecord(IDbConnection connection, IDbTransaction transaction, EmergencyRecordDto dto);

        EmergencyReviewDto GetReview(IDbConnection connection, int eventId);
        PageResult<EmergencyReviewDto> QueryReviews(IDbConnection connection, PageRequest query, out int total);
        /// <summary>复盘落库（含 review_no/host/plan/completion），返回复盘 id；新插入自动生成 FP-yyMM-id。</summary>
        int UpsertReview(IDbConnection connection, IDbTransaction transaction, EmergencyReviewDto dto);
        /// <summary>改进措施清单全量保存（先删后插，PG-EMG-04）。</summary>
        void ReplaceReviewItems(IDbConnection connection, IDbTransaction transaction, int reviewId, List<EmergencyReviewItemDto> items);
    }
}
