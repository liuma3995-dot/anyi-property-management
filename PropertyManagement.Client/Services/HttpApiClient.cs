using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PropertyManagement.Contract.Auth;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Health;

namespace PropertyManagement.Client.Services
{
    /// <summary>真实 HTTP 客户端：统一信封解包、token 注入、错误码映射（契约 v0.1）。</summary>
    public class HttpApiClient : IApiClient
    {
        public const string DefaultBaseAddress = "http://127.0.0.1:5210/api/v1";

        private readonly HttpClient _http;

        public bool IsMock => false;

        public HttpApiClient(string baseAddress)
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(baseAddress),
                Timeout = TimeSpan.FromSeconds(8)
            };
        }

        public Task<HealthResponse> GetHealthAsync()
        {
            return GetAsync<HealthResponse>("health");
        }

        public Task<LoginResult> LoginAsync(LoginRequest request)
        {
            return PostAsync<LoginRequest, LoginResult>("auth/login", request);
        }

        public Task LogoutAsync()
        {
            return PostAsync<object, object>("auth/logout", null);
        }

        public Task ChangePasswordAsync(ChangePasswordRequest request)
        {
            return PostAsync<ChangePasswordRequest, object>("auth/change-password", request);
        }

        public Task<DashboardDto> GetDashboardAsync()
        {
            return GetAsync<DashboardDto>("common/dashboard");
        }

        private async Task<T> GetAsync<T>(string url)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                AddToken(req);
                return await SendAsync<T>(req);
            }
        }

        private async Task<TResult> PostAsync<TBody, TResult>(string url, TBody body)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                if (body != null)
                {
                    req.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                }
                AddToken(req);
                return await SendAsync<TResult>(req);
            }
        }

        private void AddToken(HttpRequestMessage req)
        {
            var session = SessionManager.Instance.Current;
            if (session != null && !string.IsNullOrEmpty(session.Token))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            }
        }

        private async Task<T> SendAsync<T>(HttpRequestMessage req)
        {
            HttpResponseMessage resp = await _http.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(json) && resp.IsSuccessStatusCode)
            {
                return default(T);
            }

            var envelope = JsonConvert.DeserializeObject<ApiResponse<T>>(json);
            if (envelope == null || envelope.Code != ErrorCode.Success)
            {
                int code = envelope == null ? ErrorCode.InternalError : envelope.Code;
                string message = envelope == null
                    ? "服务响应异常（HTTP " + (int)resp.StatusCode + "）"
                    : envelope.Message;
                throw new ApiClientException(code, message);
            }
            return envelope.Data;
        }
    }
}
