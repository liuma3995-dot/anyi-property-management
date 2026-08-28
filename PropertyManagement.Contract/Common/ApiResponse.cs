using System;

namespace PropertyManagement.Contract.Common
{
    /// <summary>
    /// 统一 API 响应信封：所有接口返回 { Code, Message, Data }。
    /// 约定：Code == ErrorCode.Success(0) 表示成功；其余为错误码（见 ErrorCode）。
    /// </summary>
    [Serializable]
    public class ApiResponse<T>
    {
        public int Code { get; set; }

        public string Message { get; set; }

        public T Data { get; set; }

        public static ApiResponse<T> Ok(T data)
        {
            return new ApiResponse<T> { Code = ErrorCode.Success, Message = "ok", Data = data };
        }

        public static ApiResponse<T> Fail(int code, string message)
        {
            return new ApiResponse<T> { Code = code, Message = message };
        }
    }
}
