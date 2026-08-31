using System;
using System.Data;
using Dapper;
using PropertyManagement.Server.Domain.Entities;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>t_user 仓储实现（SQLite/Dapper）。</summary>
    public class SqlUserRepository : IUserRepository
    {
        public AuthUser FindByUsername(IDbConnection connection, string username)
        {
            return connection.QueryFirstOrDefault<AuthUser>(
                "SELECT id, username, password_hash AS PasswordHash, status, " +
                "last_login_at AS LastLoginAt, login_fail_count AS LoginFailCount, " +
                "locked_until AS LockedUntil " +
                "FROM t_user WHERE username = @username",
                new { username });
        }

        public void UpdateLoginSuccess(IDbConnection connection, IDbTransaction transaction, AuthUser user)
        {
            connection.Execute(
                "UPDATE t_user SET login_fail_count = 0, locked_until = NULL, " +
                "last_login_at = @now, updated_at = @now WHERE id = @id",
                new { now = DateTime.Now, id = user.Id },
                transaction);
        }

        public void UpdateLoginFailure(IDbConnection connection, IDbTransaction transaction, AuthUser user)
        {
            connection.Execute(
                "UPDATE t_user SET login_fail_count = @count, locked_until = @lockedUntil, " +
                "updated_at = @now WHERE id = @id",
                new
                {
                    count = user.LoginFailCount,
                    lockedUntil = user.LockedUntil,
                    now = DateTime.Now,
                    id = user.Id
                },
                transaction);
        }

        public void UpdatePassword(IDbConnection connection, IDbTransaction transaction, int userId, string newPasswordHash)
        {
            connection.Execute(
                "UPDATE t_user SET password_hash = @hash, updated_at = @now WHERE id = @id",
                new { hash = newPasswordHash, now = DateTime.Now, id = userId },
                transaction);
        }
    }
}
