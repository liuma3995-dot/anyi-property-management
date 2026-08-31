using System;
using System.Configuration;
using System.Data;
using PropertyManagement.Contract.Auth;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Domain.Entities;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;
using PropertyManagement.Server.Infrastructure.Security;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 认证用例服务（UC-COM-001/002）：
    /// 登录（BCrypt 校验 + P-02 失败锁定 + BR-COM-02/03 + token 签发）、
    /// 修改密码（DM-07 最小 8 位，审计留痕 BR-COM-01）。
    /// 事务边界在本服务层控制（M2-D7）。
    /// </summary>
    public class AuthService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IUserRepository _users;
        private readonly IParamRepository _params;
        private readonly IAuditLogRepository _auditLogs;

        public AuthService()
            : this(
                new SqliteConnectionFactory(),
                new SqlUserRepository(),
                new SqlParamRepository(),
                new SqlAuditLogRepository())
        {
        }

        public AuthService(
            IDbConnectionFactory connectionFactory,
            IUserRepository users,
            IParamRepository parameters,
            IAuditLogRepository auditLogs)
        {
            _connectionFactory = connectionFactory;
            _users = users;
            _params = parameters;
            _auditLogs = auditLogs;
        }

        public LoginResult Login(LoginRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.UserName) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                throw ApiException.BadRequest("用户名和密码不能为空");
            }

            int failLimit = ReadParamInt("auth.fail.limit", 5);
            int lockMinutes = ReadParamInt("auth.lock.minutes", 30);
            DateTime now = DateTime.Now;
            string username = request.UserName.Trim();

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                AuthUser user = _users.FindByUsername(connection, username);

                if (user == null)
                {
                    throw ApiException.Unauthorized("用户名或密码错误");
                }

                // BR-COM-03：停用账号禁止登录
                if (user.Status == UserStatus.Disabled)
                {
                    throw ApiException.Unauthorized("账号已停用，禁止登录");
                }

                // BR-COM-02 / P-02：锁定中禁止登录
                if (user.Status == UserStatus.Locked ||
                    (user.LockedUntil.HasValue && user.LockedUntil.Value > now))
                {
                    throw ApiException.LoginLocked("登录失败次数超限，账号已锁定，请稍后再试");
                }

                if (!PasswordHasher.Verify(request.Password, user.PasswordHash))
                {
                    int fails = user.LoginFailCount + 1;
                    DateTime? lockedUntil = null;

                    if (fails >= failLimit)
                    {
                        lockedUntil = now.AddMinutes(lockMinutes);
                        fails = 0;
                        _auditLogs.Insert(
                            connection,
                            transaction,
                            new AuditLogEntry
                            {
                                UserId = user.Id,
                                Action = "LOGIN_LOCKED",
                                TargetType = "user",
                                TargetId = user.Id.ToString(),
                                Detail = "连续登录失败达到阈值，账号锁定 " + lockMinutes + " 分钟"
                            });
                    }

                    user.LoginFailCount = fails;
                    user.LockedUntil = lockedUntil;
                    _users.UpdateLoginFailure(connection, transaction, user);
                    transaction.Commit();

                    if (lockedUntil.HasValue)
                    {
                        throw ApiException.LoginLocked(
                            "登录失败次数超限，账号已锁定 " + lockMinutes + " 分钟");
                    }

                    throw ApiException.Unauthorized("用户名或密码错误");
                }

                // 登录成功：重置失败计数、清除锁定、记录最后登录时间、签发 token
                user.LoginFailCount = 0;
                user.LockedUntil = null;
                user.LastLoginAt = now;
                _users.UpdateLoginSuccess(connection, transaction, user);
                transaction.Commit();

                int expireMinutes = ReadAppInt("TokenExpireMinutes", 480);
                DateTime expiresAt = now.AddMinutes(expireMinutes);

                return new LoginResult
                {
                    Token = TokenService.Issue(user.Username, expiresAt),
                    DisplayName = "系统管理员",
                    ExpiresAt = expiresAt
                };
            }
        }

        public void ChangePassword(ChangePasswordRequest request, string currentUsername)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.OldPassword) ||
                string.IsNullOrWhiteSpace(request.NewPassword))
            {
                throw ApiException.BadRequest("旧密码和新密码不能为空");
            }

            // DM-07：密码最小 8 位
            if (request.NewPassword.Length < 8)
            {
                throw ApiException.ValidationFailed("新密码长度不能少于 8 位");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                AuthUser user = _users.FindByUsername(connection, currentUsername);

                if (user == null)
                {
                    throw ApiException.Unauthorized("登录状态无效，请重新登录");
                }

                if (!PasswordHasher.Verify(request.OldPassword, user.PasswordHash))
                {
                    throw ApiException.ValidationFailed("旧密码不正确");
                }

                if (string.Equals(request.OldPassword, request.NewPassword, StringComparison.Ordinal))
                {
                    throw ApiException.ValidationFailed("新密码不能与旧密码相同");
                }

                _users.UpdatePassword(
                    connection,
                    transaction,
                    user.Id,
                    PasswordHasher.Hash(request.NewPassword));

                _auditLogs.Insert(
                    connection,
                    transaction,
                    new AuditLogEntry
                    {
                        UserId = user.Id,
                        Action = "CHANGE_PASSWORD",
                        TargetType = "user",
                        TargetId = user.Id.ToString(),
                        Detail = "修改登录密码"
                    });

                transaction.Commit();
            }
        }

        private int ReadParamInt(string key, int defaultValue)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _params.GetInt(connection, key, defaultValue);
            }
        }

        private static int ReadAppInt(string key, int defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            int result;
            return int.TryParse(value, out result) ? result : defaultValue;
        }
    }
}
