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
                    // 关键（M8 D8-4 修复）：控制台程序仅靠 WindowStyle=Hidden 仍会创建并显示控制台窗口，
                    // 必须同时置 CreateNoWindow，后端才能完全隐藏在后台（避免用户误触关闭服务）。
                    CreateNoWindow = true,
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
        /// 其次安装布局（M8 D8-1 定稿：`{app}\Client\` 与 `{app}\Server\` 并列，见 T8-1-2），
        /// 最后兼容 Client 目录内直放 Server 的扁平布局。
        /// </summary>
        public static string FindServerExecutable()
        {
            string clientDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                // 安装布局（M8 T8-1-2 口径）：{app}\Client\..\Server\PropertyManagement.Server.exe
                Path.Combine(clientDir, "..", "Server", "PropertyManagement.Server.exe"),
                // 开发布局（源码仓库）：Client\bin\{Debug|Release} → 仓库根\PropertyManagement.Server\bin\{Debug|Release}
                // CHG-v1.1.2-37：原候选写成「仓库根\Server\bin\...」，而仓库实际目录是 PropertyManagement.Server，
                // 导致 Debug 客户端**永远找不到后端可执行文件**（后端没起来时只能干等，页面取数失败又无提示）。
                Path.Combine(clientDir, "..", "..", "..", "PropertyManagement.Server", "bin", "Debug", "PropertyManagement.Server.exe"),
                Path.Combine(clientDir, "..", "..", "..", "PropertyManagement.Server", "bin", "Release", "PropertyManagement.Server.exe"),
                // 兼容旧命名副本
                Path.Combine(clientDir, "..", "..", "..", "Server", "bin", "Debug", "PropertyManagement.Server.exe"),
                Path.Combine(clientDir, "..", "..", "..", "Server", "bin", "Release", "PropertyManagement.Server.exe"),
                // 扁平布局
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
