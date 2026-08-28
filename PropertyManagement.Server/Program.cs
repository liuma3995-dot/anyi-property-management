using System;
using System.Configuration;
using Microsoft.Owin.Hosting;
using NLog;

namespace PropertyManagement.Server
{
    /// <summary>
    /// 本地后端服务入口（M0 骨架）。
    /// 职责：读取监听地址、启动 OWIN 自承载宿主、优雅停止。
    /// 注意：仅绑定 127.0.0.1 回环地址，不暴露局域网（技术选型 §七）。
    /// </summary>
    internal static class Program
    {
        private const string DefaultBaseAddress = "http://127.0.0.1:5210";

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static void Main(string[] args)
        {
            string baseAddress = ConfigurationManager.AppSettings["BaseAddress"];
            if (string.IsNullOrWhiteSpace(baseAddress))
            {
                baseAddress = DefaultBaseAddress;
            }

            Console.WriteLine("PropertyManagement.Server 启动中...");
            Log.Info("PropertyManagement.Server 启动中，监听地址：{0}", baseAddress);

            try
            {
                using (WebApp.Start<Startup>(baseAddress))
                {
                    Console.WriteLine("服务已启动：" + baseAddress);
                    Console.WriteLine("健康检查：GET " + baseAddress + "/api/v1/health");
                    Console.WriteLine("按任意键停止服务...");
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                DumpException(ex);
                Log.Fatal("服务启动失败");
            }

            Log.Info("服务已停止");
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
                Console.WriteLine(line);
                Log.Fatal(line);

                if (!string.IsNullOrEmpty(current.StackTrace))
                {
                    Console.WriteLine(current.StackTrace);
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
