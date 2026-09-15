using System;
using System.Configuration;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Owin.Hosting;
using NLog;

namespace PropertyManagement.Server
{
    /// <summary>
    /// 本地后端服务入口。
    /// 职责：单实例互斥 → 读取监听地址 → 启动 OWIN 自承载宿主 → 等待停止。
    /// 注意：仅绑定 127.0.0.1 回环地址，不暴露局域网（技术选型 §七）。
    ///
    /// M8 T8-2-1 改造（D8-2 / D8-4 口径 2）：
    /// 1) **单实例互斥**：优先 `Global\PropertyManagement.Server.SingleInstance`，
    ///    非提权会话缺少 `SeCreateGlobalPrivilege` 时回退 `Local\...`；重复启动**静默退出且不抢 5210**；
    /// 2) **无控制台健壮化**：自启/隐藏窗口/标准输入被重定向时不再依赖 `Console.ReadKey`
    ///    （M7 已知坑：无控制台时 `ReadKey` 立即返回 → 进程退出），改为阻塞等待至进程被终止。
    /// </summary>
    internal static class Program
    {
        private const string DefaultBaseAddress = "http://127.0.0.1:5210";
        /// <summary>
        /// 单实例互斥体基名。实际名称追加**监听端点作用域**（host_port）：
        /// 生产固定 5210，同名端点重复启动才静默退出；隔离测试/多实例场景使用其它端口时互不干扰。
        /// （M8 T8-2-1 实测修正：不加作用域会把不同端口的第二个后端一并拦截。）
        /// </summary>
        private const string GlobalMutexBase = @"Global\PropertyManagement.Server.SingleInstance";
        private const string LocalMutexBase = @"Local\PropertyManagement.Server.SingleInstance";

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>持有单实例互斥体引用：进程存活期间保持占用。</summary>
        private static Mutex _singleInstance;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        private static void Main(string[] args)
        {
            string baseAddress = ConfigurationManager.AppSettings["BaseAddress"];
            if (string.IsNullOrWhiteSpace(baseAddress))
            {
                baseAddress = DefaultBaseAddress;
            }

            if (!AcquireSingleInstance(baseAddress))
            {
                Log.Info("检测到同一监听地址已有实例在运行，本次启动静默退出（单实例互斥）：{0}", baseAddress);
                TryWriteLine("已有实例在运行，本次启动退出。");
                return;
            }

            TryWriteLine("PropertyManagement.Server 启动中...");
            Log.Info("PropertyManagement.Server 启动中，监听地址：{0}", baseAddress);

            try
            {
                using (WebApp.Start<Startup>(baseAddress))
                {
                    TryWriteLine("服务已启动：" + baseAddress);
                    TryWriteLine("健康检查：GET " + baseAddress + "/api/v1/health");
                    WaitForShutdownSignal();
                }
            }
            catch (AggregateException ex) when (ContainsInner<HttpListenerException>(ex))
            {
                DumpException(ex);
                Log.Fatal("服务启动失败：端口 {0} 无法监听（可能已被占用，或当前账号缺少 URL 保留权限）", baseAddress);
                LogPortGuidance(baseAddress);
            }
            catch (HttpListenerException ex)
            {
                DumpException(ex);
                Log.Fatal("服务启动失败：端口 {0} 无法监听（错误码 {1}）", baseAddress, ex.ErrorCode);
                LogPortGuidance(baseAddress);
            }
            catch (Exception ex)
            {
                DumpException(ex);
                Log.Fatal("服务启动失败");
            }
            finally
            {
                ReleaseSingleInstance();
                Log.Info("服务已停止");
            }
        }

        /// <summary>
        /// 单实例获取：Global → Local → 放行（互斥体完全不可用时不阻断启动，端口占用由启动阶段报错）。
        /// 互斥体名称按监听端点作用域隔离，避免误伤不同端口（隔离测试/多实例）的后端。
        /// </summary>
        private static bool AcquireSingleInstance(string baseAddress)
        {
            bool createdNew;
            string scope = BuildEndpointScope(baseAddress);

            Mutex global = TryCreateMutex(GlobalMutexBase + "." + scope, out createdNew);
            if (global != null)
            {
                if (!createdNew)
                {
                    global.Dispose();
                    return false;
                }
                _singleInstance = global;
                Log.Info("单实例互斥体已获取：{0}.{1}", GlobalMutexBase, scope);
                return true;
            }

            Mutex local = TryCreateMutex(LocalMutexBase + "." + scope, out createdNew);
            if (local != null)
            {
                if (!createdNew)
                {
                    local.Dispose();
                    return false;
                }
                _singleInstance = local;
                Log.Info("单实例互斥体已获取：{0}.{1}（全局命名空间不可用，已回退本地会话）", LocalMutexBase, scope);
                return true;
            }

            Log.Warn("单实例互斥体不可用，跳过单实例检查（端口占用仍会在启动阶段被拦截）");
            return true;
        }

        /// <summary>由监听地址推导互斥体作用域（host_port，仅保留字母数字与下划线）。</summary>
        private static string BuildEndpointScope(string baseAddress)
        {
            string raw = baseAddress;
            try
            {
                var uri = new Uri(baseAddress, UriKind.Absolute);
                raw = uri.Host + "_" + uri.Port;
            }
            catch
            {
                // 非标准地址：退化为原始字符串
            }

            var builder = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            return builder.Length == 0 ? "default" : builder.ToString();
        }

        /// <summary>创建并占用互斥体；失败返回 null（由调用方决定回退策略）。</summary>
        private static Mutex TryCreateMutex(string name, out bool createdNew)
        {
            createdNew = false;
            try
            {
                bool created;
                var mutex = new Mutex(true, name, out created);
                createdNew = created;
                return mutex;
            }
            catch (UnauthorizedAccessException)
            {
                // 非提权会话缺少 SeCreateGlobalPrivilege（Global\ 专用）等场景
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn("创建互斥体 {0} 失败：{1}", name, SafeMessage(ex));
                return null;
            }
        }

        private static void ReleaseSingleInstance()
        {
            if (_singleInstance == null)
            {
                return;
            }

            try
            {
                _singleInstance.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 互斥体非本线程持有时忽略：进程退出即释放
            }
            catch (Exception ex)
            {
                Log.Warn("释放单实例互斥体失败：{0}", SafeMessage(ex));
            }
            finally
            {
                _singleInstance.Dispose();
                _singleInstance = null;
            }
        }

        /// <summary>
        /// 等待停止信号：有交互控制台时按任意键；无控制台/句柄被重定向（自启、隐藏窗口、管道）时阻塞等待。
        /// 两种形态都保证进程**持续存活**，不会因 `ReadKey` 立即返回而退出。
        /// </summary>
        private static void WaitForShutdownSignal()
        {
            if (HasInteractiveConsole())
            {
                TryWriteLine("按任意键停止服务...");
                try
                {
                    Console.ReadKey(true);
                    return;
                }
                catch (InvalidOperationException)
                {
                    // 运行期句柄被重定向/断开 → 退化为阻塞等待
                }
            }
            else
            {
                TryWriteLine("无交互控制台（自启/隐藏窗口形态）：服务持续运行，随进程终止而结束。");
            }

            using (var stop = new ManualResetEventSlim(false))
            {
                stop.Wait();
            }
        }

        private static bool HasInteractiveConsole()
        {
            try
            {
                return GetConsoleWindow() != IntPtr.Zero && !Console.IsInputRedirected;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>控制台可能不存在：写失败不影响服务运行。</summary>
        private static void TryWriteLine(string text)
        {
            try
            {
                Console.WriteLine(text);
            }
            catch
            {
                // 无控制台句柄等场景，忽略
            }
        }

        private static bool ContainsInner<TException>(AggregateException ex) where TException : Exception
        {
            foreach (Exception inner in ex.Flatten().InnerExceptions)
            {
                if (inner is TException)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>端口占用/URL 保留缺失时的排查指引（P-08 路径⑤、P-12）。</summary>
        private static void LogPortGuidance(string baseAddress)
        {
            Log.Fatal("排查指引：①确认 5210 未被其它进程占用（netstat -ano | findstr 5210）；" +
                      "②若为权限问题（错误码 5 / AccessDenied），以管理员执行 " +
                      "netsh http add urlacl url={0}/ user=%USERDOMAIN%\\%USERNAME% 后重试；" +
                      "③查看日志 {1}",
                      baseAddress.TrimEnd('/'),
                      @"%ProgramData%\PropertyManagement\logs");
        }

        private static void DumpException(Exception ex)
        {
            Exception current = ex;
            int depth = 0;
            while (current != null)
            {
                string line = string.Format(
                    "第 {0} 层异常：类型={1}；消息={2}",
                    depth,
                    current.GetType().FullName,
                    SafeMessage(current));
                TryWriteLine(line);
                Log.Fatal(line);

                if (!string.IsNullOrEmpty(current.StackTrace))
                {
                    TryWriteLine(current.StackTrace);
                }

                current = current.InnerException;
                depth++;
            }
        }

        private static string SafeMessage(Exception ex)
        {
            try
            {
                return ex.Message;
            }
            catch
            {
                return "（消息获取失败）";
            }
        }
    }
}
