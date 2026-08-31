using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Health;

namespace PropertyManagement.Client.Services
{
    /// <summary>服务联动（D3-4）：端口占用检测、健康探测、后端自拉起。</summary>
    public static class BackendLauncher
    {
        private const string DefaultBaseUrl = "http://127.0.0.1:5210";

        public static bool IsPortInUse(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var task = client.ConnectAsync("127.0.0.1", port);
                    return task.Wait(TimeSpan.FromMilliseconds(500)) && client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task<HealthResponse> ProbeAsync(int timeoutMs = 2000)
        {
            using (var http = new HttpClient
            {
                BaseAddress = new Uri(DefaultBaseUrl),
                Timeout = TimeSpan.FromMilliseconds(timeoutMs)
            })
            {
                string json = await http.GetStringAsync("api/v1/health");
                var resp = JsonConvert.DeserializeObject<ApiResponse<HealthResponse>>(json);
                return resp == null ? null : resp.Data;
            }
        }

        /// <summary>尝试拉起后端进程；找不到后端可执行文件时返回 false。</summary>
        public static bool TryStartServer()
        {
            string exe = FindServerExecutable();
            if (exe == null)
            {
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 定位后端可执行文件：优先开发布局（Client/bin → ../../Server/bin），
        /// 其次安装布局（Client 同级 Server/，M8 安装包定稿后校对）。
        /// </summary>
        public static string FindServerExecutable()
        {
            string clientDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(clientDir, "..", "..", "..", "Server", "bin", "Debug", "PropertyManagement.Server.exe"),
                Path.Combine(clientDir, "..", "..", "..", "Server", "bin", "Release", "PropertyManagement.Server.exe"),
                Path.Combine(clientDir, "Server", "PropertyManagement.Server.exe")
            };

            foreach (string candidate in candidates)
            {
                string full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }
            return null;
        }
    }
}
