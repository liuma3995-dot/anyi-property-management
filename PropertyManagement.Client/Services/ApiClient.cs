using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Health;

namespace PropertyManagement.Client.Services
{
    /// <summary>
    /// API 客户端封装（M0 骨架：仅健康检查）。
    /// M1 契约定稿后按端点分组扩展（auth/baseinfo/billing/payments/...），统一携带 token 与错误码处理。
    /// </summary>
    public class ApiClient
    {
        public const string DefaultBaseAddress = "http://127.0.0.1:5210/api/v1";

        private readonly HttpClient _http;

        public ApiClient()
            : this(DefaultBaseAddress)
        {
        }

        public ApiClient(string baseAddress)
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(baseAddress),
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        public async Task<HealthResponse> GetHealthAsync()
        {
            string json = await _http.GetStringAsync("health");
            var response = JsonConvert.DeserializeObject<ApiResponse<HealthResponse>>(json);
            return response == null ? null : response.Data;
        }
    }
}
