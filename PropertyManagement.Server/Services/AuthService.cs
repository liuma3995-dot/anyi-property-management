using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
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
    /// 修改密码（PG-COM-04 简化规则：6-20 位 + 必须含字母与数字 + 最近 3 次不重复；
    /// R16 已下线「90 天强制更换」规则，强制改密仅保留首登/管理员标记；
    /// t_password_history 保留 5 条；审计留痕 BR-COM-01）。
    /// 事务边界在本服务层控制（M2-D7）。
    /// </summary>
    public class AuthService
    {
        private const int PasswordMinLength = 6;
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

        // ===================== 个人信息（R17：顶栏管理员下拉 → 个人信息设置） =====================

        /// <summary>内置可选头像（8 个；不含文件上传，仅存 key）。</summary>
        private static readonly string[][] AvatarCatalog =
        {
            new[] { "avatar-01", "管理员" }, new[] { "avatar-02", "客服" },
            new[] { "avatar-03", "工程" }, new[] { "avatar-04", "安保" },
            new[] { "avatar-05", "财务" }, new[] { "avatar-06", "保洁" },
            new[] { "avatar-07", "秩序" }, new[] { "avatar-08", "访客" }
        };

        /// <summary>读取当前登录账号个人信息（三项均可空；返回内置头像候选供前端渲染）。</summary>
        public UserProfileDto GetProfile(string currentUsername)
        {
            if (string.IsNullOrWhiteSpace(currentUsername))
            {
                throw ApiException.Unauthorized("登录状态无效，请重新登录");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                UserProfileDto profile = _users.GetProfile(connection, currentUsername.Trim())
                    ?? throw ApiException.NotFound("账号不存在");
                profile.Role = "系统管理员";
                profile.AvatarOptions = AvatarCatalog
                    .Select(x => new AvatarOptionDto { Key = x[0], Label = x[1] })
                    .ToList();
                return profile;
            }
        }

        /// <summary>
        /// 保存个人信息（R17）：名字/手机号/简介/内置头像**均可填可不填**，填了才校验
        /// （名字 ≤20 字、简介 ≤200 字、手机号按 BR-TEL-04 口径、头像必须在内置范围内）；
        /// 保存动作写审计 USER_PROFILE_UPDATE（BR-ORG-01 敏感操作留痕）。
        /// </summary>
        public UserProfileDto UpdateProfile(UserProfileRequest request, string currentUsername, string ip = null)
        {
            if (request == null)
            {
                throw ApiException.ValidationFailed("请求体不能为空");
            }
            if (string.IsNullOrWhiteSpace(currentUsername))
            {
                throw ApiException.Unauthorized("登录状态无效，请重新登录");
            }

            string displayName = (request.DisplayName ?? string.Empty).Trim();
            string phone = (request.Phone ?? string.Empty).Trim();
            string bio = (request.Bio ?? string.Empty).Trim();
            string avatarKey = (request.AvatarKey ?? string.Empty).Trim();

            if (displayName.Length > 20) throw ApiException.ValidationFailed("名字不能超过 20 个字");
            if (bio.Length > 200) throw ApiException.ValidationFailed("个人简介不能超过 200 字");
            if (phone.Length > 0 && !IsValidContactPhone(phone))
                throw ApiException.ValidationFailed("手机号码格式不正确（支持 11 位手机号或带区号座机）");
            if (avatarKey.Length > 0 && !AvatarCatalog.Any(x => x[0] == avatarKey))
                throw ApiException.ValidationFailed("头像不在内置可选范围内，请重新选择");

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                AuthUser user = _users.FindByUsername(connection, currentUsername.Trim())
                    ?? throw ApiException.Unauthorized("登录状态无效，请重新登录");

                _users.UpdateProfile(connection, transaction, user.Id, displayName, phone, bio, avatarKey);
                WriteAuthAudit(connection, transaction, "USER_PROFILE_UPDATE", user.Id, user.Username, ip, "成功",
                    "更新个人信息：名字「" + (displayName.Length == 0 ? "未填" : displayName) + "」、" +
                    "手机号「" + (phone.Length == 0 ? "未填" : phone) + "」、" +
                    "简介" + (bio.Length == 0 ? "未填" : "已填写 " + bio.Length + " 字") + "、" +
                    "头像「" + (avatarKey.Length == 0 ? "默认" : avatarKey) + "」");
                transaction.Commit();
            }

            return GetProfile(currentUsername);
        }

        /// <summary>手机号/座机格式校验（与 BR-TEL-04 同口径：11 位手机、可含区号座机）。</summary>
        private static bool IsValidContactPhone(string phone)
        {
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
            {
                return false;
            }
            return Regex.IsMatch(digits, "^1\\d{10}$") || Regex.IsMatch(digits, "^(0\\d{2,3})?\\d{7,8}$");
        }

        /// <summary>
        /// 密码规则（PG-COM-04 简化版，2026-09-13 负责人确认）：6-20 位，必须同时含字母与数字；
        /// 大小写不限，**不再要求**大写字母，**不再要求**特殊字符。
        /// </summary>
        private static void ValidatePasswordStrength(string password)
        {
            if (password.Length < PasswordMinLength || password.Length > PasswordMaxLength)
            {
                throw ApiException.ValidationFailed(
                    "新密码长度须为 " + PasswordMinLength + "-" + PasswordMaxLength + " 位");
            }
            if (!Regex.IsMatch(password, "[A-Za-z]"))
            {
                throw ApiException.ValidationFailed("新密码必须包含至少一个字母");
            }
            if (!Regex.IsMatch(password, "[0-9]"))
            {
                throw ApiException.ValidationFailed("新密码必须包含至少一个数字");
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
