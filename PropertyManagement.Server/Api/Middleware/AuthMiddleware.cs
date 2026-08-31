using System;
using System.Threading.Tasks;
using Microsoft.Owin;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PropertyManagement.Contract.Common;
using PropertyManagement.Server.Infrastructure.Security;

namespace PropertyManagement.Server.Api.Middleware
{
    /// <summary>
    /// Bearer token 鉴权中间件（M2 D2-4，DM-07 §一·五）：
    /// 除 /auth/login、/health 外逐请求校验；无效/过期返回 HTTP 401 + code=40100 信封。
    /// 通过后将用户名写入 OWIN 环境，供控制器读取。
    /// </summary>
    public class AuthMiddleware : OwinMiddleware
    {
        public const string UsernameEnvKey = "pm.username";

        public AuthMiddleware(OwinMiddleware next)
            : base(next)
        {
        }

        public override async Task Invoke(IOwinContext context)
        {
            string path = context.Request.Path.Value ?? string.Empty;

            if (IsPublicPath(path))
            {
                await Next.Invoke(context);
                return;
            }

            string header = context.Request.Headers["Authorization"];
            if (!string.IsNullOrWhiteSpace(header) &&
                header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                string token = header.Substring("Bearer ".Length).Trim();
                string username;
                if (TokenService.TryValidate(token, out username))
                {
                    context.Set(UsernameEnvKey, username);
                    await Next.Invoke(context);
                    return;
                }
            }

            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json; charset=utf-8";

            var envelope = ApiResponse<object>.Fail(ErrorCode.Unauthorized, "token 无效或过期");
            await context.Response.WriteAsync(JsonConvert.SerializeObject(envelope, CamelCaseSettings));
        }

        private static readonly JsonSerializerSettings CamelCaseSettings =
            new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };

        private static bool IsPublicPath(string path)
        {
            return path.EndsWith("/auth/login", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith("/health", StringComparison.OrdinalIgnoreCase);
        }
    }
}
