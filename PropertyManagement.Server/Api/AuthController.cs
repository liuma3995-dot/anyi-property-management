using System.Web.Http;
using PropertyManagement.Contract.Auth;
using PropertyManagement.Contract.Common;
using PropertyManagement.Server.Api.Middleware;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>认证端点（UC-COM-001/002）：login/logout/change-password。
    /// PG-COM-04：登录返回 MustChangePassword；改密走强度/历史/90 天策略；
    /// 登录失败逐次留痕（action=LOGIN_FAIL/LOGIN_LOCKED/LOGIN，带 IP 与结果）。</summary>
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
            LoginResult result = _authService.Login(request, GetClientIp());
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
            _authService.ChangePassword(request, username, GetClientIp());
            return ApiResponse<object>.Ok(null);
        }

        /// <summary>R17：读取当前账号个人信息（顶栏管理员下拉 → 个人信息设置）。</summary>
        [HttpGet]
        [Route("profile")]
        public ApiResponse<UserProfileDto> GetProfile()
        {
            return ApiResponse<UserProfileDto>.Ok(_authService.GetProfile(GetCurrentUsername()));
        }

        /// <summary>R17：保存个人信息（三项均可填可不填；写审计 USER_PROFILE_UPDATE）。</summary>
        [HttpPut]
        [Route("profile")]
        public ApiResponse<UserProfileDto> UpdateProfile(UserProfileRequest request)
        {
            return ApiResponse<UserProfileDto>.Ok(
                _authService.UpdateProfile(request, GetCurrentUsername(), GetClientIp()));
        }

        /// <summary>从 WebApi 注入的 OWIN 环境读取鉴权中间件写入的用户名。</summary>
        private string GetCurrentUsername()
        {
            object owinContextValue;
            if (Request.Properties.TryGetValue("MS_OwinContext", out owinContextValue))
            {
                var owinContext = owinContextValue as Microsoft.Owin.IOwinContext;
                if (owinContext != null)
                {
                    return owinContext.Get<string>(AuthMiddleware.UsernameEnvKey) ?? string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>客户端 IP（审计留痕：LOGIN/LOGIN_FAIL/CHANGE_PASSWORD 等）。</summary>
        private string GetClientIp()
        {
            object owinContextValue;
            if (Request.Properties.TryGetValue("MS_OwinContext", out owinContextValue))
            {
                var owinContext = owinContextValue as Microsoft.Owin.IOwinContext;
                if (owinContext != null)
                {
                    return owinContext.Request.RemoteIpAddress;
                }
            }

            return null;
        }
    }
}
