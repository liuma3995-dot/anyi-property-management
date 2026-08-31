using System.Web.Http;
using Microsoft.Owin;
using PropertyManagement.Contract.Auth;
using PropertyManagement.Contract.Common;
using PropertyManagement.Server.Api.Middleware;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>认证端点（UC-COM-001/002）：login/logout/change-password。</summary>
    [RoutePrefix("api/v1/auth")]
    public class AuthController : ApiController
    {
        private readonly AuthService _authService;

        public AuthController()
        {
            _authService = new AuthService();
        }

        [HttpPost]
        [Route("login")]
        [AllowAnonymous]
        public ApiResponse<LoginResult> Login(LoginRequest request)
        {
            LoginResult result = _authService.Login(request);
            return ApiResponse<LoginResult>.Ok(result);
        }

        [HttpPost]
        [Route("logout")]
        public ApiResponse<object> Logout()
        {
            // 无状态 token：客户端清除会话即可；服务端失效（黑名单）后续切片按需扩展
            return ApiResponse<object>.Ok(null);
        }

        [HttpPost]
        [Route("change-password")]
        public ApiResponse<object> ChangePassword(ChangePasswordRequest request)
        {
            string username = GetCurrentUsername();
            _authService.ChangePassword(request, username);
            return ApiResponse<object>.Ok(null);
        }

        /// <summary>从 WebApi 注入的 OWIN 环境读取鉴权中间件写入的用户名。</summary>
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
