namespace PropertyManagement.Contract.Auth
{
    /// <summary>修改密码请求（UC-COM-002）。</summary>
    public class ChangePasswordRequest
    {
        public string OldPassword { get; set; }

        public string NewPassword { get; set; }
    }
}
