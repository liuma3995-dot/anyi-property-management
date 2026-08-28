using System;

namespace PropertyManagement.Contract.Health
{
    /// <summary>服务健康检查响应（骨架期用于前后端连通验证）。</summary>
    public class HealthResponse
    {
        public string Service { get; set; }

        public string Version { get; set; }

        public string Status { get; set; }

        public DateTime ServerTime { get; set; }
    }
}
