using System;

namespace PropertyManagement.Contract.Auth
{
    /// <summary>登录结果（UC-COM-001）。
    /// PG-COM-04：MustChangePassword=true 时前端强制跳转修改密码（首登/超 90 天/管理员标记）。</summary>
    public class LoginResult
    {
        public string Token { get; set; }

        public string DisplayName { get; set; }

        public DateTime ExpiresAt { get; set; }

        public bool MustChangePassword { get; set; } // 90 天强制改密 / 首次登录强制改密
    }
}
