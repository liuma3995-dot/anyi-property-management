using System;
using System.Threading.Tasks;
using PropertyManagement.Contract.Auth;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Health;

namespace PropertyManagement.Client.Services
{
    /// <summary>API 调用异常：Code 为契约错误码（ErrorCode）。</summary>
    public class ApiClientException : Exception
    {
        public int Code { get; }

        public ApiClientException(int code, string message) : base(message)
        {
            Code = code;
        }
    }

    /// <summary>前端 API 客户端契约（M3：登录/会话/仪表盘；业务端点随切片扩展）。</summary>
    public interface IApiClient
    {
        bool IsMock { get; }

        Task<HealthResponse> GetHealthAsync();

        Task<LoginResult> LoginAsync(LoginRequest request);

        Task LogoutAsync();

        Task ChangePasswordAsync(ChangePasswordRequest request);

        Task<DashboardDto> GetDashboardAsync();
    }
}
