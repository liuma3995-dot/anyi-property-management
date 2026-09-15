using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PropertyManagement.Client.Services
{
    /// <summary>启动编排状态（M8 T8-4-1）。</summary>
    public enum BackendStartupState
    {
        /// <summary>首次探测前（未启动）。</summary>
        NotStarted,

        /// <summary>探测中且未触发兜底拉起。</summary>
        Probing,

        /// <summary>已触发兜底拉起，正在等待服务就绪。</summary>
        Starting,

        /// <summary>探测通过。</summary>
        Ready,

        /// <summary>等待窗口用尽仍未就绪。</summary>
        TimedOut
    }

    /// <summary>单次进度回调载荷（供登录页状态区展示，含重试次数）。</summary>
    public sealed class BackendStartupProgress
    {
        public BackendStartupState State { get; set; }
        public int Attempt { get; set; }
        public int TotalAttempts { get; set; }
        public bool FallbackLaunched { get; set; }
        public bool PortOccupied { get; set; }
        public string Text { get; set; }
    }

    /// <summary>编排结果。</summary>
    public sealed class BackendStartupResult
    {
        public bool Ready { get; set; }
        public bool FallbackLaunched { get; set; }
        public bool ServerExecutableFound { get; set; }
        public bool PortOccupied { get; set; }
        public int Attempts { get; set; }
    }

    /// <summary>
    /// 客户端启动编排（M8 T8-4-1~T8-4-3，D8-4 口径 1）：
    /// 启动后以 `GET /health` 为**唯一判定依据**探测本地后端；未就绪时按需**兜底拉起**一次
    /// （同目录可解析到 `PropertyManagement.Server.exe` 时，隐藏窗口启动），
    /// 并在「墙钟约 20 s、最多 20 次、单次探测 2 s 超时」的等待窗口内轮询；超时给出含日志路径的排查指引。
    /// 说明：探测失败**不降级 Mock**（保持 `Api.Mode=Http`），避免掩盖"服务未就绪"。
    /// </summary>
    public static class BackendStartup
    {
        public const int DefaultAttempts = 20;
        public const int AttemptTimeoutMs = 2000;
        public const int RetryDelayMs = 800;

        /// <summary>
        /// 等待窗口的墙钟预算（M8 T8-4-1 口径「约 15~20 s」）。
        /// 说明：本机对「无监听端口」的探测实测约 2 s 才返回，单纯按次数轮询会拖到 ~56 s，
        /// 故以**墙钟预算**为界、20 次为上限，保证用户等待时间可预期。
        /// </summary>
        public const int DefaultTimeBudgetMs = 20000;

        public const int ServerPort = 5210;

        public static async Task<BackendStartupResult> EnsureAsync(
            Action<BackendStartupProgress> report,
            int attempts = DefaultAttempts,
            int timeBudgetMs = DefaultTimeBudgetMs)
        {
            if (attempts <= 0)
            {
                attempts = DefaultAttempts;
            }
            if (timeBudgetMs <= 0)
            {
                timeBudgetMs = DefaultTimeBudgetMs;
            }

            var result = new BackendStartupResult();
            string serverExe = BackendLauncher.FindServerExecutable();
            result.ServerExecutableFound = serverExe != null;
            var stopwatch = Stopwatch.StartNew();

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                // 至少执行一次探测；其后按墙钟预算收敛等待窗口
                if (attempt > 1 && stopwatch.ElapsedMilliseconds >= timeBudgetMs)
                {
                    break;
                }

                result.Attempts = attempt;
                result.PortOccupied = BackendLauncher.IsPortInUse(ServerPort);

                if (!result.FallbackLaunched)
                {
                    Report(report, result, BackendStartupState.NotStarted, attempt, attempts,
                        "● 本地服务未启动，正在等待…");
                }
                else
                {
                    Report(report, result, BackendStartupState.Starting, attempt, attempts,
                        string.Format("● 正在启动本地服务…（第 {0}/{1} 次探测）", attempt, attempts));
                }

                if (await ProbeAsync())
                {
                    result.Ready = true;
                    Report(report, result, BackendStartupState.Ready, attempt, attempts, "● 本地服务正常");
                    return result;
                }

                // 首次失败即兜底拉起一次（T8-4-2）；找不到后端可执行文件则如实呈现
                if (!result.FallbackLaunched && serverExe != null)
                {
                    result.FallbackLaunched = BackendLauncher.TryStartServer();
                }

                if (attempt < attempts)
                {
                    await Task.Delay(RetryDelayMs);
                }
            }

            result.PortOccupied = BackendLauncher.IsPortInUse(ServerPort);
            Report(report, result, BackendStartupState.TimedOut, attempts, attempts,
                "● 本地服务未就绪（已超时）");
            return result;
        }

        /// <summary>单次健康探测；异常一律视为未就绪。</summary>
        private static async Task<bool> ProbeAsync()
        {
            try
            {
                var health = await BackendLauncher.ProbeAsync(AttemptTimeoutMs);
                return health != null && string.Equals(health.Status, "ok", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>超时排查指引（T8-4-3，含日志路径）。</summary>
        public static string BuildTimeoutGuidance(BackendStartupResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            if (!result.ServerExecutableFound)
            {
                return "未找到 PropertyManagement.Server.exe（应与客户端安装在同一安装目录下）。"
                     + @"请重新安装客户端，或手动启动后端后重试。日志目录：%ProgramData%\PropertyManagement\logs";
            }

            if (result.PortOccupied)
            {
                return "端口 5210 已被占用但健康检查无响应。请确认没有其它程序占用该端口"
                     + @"（netstat -ano | findstr 5210），结束后重试。日志目录：%ProgramData%\PropertyManagement\logs";
            }

            return "本地服务启动超时。请手动运行安装目录下的 PropertyManagement.Server.exe 后重试；"
                 + @"仍失败时查看日志目录：%ProgramData%\PropertyManagement\logs";
        }

        private static void Report(
            Action<BackendStartupProgress> report,
            BackendStartupResult result,
            BackendStartupState state,
            int attempt,
            int totalAttempts,
            string text)
        {
            if (report == null)
            {
                return;
            }

            report(new BackendStartupProgress
            {
                State = state,
                Attempt = attempt,
                TotalAttempts = totalAttempts,
                FallbackLaunched = result.FallbackLaunched,
                PortOccupied = result.PortOccupied,
                Text = text
            });
        }
    }
}
