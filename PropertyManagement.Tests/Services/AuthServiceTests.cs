using System;
using PropertyManagement.Server.Infrastructure.Security;
using PropertyManagement.Contract.Auth;
using PropertyManagement.Contract.Common;
using PropertyManagement.Server.Services;
using PropertyManagement.Tests.Infrastructure;
using Xunit;

namespace PropertyManagement.Tests.Services
{
    /// <summary>
    /// 认证服务单元测试（BR-COM-02/03、BR-ORG-01 敏感操作审计、R16 已下线 90 天强制改密）。
    /// </summary>
    public class AuthServiceTests : DbTestBase
    {
        private const string AdminPassword = "Admin@123";

        private readonly AuthService _service = new AuthService();

        // ===================== BR-COM-02 连续登录失败 5 次锁定账号 =====================

        [Fact]
        public void Login_连续失败5次_第5次抛LoginLocked()
        {
            for (int i = 1; i <= 4; i++)
            {
                var failed = Assert.Throws<ApiException>(() => _service.Login(new LoginRequest
                {
                    UserName = "admin", Password = "Wrong@123"
                }, "127.0.0.1"));
                Assert.Equal(ErrorCode.Unauthorized, failed.Code);
            }

            var locked = Assert.Throws<ApiException>(() => _service.Login(new LoginRequest
            {
                UserName = "admin", Password = "Wrong@123"
            }, "127.0.0.1"));

            Assert.Equal(ErrorCode.LoginLocked, locked.Code);
            Assert.Equal(40101, locked.Code);
            Assert.True(ScalarText("SELECT locked_until FROM t_user WHERE username = 'admin'") != null);
        }

        [Fact]
        public void Login_锁定后使用正确密码_仍拒绝登录()
        {
            for (int i = 1; i <= 5; i++)
            {
                Assert.Throws<ApiException>(() => _service.Login(new LoginRequest { UserName = "admin", Password = "Wrong@123" }));
            }

            var ex = Assert.Throws<ApiException>(() => _service.Login(new LoginRequest
            {
                UserName = "admin", Password = AdminPassword
            }));

            Assert.Equal(ErrorCode.LoginLocked, ex.Code);
        }

        [Fact]
        public void Login_密码错误_写失败审计留痕()
        {
            Assert.Throws<ApiException>(() => _service.Login(new LoginRequest { UserName = "admin", Password = "Wrong@123" }, "127.0.0.1"));

            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_audit_log WHERE action = 'LOGIN_FAIL' AND result = '失败'"));
            Assert.Equal(1, ScalarInt("SELECT login_fail_count FROM t_user WHERE username = 'admin'"));
        }

        [Fact]
        public void Login_用户名不存在_抛Unauthorized()
        {
            var ex = Assert.Throws<ApiException>(() => _service.Login(new LoginRequest
            {
                UserName = "not_exist_user", Password = "Whatever@123"
            }));

            Assert.Equal(ErrorCode.Unauthorized, ex.Code);
        }

        // ===================== BR-COM-03 停用账号禁止登录 =====================

        [Fact]
        public void Login_账号已停用_抛Unauthorized()
        {
            Execute("UPDATE t_user SET status = 2 WHERE username = 'admin'");

            var ex = Assert.Throws<ApiException>(() => _service.Login(new LoginRequest
            {
                UserName = "admin", Password = AdminPassword
            }));

            Assert.Equal(ErrorCode.Unauthorized, ex.Code);
            Assert.Contains("账号已停用", ex.Message);
        }

        [Fact]
        public void Login_正常账号_成功签发令牌()
        {
            LoginResult result = _service.Login(new LoginRequest { UserName = "admin", Password = AdminPassword }, "127.0.0.1");

            Assert.False(string.IsNullOrWhiteSpace(result.Token));
            Assert.True(result.ExpiresAt > DateTime.Now);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_audit_log WHERE action = 'LOGIN' AND result = '成功'"));
        }

        // ===================== BR-ORG-01 敏感操作审计（修改密码）+ R16 =====================

        [Fact]
        public void ChangePassword_修改成功_写审计且新密码可登录()
        {
            _service.ChangePassword(new ChangePasswordRequest
            {
                // R19 简化规则：6-20 位 + 含字母与数字即可（无需大写/特殊字符）
                OldPassword = AdminPassword, NewPassword = "abc123"
            }, "admin", "127.0.0.1");

            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_audit_log WHERE action = 'CHANGE_PASSWORD' AND result = '成功'"));
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_password_history WHERE user_id = (SELECT id FROM t_user WHERE username='admin')"));
            LoginResult login = _service.Login(new LoginRequest { UserName = "admin", Password = "abc123" });
            Assert.False(login.MustChangePassword); // 改密后已完成强制改密
        }

        [Fact]
        public void ChangePassword_旧密码错误_抛ValidationFailed()
        {
            var ex = Assert.Throws<ApiException>(() => _service.ChangePassword(new ChangePasswordRequest
            {
                OldPassword = "Wrong@123", NewPassword = "NewPass@2026"
            }, "admin"));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("旧密码不正确", ex.Message);
        }

        [Fact]
        public void ChangePassword_新密码与旧密码相同_抛ValidationFailed()
        {
            var ex = Assert.Throws<ApiException>(() => _service.ChangePassword(new ChangePasswordRequest
            {
                OldPassword = AdminPassword, NewPassword = AdminPassword
            }, "admin"));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("不能与旧密码相同", ex.Message);
        }

        [Fact]
        public void ChangePassword_新密码不符合简化规则_抛ValidationFailed()
        {
            // 长度不足
            var tooShort = Assert.Throws<ApiException>(() => _service.ChangePassword(new ChangePasswordRequest
            {
                OldPassword = AdminPassword, NewPassword = "ab12"
            }, "admin"));
            Assert.Equal(ErrorCode.ValidationFailed, tooShort.Code);
            Assert.Contains("6-20 位", tooShort.Message);

            // 纯字母（缺数字）
            var lettersOnly = Assert.Throws<ApiException>(() => _service.ChangePassword(new ChangePasswordRequest
            {
                OldPassword = AdminPassword, NewPassword = "abcdef"
            }, "admin"));
            Assert.Equal(ErrorCode.ValidationFailed, lettersOnly.Code);
            Assert.Contains("数字", lettersOnly.Message);

            // 纯数字（缺字母）
            var digitsOnly = Assert.Throws<ApiException>(() => _service.ChangePassword(new ChangePasswordRequest
            {
                OldPassword = AdminPassword, NewPassword = "123456"
            }, "admin"));
            Assert.Equal(ErrorCode.ValidationFailed, digitsOnly.Code);
            Assert.Contains("字母", digitsOnly.Message);

            // 超长（>20）
            var tooLong = Assert.Throws<ApiException>(() => _service.ChangePassword(new ChangePasswordRequest
            {
                OldPassword = AdminPassword, NewPassword = "abc12345678901234567890"
            }, "admin"));
            Assert.Equal(ErrorCode.ValidationFailed, tooLong.Code);
            Assert.Contains("6-20 位", tooLong.Message);
        }

        [Fact]
        public void ChangePassword_与历史密码重复_规则已下线_可通过()
        {
            // v1.1.0 第 8 轮（负责人裁定）：下线「新密码不得与最近 3 次使用过的密码重复」，
            // 历史哈希仍按 PG-COM-04 留痕，但不再作为拦截条件。
            _service.ChangePassword(new ChangePasswordRequest { OldPassword = AdminPassword, NewPassword = "abc123" }, "admin");

            _service.ChangePassword(new ChangePasswordRequest
            {
                OldPassword = "abc123", NewPassword = AdminPassword
            }, "admin");

            LoginResult login = _service.Login(new LoginRequest { UserName = "admin", Password = AdminPassword });
            Assert.False(login.MustChangePassword);
            Assert.True(ScalarInt("SELECT COUNT(1) FROM t_password_history") >= 2); // 历史仍留痕
        }

        [Fact]
        public void ChangePassword_简单口令小写字母加数字_规则通过()
        {
            // R19：不再要求大写字母与特殊字符，小写字母+数字的 6 位口令即可
            _service.ChangePassword(new ChangePasswordRequest
            {
                OldPassword = AdminPassword, NewPassword = "admin1"
            }, "admin");

            LoginResult login = _service.Login(new LoginRequest { UserName = "admin", Password = "admin1" });
            Assert.False(string.IsNullOrWhiteSpace(login.Token));
        }

        [Fact]
        public void Login_密码超90天未修改_不再强制改密()
        {
            Execute("UPDATE t_user SET password_changed_at = datetime('now','localtime','-100 days'), must_change_password = 0 " +
                    "WHERE username = 'admin'");

            LoginResult result = _service.Login(new LoginRequest { UserName = "admin", Password = AdminPassword });

            Assert.False(result.MustChangePassword); // R16：90 天强制更换规则已下线
        }

        [Fact]
        public void Login_管理员标记强制改密_仍然强制()
        {
            Execute("UPDATE t_user SET password_changed_at = datetime('now','localtime'), must_change_password = 1 " +
                    "WHERE username = 'admin'");

            LoginResult result = _service.Login(new LoginRequest { UserName = "admin", Password = AdminPassword });

            Assert.True(result.MustChangePassword);
        }

        [Fact]
        public void Login_首次登录未改密_标记强制改密()
        {
            LoginResult result = _service.Login(new LoginRequest { UserName = "admin", Password = AdminPassword });

            Assert.True(result.MustChangePassword); // 首登（password_changed_at 为空）仍需改密
        }

        // ===================== v1.1.0 第 4 轮：改密失效判定（秒级口径） =====================

        [Fact]
        public void IsRevokedByTime_改密后同一秒内签发的token_不判为失效()
        {
            // token 的 iat 只有秒精度（31.000），password_changed_at 带亚秒（31.123）：
            // 修复前直接比较会误判为「密码已修改」，导致刚改密重登就 401
            DateTime changedAt = new DateTime(2026, 9, 16, 19, 52, 31, 123);
            DateTime issuedAt = new DateTime(2026, 9, 16, 19, 52, 31, 0);

            Assert.False(TokenService.IsRevokedByTime(issuedAt, changedAt));
        }

        [Fact]
        public void IsRevokedByTime_改密前签发的token_判为失效()
        {
            DateTime changedAt = new DateTime(2026, 9, 16, 19, 52, 31, 123);
            DateTime issuedAt = new DateTime(2026, 9, 16, 19, 52, 30, 900);

            Assert.True(TokenService.IsRevokedByTime(issuedAt, changedAt));
        }

        [Fact]
        public void IsRevokedByTime_改密后较晚签发的token_不判为失效()
        {
            DateTime changedAt = new DateTime(2026, 9, 16, 19, 52, 31, 123);
            DateTime issuedAt = new DateTime(2026, 9, 16, 19, 52, 35, 0);

            Assert.False(TokenService.IsRevokedByTime(issuedAt, changedAt));
        }
    }
}
