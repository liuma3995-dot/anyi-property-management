using System;

namespace PropertyManagement.Contract.Auth
{
    /// <summary>登录结果（UC-COM-001）。</summary>
    public class LoginResult
    {
        public string Token { get; set; }

        public string DisplayName { get; set; }

        public DateTime ExpiresAt { get; set; }
    }
}
