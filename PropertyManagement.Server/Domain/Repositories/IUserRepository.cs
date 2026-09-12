using System.Data;
using PropertyManagement.Server.Domain.Entities;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>密码状态（PG-COM-04：90 天强制改密 / 首次登录强制改密）。</summary>
    public class UserPasswordState
    {
        public System.DateTime? PasswordChangedAt { get; set; }

        public bool MustChangePassword { get; set; }
    }

    /// <summary>登录账号仓储（t_user）。事务边界由应用服务层控制。</summary>
    public interface IUserRepository
    {
        AuthUser FindByUsername(IDbConnection connection, string username);

        /// <summary>登录成功：重置失败计数、清除锁定、记录最后登录时间。</summary>
        void UpdateLoginSuccess(IDbConnection connection, IDbTransaction transaction, AuthUser user);

        /// <summary>登录失败：更新失败计数与锁定截止时间（P-02）。</summary>
        void UpdateLoginFailure(IDbConnection connection, IDbTransaction transaction, AuthUser user);

        /// <summary>PG-COM-04：更新密码并写 password_changed_at、清除 must_change_password。</summary>
        void UpdatePassword(IDbConnection connection, IDbTransaction transaction, int userId, string newPasswordHash, System.DateTime passwordChangedAt);

        UserPasswordState GetPasswordState(IDbConnection connection, int userId);

        /// <summary>PG-COM-04：取最近 N 条密码历史哈希（t_password_history，新→旧）。</summary>
        System.Collections.Generic.IList<string> GetRecentPasswordHashes(IDbConnection connection, int userId, int count);

        void InsertPasswordHistory(IDbConnection connection, IDbTransaction transaction, int userId, string passwordHash);

        /// <summary>只保留最近 keep 条历史（PG-COM-04：保留 5 条，供「最近 3 次不重复」校验）。</summary>
        void TrimPasswordHistory(IDbConnection connection, IDbTransaction transaction, int userId, int keep);
    }
}
