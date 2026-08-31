using System.Net;
using System.Net.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using PropertyManagement.Contract.Common;

namespace PropertyManagement.Server.Api.Filters
{
    /// <summary>模型校验失败统一返回 HTTP 400 + code=40000 信封（契约 §四）。</summary>
    public class ApiValidationFilterAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            if (actionContext.ModelState.IsValid)
            {
                return;
            }

            string message = "请求参数不正确";
            foreach (var state in actionContext.ModelState)
            {
                foreach (var error in state.Value.Errors)
                {
                    if (!string.IsNullOrEmpty(error.ErrorMessage))
                    {
                        message = error.ErrorMessage;
                        break;
                    }
                }

                if (message != "请求参数不正确")
                {
                    break;
                }
            }

            var envelope = ApiResponse<object>.Fail(ErrorCode.BadRequest, message);
            actionContext.Response = actionContext.Request.CreateResponse(
                HttpStatusCode.BadRequest,
                envelope,
                "application/json");
        }
    }
}
