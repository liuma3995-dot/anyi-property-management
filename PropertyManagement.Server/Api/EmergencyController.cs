using System.Collections.Generic;
using System.Web.Http;
using Microsoft.Owin;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Emergency;
using PropertyManagement.Server.Api.Middleware;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>应急处置端点（M6 D6-1，UC-EMG-001~008 + BR-EMG-01~06）。</summary>
    [RoutePrefix("api/v1/emergency")]
    public class EmergencyController : ApiController
    {
        private readonly EmergencyService _service;

        public EmergencyController()
        {
            _service = new EmergencyService();
        }

        // ===================== 场景与步骤 =====================

        [HttpGet] [Route("scenes")]
        public ApiResponse<List<EmergencySceneDto>> ListScenes(string keyword = null, int? status = null) =>
            ApiResponse<List<EmergencySceneDto>>.Ok(_service.ListScenes(keyword, status));

        [HttpPost] [Route("scenes")]
        public ApiResponse<EmergencySceneDto> CreateScene(EmergencySceneRequest request) =>
            ApiResponse<EmergencySceneDto>.Ok(_service.SaveScene(0, request));

        [HttpPut] [Route("scenes/{id:int}")]
        public ApiResponse<EmergencySceneDto> UpdateScene(int id, EmergencySceneRequest request) =>
            ApiResponse<EmergencySceneDto>.Ok(_service.SaveScene(id, request));

        /// <summary>场景启用/停用（BR-EMG-01：停用后不可新发起）。</summary>
        [HttpPost] [Route("scenes/{id:int}/status")]
        public ApiResponse<EmergencySceneDto> SetSceneStatus(int id, EmergencySceneStatusRequest request) =>
            ApiResponse<EmergencySceneDto>.Ok(_service.SetSceneStatus(id, (request ?? new EmergencySceneStatusRequest()).Status));

        [HttpDelete] [Route("scenes/{id:int}")]
        public ApiResponse<object> DeleteScene(int id) { _service.DeleteScene(id); return ApiResponse<object>.Ok(null); }

        [HttpGet] [Route("scenes/{id:int}/steps")]
        public ApiResponse<List<EmergencyStepDto>> ListSteps(int id) =>
            ApiResponse<List<EmergencyStepDto>>.Ok(_service.ListSteps(id));

        [HttpPost] [Route("scenes/{id:int}/steps")]
        public ApiResponse<EmergencyStepDto> CreateStep(int id, EmergencyStepRequest request) =>
            ApiResponse<EmergencyStepDto>.Ok(_service.SaveStep(0, id, request));

        [HttpPut] [Route("steps/{id:int}")]
        public ApiResponse<EmergencyStepDto> UpdateStep(int id, EmergencyStepRequest request)
        {
            int sceneId = request == null ? 0 : request.SceneId;
            return ApiResponse<EmergencyStepDto>.Ok(_service.SaveStep(id, sceneId, request ?? new EmergencyStepRequest()));
        }

        /// <summary>步骤拖拽排序持久化（body：按目标顺序排列的步骤 id 数组，T6-1-2）。</summary>
        [HttpPost] [Route("scenes/{id:int}/steps/reorder")]
        public ApiResponse<object> ReorderSteps(int id, List<int> orderedIds)
        {
            _service.ReorderSteps(id, orderedIds);
            return ApiResponse<object>.Ok(null);
        }

        [HttpDelete] [Route("steps/{id:int}")]
        public ApiResponse<object> DeleteStep(int id) { _service.DeleteStep(id); return ApiResponse<object>.Ok(null); }

        /// <summary>自动匹配责任人（BR-EMG-03：按步骤角色 + 当日已发布排班 + 在岗，PG-EMG-02 右列）。</summary>
        [HttpGet] [Route("scenes/{id:int}/match")]
        public ApiResponse<List<EmergencyMatchDto>> MatchResponsible(int id) =>
            ApiResponse<List<EmergencyMatchDto>>.Ok(_service.MatchResponsible(id));

        // ===================== 事件 =====================

        [HttpGet] [Route("events")]
        public ApiResponse<PageResult<EmergencyEventDto>> QueryEvents([FromUri] EmergencyEventQueryRequest request) =>
            ApiResponse<PageResult<EmergencyEventDto>>.Ok(_service.QueryEvents(request ?? new EmergencyEventQueryRequest()));

        /// <summary>工作台汇总统计（进行中含超时/今日新增/本月已结案/待复盘，T6-1-7、P-03）。</summary>
        [HttpGet] [Route("events/stats")]
        public ApiResponse<EmergencyEventStatsDto> GetEventStats() =>
            ApiResponse<EmergencyEventStatsDto>.Ok(_service.GetEventStats());

        [HttpGet] [Route("events/{id:int}")]
        public ApiResponse<EmergencyEventDetailDto> GetEvent(int id) => ApiResponse<EmergencyEventDetailDto>.Ok(_service.GetEvent(id));

        [HttpPost] [Route("events")]
        public ApiResponse<EmergencyEventDetailDto> CreateEvent(EmergencyEventCreateRequest request) =>
            ApiResponse<EmergencyEventDetailDto>.Ok(_service.CreateEvent(request, GetCurrentUsername()));

        [HttpPost] [Route("events/{id:int}/assign")]
        public ApiResponse<EmergencyEventDetailDto> AssignResponsible(int id, EmergencyAssignRequest request) =>
            ApiResponse<EmergencyEventDetailDto>.Ok(_service.AssignResponsible(id, request ?? new EmergencyAssignRequest()));

        /// <summary>误发起撤销（仅「已发起」且 60 秒内，软删 + 状态日志留痕）。</summary>
        [HttpPost] [Route("events/{id:int}/cancel")]
        public ApiResponse<object> CancelEvent(int id)
        {
            _service.CancelEvent(id, GetCurrentUsername());
            return ApiResponse<object>.Ok(null);
        }

        [HttpPost] [Route("events/{id:int}/records")]
        public ApiResponse<EmergencyRecordDto> AddRecord(int id, EmergencyRecordRequest request)
        {
            if (request != null) request.EventId = id;
            return ApiResponse<EmergencyRecordDto>.Ok(_service.AddRecord(id, request ?? new EmergencyRecordRequest { EventId = id }, GetCurrentUsername()));
        }

        [HttpPost] [Route("events/{id:int}/close")]
        public ApiResponse<EmergencyEventDetailDto> CloseEvent(int id, EmergencyCloseRequest request)
        {
            if (request != null) request.EventId = id;
            return ApiResponse<EmergencyEventDetailDto>.Ok(_service.CloseEvent(id, request ?? new EmergencyCloseRequest { EventId = id }, GetCurrentUsername()));
        }

        // ===================== 复盘 =====================

        [HttpPost] [Route("events/{id:int}/review")]
        public ApiResponse<EmergencyReviewDto> ReviewEvent(int id, EmergencyReviewRequest request)
        {
            if (request != null) request.EventId = id;
            return ApiResponse<EmergencyReviewDto>.Ok(_service.ReviewEvent(id, request ?? new EmergencyReviewRequest { EventId = id }, GetCurrentUsername()));
        }

        /// <summary>复盘列表（分页，含关联事件编号，PG-EMG-04）。</summary>
        [HttpGet] [Route("reviews")]
        public ApiResponse<PageResult<EmergencyReviewDto>> QueryReviews([FromUri] PageRequest request) =>
            ApiResponse<PageResult<EmergencyReviewDto>>.Ok(_service.QueryReviews(request ?? new PageRequest()));

        /// <summary>按事件取复盘详情（含改进措施清单）。</summary>
        [HttpGet] [Route("reviews/{eventId:int}")]
        public ApiResponse<EmergencyReviewDto> GetReview(int eventId) =>
            ApiResponse<EmergencyReviewDto>.Ok(_service.GetReview(eventId));

        /// <summary>从 WebApi 注入的 OWIN 环境读取鉴权中间件写入的用户名（同 AuthController）。</summary>
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
