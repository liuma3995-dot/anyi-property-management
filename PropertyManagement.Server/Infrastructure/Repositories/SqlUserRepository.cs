using System;
using System.Collections.Generic;
using System.Data;
using Dapper;
using PropertyManagement.Server.Domain.Entities;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>t_user 仓储实现（SQLite/Dapper）。
    /// PG-COM-04：password_changed_at/must_change_password/t_password_history。</summary>
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

        public void UpdatePassword(IDbConnection connection, IDbTransaction transaction, int userId, string newPasswordHash, DateTime passwordChangedAt)
        {
            connection.Execute(
                "UPDATE t_user SET password_hash = @hash, password_changed_at = @changedAt, " +
                "must_change_password = 0, updated_at = @now WHERE id = @id",
                new { hash = newPasswordHash, changedAt = passwordChangedAt, now = DateTime.Now, id = userId },
                transaction);
        }

        public UserPasswordState GetPasswordState(IDbConnection connection, int userId)
        {
            return connection.QueryFirstOrDefault<UserPasswordState>(
                "SELECT password_changed_at AS PasswordChangedAt, must_change_password AS MustChangePassword " +
                "FROM t_user WHERE id = @id", new { id = userId }) ?? new UserPasswordState();
        }

        public IList<string> GetRecentPasswordHashes(IDbConnection connection, int userId, int count)
        {
            return connection.Query<string>(
                "SELECT password_hash FROM t_password_history WHERE user_id = @userId " +
                "ORDER BY id DESC LIMIT @count",
                new { userId, count }).AsList();
        }

        public void InsertPasswordHistory(IDbConnection connection, IDbTransaction transaction, int userId, string passwordHash)
        {
            connection.Execute(
                "INSERT INTO t_password_history (user_id, password_hash) VALUES (@userId, @passwordHash)",
                new { userId, passwordHash },
                transaction);
        }

        public void TrimPasswordHistory(IDbConnection connection, IDbTransaction transaction, int userId, int keep)
        {
            connection.Execute(
                "DELETE FROM t_password_history WHERE user_id = @userId AND id NOT IN (" +
                "SELECT id FROM t_password_history WHERE user_id = @userId ORDER BY id DESC LIMIT @keep)",
                new { userId, keep },
                transaction);
        }

        /// <summary>R17：个人信息读取（t_user 扩展列，空值统一返回空字符串便于前端绑定）。</summary>
        public PropertyManagement.Contract.Auth.UserProfileDto GetProfile(IDbConnection connection, string username)
        {
            return connection.QueryFirstOrDefault<PropertyManagement.Contract.Auth.UserProfileDto>(
                "SELECT id, username AS UserName, COALESCE(display_name,'') AS DisplayName, " +
                "COALESCE(phone,'') AS Phone, COALESCE(bio,'') AS Bio, COALESCE(avatar_key,'') AS AvatarKey " +
                "FROM t_user WHERE username = @username", new { username });
        }

        /// <summary>R17：个人信息保存（四项均可空；空字符串写入 NULL，保持库内干净）。</summary>
        public void UpdateProfile(IDbConnection connection, IDbTransaction transaction, int userId,
            string displayName, string phone, string bio, string avatarKey)
        {
            connection.Execute(
                "UPDATE t_user SET display_name = @displayName, phone = @phone, bio = @bio, avatar_key = @avatarKey, " +
                "updated_at = datetime('now','localtime') WHERE id = @userId",
                new
                {
                    displayName = NullIfEmpty(displayName),
                    phone = NullIfEmpty(phone),
                    bio = NullIfEmpty(bio),
                    avatarKey = NullIfEmpty(avatarKey),
                    userId
                },
                transaction);
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
