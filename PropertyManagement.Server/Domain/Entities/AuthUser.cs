using System;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Server.Domain.Entities
{
    /// <summary>登录账号（t_user；login_fail_count/locked_until 为 M2-D4 实施列）。</summary>
    public class AuthUser
    {
        public int Id { get; set; }

        public string Username { get; set; }

        public string PasswordHash { get; set; }

        public UserStatus Status { get; set; }

        public DateTime? LastLoginAt { get; set; }

        public int LoginFailCount { get; set; }

        public DateTime? LockedUntil { get; set; }
    }
}
