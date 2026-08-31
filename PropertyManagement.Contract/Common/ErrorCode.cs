namespace PropertyManagement.Contract.Common
{
    /// <summary>
    /// 全局错误码约定（骨架版）。
    /// 五位分段：业务子系统码(2位) + 业务错误码(3位)；公共段见下，业务段随 M1 契约逐用例补全。
    /// </summary>
    public static class ErrorCode
    {
        public const int Success = 0;

        // 公共段 4xxxx
        public const int BadRequest = 40000;
        public const int Unauthorized = 40100;
        public const int LoginLocked = 40101; // 登录失败次数超限，账号锁定（P-02，契约文档 §四）
        public const int Forbidden = 40300;
        public const int NotFound = 40400;
        public const int Conflict = 40900;
        public const int ValidationFailed = 42200;
        public const int InternalError = 50000;
        public const int ServiceUnavailable = 50300;
    }
}
