using System;
using System.Configuration;
using System.Net.Http;

namespace PropertyManagement.Client.Services
{
    /// <summary>
    /// API 客户端工厂（M3-D3）：
    /// Api.Mode=Http 强制真实后端；Mock 强制演示数据；Auto（默认）先探测健康检查，
    /// 通过则用真实 HTTP，否则降级演示数据并在状态栏明示。
    /// </summary>
    public static class ApiClientFactory
    {
        public static IApiClient Create()
        {
            string mode = (ConfigurationManager.AppSettings["Api.Mode"] ?? "Auto").Trim();
            string baseUrl = ConfigurationManager.AppSettings["Api.BaseUrl"] ?? HttpApiClient.DefaultBaseAddress;

            if (string.Equals(mode, "Http", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpApiClient(baseUrl);
            }
            if (string.Equals(mode, "Mock", StringComparison.OrdinalIgnoreCase))
            {
                return new MockApiClient();
            }

            // Auto：短超时探测，通过则走真实后端
            try
            {
                using (var probe = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(2) })
                {
                    var task = probe.GetStringAsync("health");
                    if (task.Wait(TimeSpan.FromSeconds(2)))
                    {
                        string json = task.Result;
                        if (!string.IsNullOrEmpty(json) && json.Contains("\"code\":0"))
                        {
                            return new HttpApiClient(baseUrl);
                        }
                    }
                }
            }
            catch
            {
                // 探测失败 → 降级演示数据
            }

            return new MockApiClient();
        }
    }
}
