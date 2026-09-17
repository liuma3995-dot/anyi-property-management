using System;
using System.Net;
using System.Net.Http;
using System.Web.Http.Filters;
using NLog;
using PropertyManagement.Contract.Common;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api.Filters
{
    /// <summary>
    /// 统一异常映射过滤器（M2 D2-5，契约 §四）：
    /// ApiException → 自身错误码；SQLite/IO/权限 → 50300；其余 → 50000。
    /// 响应统一为 ApiResponse 信封；设置 Response 即视为已处理。
    /// </summary>
    public class ApiExceptionFilterAttribute : ExceptionFilterAttribute
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public override void OnException(HttpActionExecutedContext context)
        {
            Exception exception = context.Exception;

            int code = ErrorCode.InternalError;
            int httpStatus = 500;
            string message = "服务器内部错误";

            var apiException = exception as ApiException;
            if (apiException != null)
            {
                code = apiException.Code;
                httpStatus = apiException.HttpStatus;
                message = apiException.Message;
            }
            else if (exception is System.Data.SQLite.SQLiteException ||
                     exception is System.IO.IOException ||
                     exception is UnauthorizedAccessException)
            {
                // v1.1.0 T9：唯一约束冲突属业务冲突（重复名称/编号），返回可读提示而非「数据服务暂不可用」
                if (exception is System.Data.SQLite.SQLiteException &&
                    (exception.Message ?? string.Empty).IndexOf("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    code = ErrorCode.Conflict;
                    httpStatus = 409;
                    message = "数据已存在：请检查名称/编号是否与既有记录重复（软删除留下的留痕记录不影响新建）";
                }
                else
                {
                    code = ErrorCode.ServiceUnavailable;
                    httpStatus = 503;
                    message = "数据服务暂不可用，请稍后重试";
                }
            }

            if (code == ErrorCode.InternalError || code == ErrorCode.ServiceUnavailable)
            {
                Log.Error(exception, "请求处理异常：{0}", context.Request.RequestUri);
            }
            else
            {
                Log.Warn("业务失败：code={0} message={1} uri={2}", code, message, context.Request.RequestUri);
            }

            var envelope = ApiResponse<object>.Fail(code, message);
            context.Response = context.Request.CreateResponse(
                (HttpStatusCode)httpStatus,
                envelope,
                "application/json");
        }
    }
}
