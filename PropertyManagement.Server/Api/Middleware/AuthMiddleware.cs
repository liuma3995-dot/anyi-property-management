using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Owin;
using PropertyManagement.Server.Infrastructure.Data;
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
                    // 改密后旧 token 失效（M6 PG-COM-04）：token 签发时间早于密码修改时间则拒绝
                    if (IsTokenRevoked(username, token))
                    {
                        context.Response.StatusCode = 401;
                        context.Response.ContentType = "application/json; charset=utf-8";
                        var revoked = ApiResponse<object>.Fail(ErrorCode.Unauthorized, "密码已修改，请重新登录");
                        await context.Response.WriteAsync(JsonConvert.SerializeObject(revoked, CamelCaseSettings));
                        return;
                    }
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

        // 改密失效缓存（username -> password_changed_at tick），5 秒窗口减少库查询
        private static readonly Dictionary<string, Tuple<DateTime, long>> RevokedCache =
            new Dictionary<string, Tuple<DateTime, long>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object RevokedCacheLock = new object();
        private const int RevokedCacheSeconds = 5;

        private static bool IsTokenRevoked(string username, string token)
        {
            DateTime? issuedAt = TokenService.TryGetIssuedAt(token);
            if (!issuedAt.HasValue)
            {
                return false; // 老 token 无 iat，不阻断
            }

            DateTime? changedAt = GetPasswordChangedAt(username);
            if (!changedAt.HasValue)
            {
                return false;
            }

            return issuedAt.Value < changedAt.Value;
        }

        private static DateTime? GetPasswordChangedAt(string username)
        {
            DateTime now = DateTime.Now;
            lock (RevokedCacheLock)
            {
                Tuple<DateTime, long> cached;
                if (RevokedCache.TryGetValue(username, out cached) &&
                    (now - cached.Item1).TotalSeconds < RevokedCacheSeconds)
                {
                    long ticks = cached.Item2;
                    return ticks == 0 ? (DateTime?)null : new DateTime(ticks);
                }
            }

            DateTime? changed = null;
            try
            {
                using (IDbConnection c = new SqliteConnectionFactory().OpenConnection())
                {
                    string raw = c.ExecuteScalar<string>(
                        "SELECT password_changed_at FROM t_user WHERE username = @username",
                        new { username });
                    DateTime parsed;
                    if (!string.IsNullOrEmpty(raw) && DateTime.TryParse(raw, out parsed))
                    {
                        changed = parsed;
                    }
                }
            }
            catch
            {
                changed = null; // 查询失败不阻断请求
            }

            lock (RevokedCacheLock)
            {
                RevokedCache[username] = Tuple.Create(now, changed.HasValue ? changed.Value.Ticks : 0L);
                if (RevokedCache.Count > 256)
                {
                    RevokedCache.Clear(); // 极简防膨胀
                }
            }
            return changed;
        }

        private static bool IsPublicPath(string path)
        {
            return path.EndsWith("/auth/login", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith("/health", StringComparison.OrdinalIgnoreCase);
        }
    }
}
