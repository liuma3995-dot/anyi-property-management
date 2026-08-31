using System;
using PropertyManagement.Contract.Common;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 携带契约错误码的业务异常。统一由 ApiExceptionFilterAttribute 映射
    /// 为 ApiResponse 信封与 HTTP 状态码（契约 §四）。
    /// </summary>
    public class ApiException : Exception
    {
        public int Code { get; }

        public int HttpStatus { get; }

        public ApiException(int code, string message, int httpStatus = 400)
            : base(message)
        {
            Code = code;
            HttpStatus = httpStatus;
        }

        public static ApiException BadRequest(string message)
        {
            return new ApiException(ErrorCode.BadRequest, message, 400);
        }

        public static ApiException Unauthorized(string message)
        {
            return new ApiException(ErrorCode.Unauthorized, message, 401);
        }

        /// <summary>登录失败次数超限，账号锁定（40101，P-02）。</summary>
        public static ApiException LoginLocked(string message)
        {
            return new ApiException(ErrorCode.LoginLocked, message, 401);
        }

        public static ApiException Forbidden(string message)
        {
            return new ApiException(ErrorCode.Forbidden, message, 403);
        }

        public static ApiException NotFound(string message)
        {
            return new ApiException(ErrorCode.NotFound, message, 404);
        }

        public static ApiException Conflict(string message)
        {
            return new ApiException(ErrorCode.Conflict, message, 409);
        }

        public static ApiException ValidationFailed(string message)
        {
            return new ApiException(ErrorCode.ValidationFailed, message, 422);
        }
    }
}
