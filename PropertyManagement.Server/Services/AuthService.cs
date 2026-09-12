using System;
using System.Configuration;
using System.Data;
using System.Text.RegularExpressions;
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
    /// 登录（BCrypt 校验 + P-02 失败锁定 + BR-COM-02/03 + token 签发 + 登录逐次留痕）、
    /// 修改密码（PG-COM-04：8-20 位 + 大写/数字/特殊字符 + 最近 3 次不重复；
    /// R16 已下线「90 天强制更换」规则，强制改密仅保留首登/管理员标记；
    /// t_password_history 保留 5 条；审计留痕 BR-COM-01）。
    /// 事务边界在本服务层控制（M2-D7）。
    /// </summary>
    public class AuthService
    {
        private const int PasswordMinLength = 8;
        private const int PasswordMaxLength = 20;
        private const int PasswordHistoryDenyCount = 3; // 不得与最近 3 次重复
        private const int PasswordHistoryKeep = 5;      // 历史保留条数

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

        public LoginResult Login(LoginRequest request, string ip = null)
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
                    // P1：登录失败逐次留痕（尝试用户名+IP，user_id 置空）
                    WriteAuthAudit(connection, transaction, "LOGIN_FAIL", null, username, ip, "失败",
                        "用户名不存在，登录失败");
                    transaction.Commit();
                    throw ApiException.Unauthorized("用户名或密码错误");
                }

                // BR-COM-03：停用账号禁止登录
                if (user.Status == UserStatus.Disabled)
                {
                    WriteAuthAudit(connection, transaction, "LOGIN_FAIL", user.Id, user.Username, ip, "失败",
                        "账号已停用，尝试登录被拒绝");
                    transaction.Commit();
                    throw ApiException.Unauthorized("账号已停用，禁止登录");
                }

                // BR-COM-02 / P-02：锁定中禁止登录
                if (user.Status == UserStatus.Locked ||
                    (user.LockedUntil.HasValue && user.LockedUntil.Value > now))
                {
                    WriteAuthAudit(connection, transaction, "LOGIN_FAIL", user.Id, user.Username, ip, "失败",
                        "账号锁定期间尝试登录");
                    transaction.Commit();
                    throw ApiException.LoginLocked("登录失败次数超限，账号已锁定，请稍后再试");
                }

                if (!PasswordHasher.Verify(request.Password, user.PasswordHash))
                {
                    int fails = user.LoginFailCount + 1;
                    DateTime? lockedUntil = null;

                    // 每次失败均留痕（PG-COM-03）
                    WriteAuthAudit(connection, transaction, "LOGIN_FAIL", user.Id, user.Username, ip, "失败",
                        "密码错误，连续第 " + fails + " 次失败");

                    if (fails >= failLimit)
                    {
                        lockedUntil = now.AddMinutes(lockMinutes);
                        fails = 0;
                        WriteAuthAudit(connection, transaction, "LOGIN_LOCKED", user.Id, user.Username, ip, "失败",
                            "连续登录失败达到阈值（" + failLimit + " 次），账号锁定 " + lockMinutes + " 分钟");
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

                // 登录成功：重置失败计数、清除锁定、记录最后登录时间
                user.LoginFailCount = 0;
                user.LockedUntil = null;
                user.LastLoginAt = now;
                _users.UpdateLoginSuccess(connection, transaction, user);

                // PG-COM-04 强制改密：首登（password_changed_at 为空）或管理员标记（must_change_password）
                // R16：已下线「90 天强制更换」规则，不再按 password_changed_at 距今天数判定
                UserPasswordState state = _users.GetPasswordState(connection, user.Id);
                bool mustChangePassword = state.MustChangePassword || !state.PasswordChangedAt.HasValue;

                WriteAuthAudit(connection, transaction, "LOGIN", user.Id, user.Username, ip, "成功",
                    mustChangePassword ? "登录成功（需修改密码后继续使用）" : "登录成功");

                transaction.Commit();

                int expireMinutes = ReadAppInt("TokenExpireMinutes", 480);
                DateTime expiresAt = now.AddMinutes(expireMinutes);

                return new LoginResult
                {
                    Token = TokenService.Issue(user.Username, expiresAt),
                    DisplayName = user.Username,
                    ExpiresAt = expiresAt,
                    MustChangePassword = mustChangePassword
                };
            }
        }

        public void ChangePassword(ChangePasswordRequest request, string currentUsername, string ip = null)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.OldPassword) ||
                string.IsNullOrWhiteSpace(request.NewPassword))
            {
                throw ApiException.BadRequest("旧密码和新密码不能为空");
            }

            // PG-COM-04：强度规则（服务端先行校验，客户端 6 位限制由前端代理统一为 8-20）
            ValidatePasswordStrength(request.NewPassword);

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
                    WriteAuthAudit(connection, transaction, "CHANGE_PASSWORD", user.Id, user.Username, ip, "失败",
                        "修改密码失败：旧密码校验未通过");
                    transaction.Commit();
                    throw ApiException.ValidationFailed("旧密码不正确");
                }

                if (string.Equals(request.OldPassword, request.NewPassword, StringComparison.Ordinal))
                {
                    throw ApiException.ValidationFailed("新密码不能与旧密码相同");
                }

                // 不得与最近 3 次重复（t_password_history BCrypt 逐条比对）
                System.Collections.Generic.IList<string> recentHashes =
                    _users.GetRecentPasswordHashes(connection, user.Id, PasswordHistoryDenyCount);
                foreach (string historyHash in recentHashes)
                {
                    if (PasswordHasher.Verify(request.NewPassword, historyHash))
                    {
                        WriteAuthAudit(connection, transaction, "CHANGE_PASSWORD", user.Id, user.Username, ip, "失败",
                            "修改密码失败：新密码与最近 " + PasswordHistoryDenyCount + " 次使用过的密码重复");
                        transaction.Commit();
                        throw ApiException.ValidationFailed(
                            "新密码不得与最近 " + PasswordHistoryDenyCount + " 次使用过的密码重复");
                    }
                }

                DateTime now = DateTime.Now;
                string newHash = PasswordHasher.Hash(request.NewPassword);

                // 历史链：先记录被替换的当前密码，再保留最近 5 条，最后更新密码与改密时间（password_changed_at）
                _users.InsertPasswordHistory(connection, transaction, user.Id, user.PasswordHash);
                _users.TrimPasswordHistory(connection, transaction, user.Id, PasswordHistoryKeep);
                _users.UpdatePassword(connection, transaction, user.Id, newHash, now);

                WriteAuthAudit(connection, transaction, "CHANGE_PASSWORD", user.Id, user.Username, ip, "成功",
                    "修改登录密码（强度规则与历史校验通过，旧 token 改密后由中间件失效，见 CHG 报告）");

                transaction.Commit();
            }
        }

        /// <summary>密码强度规则（PG-COM-04/T6-6-5）：8-20 位，必须含大写字母+数字+特殊字符。</summary>
        private static void ValidatePasswordStrength(string password)
        {
            if (password.Length < PasswordMinLength || password.Length > PasswordMaxLength)
            {
                throw ApiException.ValidationFailed(
                    "新密码长度须为 " + PasswordMinLength + "-" + PasswordMaxLength + " 位");
            }
            if (!Regex.IsMatch(password, "[A-Z]"))
            {
                throw ApiException.ValidationFailed("新密码必须包含至少一个大写字母");
            }
            if (!Regex.IsMatch(password, "[0-9]"))
            {
                throw ApiException.ValidationFailed("新密码必须包含至少一个数字");
            }
            if (!Regex.IsMatch(password, "[^A-Za-z0-9]"))
            {
                throw ApiException.ValidationFailed("新密码必须包含至少一个特殊字符");
            }
        }

        private void WriteAuthAudit(
            IDbConnection connection,
            IDbTransaction transaction,
            string action,
            int? userId,
            string userName,
            string ip,
            string result,
            string detail)
        {
            _auditLogs.InsertDetailed(
                connection,
                transaction,
                new AuditLogEntry
                {
                    UserId = userId,
                    Action = action,
                    TargetType = "user",
                    TargetId = userId.HasValue ? userId.Value.ToString() : null,
                    Detail = detail
                },
                userName,
                "系统管理员",
                "系统认证",
                result,
                ip);
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
