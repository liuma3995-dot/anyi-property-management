using System;
using System.IO;

namespace PropertyManagement.Client.Services
{
    /// <summary>崩溃日志（M3 修复期新增）：启动与运行期未处理异常写入 %LocalAppData%\PropertyManagement\crash.log。</summary>
    public static class CrashLog
    {
        private static readonly object Gate = new object();

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PropertyManagement");
                return Path.Combine(dir, "crash.log");
            }
        }

        public static void Write(Exception ex)
        {
            try
            {
                lock (Gate)
                {
                    string dir = Path.GetDirectoryName(FilePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.AppendAllText(
                        FilePath,
                        "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]" + Environment.NewLine +
                        ex + Environment.NewLine +
                        new string('-', 80) + Environment.NewLine);
                }
            }
            catch
            {
                // 日志失败不阻断
            }
        }
    }
}
