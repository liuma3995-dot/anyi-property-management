namespace PropertyManagement.Contract.Auth
{
    /// <summary>登录请求（UC-COM-001）。</summary>
    public class LoginRequest
    {
        public string UserName { get; set; }

        public string Password { get; set; }
    }
}
