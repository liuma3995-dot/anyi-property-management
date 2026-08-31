using System.Data;
using PropertyManagement.Server.Domain.Entities;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>登录账号仓储（t_user）。事务边界由应用服务层控制。</summary>
    public interface IUserRepository
    {
        AuthUser FindByUsername(IDbConnection connection, string username);

        /// <summary>登录成功：重置失败计数、清除锁定、记录最后登录时间。</summary>
        void UpdateLoginSuccess(IDbConnection connection, IDbTransaction transaction, AuthUser user);

        /// <summary>登录失败：更新失败计数与锁定截止时间（P-02）。</summary>
        void UpdateLoginFailure(IDbConnection connection, IDbTransaction transaction, AuthUser user);

        void UpdatePassword(IDbConnection connection, IDbTransaction transaction, int userId, string newPasswordHash);
    }
}
