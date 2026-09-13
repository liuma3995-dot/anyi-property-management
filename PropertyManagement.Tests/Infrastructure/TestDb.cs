using System;
using System.Configuration;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;
using PropertyManagement.Server.Infrastructure.Data;

namespace PropertyManagement.Tests.Infrastructure
{
    /// <summary>
    /// 单元测试数据库隔离设施（M7 §四 4.2 硬约束）。
    ///
    /// 隔离原理：
    ///   1. 静态构造时把进程当前目录切到 %TEMP%\pm-tests\{yyyyMMdd-HHmmss}-{pid}；
    ///   2. App.config 的 DbDirectory 是相对值 "db"，生产 <see cref="DbConfig"/> 会用
    ///      Path.GetFullPath 解析它，于是库文件落在上述临时根目录下；
    ///   3. 解析结果必须位于 %TEMP% 下，否则直接抛异常终止（防止误连生产/演示库）；
    ///   4. `DbConfig` 的 OverrideDirectory 只在类型初始化时读取一次，因此只能在
    ///      「切目录 → 首次触碰 DbConfig」的顺序下生效，且隔离粒度是「每次测试运行一个根目录」，
    ///      用例级重置统一走 <see cref="Reset"/>（删库重建）。
    /// </summary>
    public static class TestDb
    {
        private const string RunRootFolderName = "pm-tests";

        private static readonly object Gate = new object();
        private static readonly string RunRootPath;
        private static readonly string DbFilePath;
        private static readonly string DbDirectoryPath;

        static TestDb()
        {
            RunRootPath = Path.Combine(
                Path.GetTempPath(),
                RunRootFolderName,
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(RunRootPath);

            // 必须在任何 DbConfig 访问之前完成切目录（含 DatabaseInitializer / 各服务）。
            Directory.SetCurrentDirectory(RunRootPath);

            // NLog 默认配置（随 Server 复制到输出目录）指向 %ProgramData%\PropertyManagement\logs，
            // 会把测试日志写进生产目录。测试运行一律改写到临时根目录内。
            ConfigureNLog();

            string overrideDirectory = ConfigurationManager.AppSettings["DbDirectory"];
            if (string.IsNullOrWhiteSpace(overrideDirectory))
            {
                throw new InvalidOperationException(
                    "测试隔离校验失败：PropertyManagement.Tests.dll.config 未设置 DbDirectory。" +
                    "测试工程必须显式指向临时目录，禁止回落到 %ProgramData%\\PropertyManagement。");
            }

            if (Path.IsPathRooted(overrideDirectory))
            {
                throw new InvalidOperationException(
                    "测试隔离校验失败：DbDirectory 必须使用相对路径（当前值 " + overrideDirectory +
                    "），以便由 TestDb 切到 %TEMP% 后的运行根目录解析。");
            }

            DbDirectoryPath = Path.GetFullPath(overrideDirectory);
            DbFilePath = Path.Combine(DbDirectoryPath, "data", "property.db");
            VerifyIsolation();
        }

        /// <summary>本次测试运行的临时根目录（运行结束由 <see cref="Cleanup"/> 删除）。</summary>
        public static string RunDirectory
        {
            get { return RunRootPath; }
        }

        /// <summary>把 NLog 输出重定向到本次运行的临时目录（避免写 %ProgramData%）。</summary>
        private static void ConfigureNLog()
        {
            string logDirectory = Path.Combine(RunRootPath, "logs");
            Directory.CreateDirectory(logDirectory);

            var config = new LoggingConfiguration();
            var target = new FileTarget("unit-test-file")
            {
                FileName = Path.Combine(logDirectory, "server-${shortdate}.log"),
                Layout = "${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}"
            };
            config.AddTarget(target);
            config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, target);
            LogManager.Configuration = config;
            LogManager.ReconfigExistingLoggers();
        }

        /// <summary>解析后的测试库目录（应为 %TEMP%\pm-tests\{...}\db）。</summary>
        public static string DatabaseDirectory
        {
            get { return DbDirectoryPath; }
        }

        /// <summary>解析后的测试库文件（应为 %TEMP%\pm-tests\{...}\db\data\property.db）。</summary>
        public static string DatabaseFile
        {
            get { return DbFilePath; }
        }

        /// <summary>生产库防护断言：库文件必须落在 %TEMP% 下。</summary>
        public static void VerifyIsolation()
        {
            string tempRoot = Path.GetFullPath(Path.GetTempPath());
            string dbFile = Path.GetFullPath(DbFilePath);
            if (!dbFile.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "测试隔离校验失败：测试库路径 " + dbFile + " 不在临时目录 " + tempRoot +
                    " 下，已终止测试以避免污染生产/演示数据。");
            }

            if (dbFile.IndexOf("PropertyManagement" + Path.DirectorySeparatorChar + "data",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException(
                    "测试隔离校验失败：测试库路径 " + dbFile + " 疑似生产库位置，已终止测试。");
            }
        }

        /// <summary>按生产初始化路径建库（schema + seed + 迁移至当前 SchemaVersion）。</summary>
        public static void EnsureInitialized()
        {
            VerifyIsolation();
            DatabaseInitializer.EnsureInitialized();
        }

        /// <summary>用例级重置：删除库文件后按生产路径重建（替代无法切换的 DbDirectory）。</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                VerifyIsolation();
                SQLiteConnection.ClearAllPools();
                foreach (string file in new[] { DbFilePath, DbFilePath + "-journal", DbFilePath + "-wal", DbFilePath + "-shm" })
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }

                EnsureInitialized();
            }
        }

        /// <summary>打开一个测试库连接（调用方负责释放）。</summary>
        public static IDbConnection OpenConnection()
        {
            VerifyIsolation();
            return new SqliteConnectionFactory().OpenConnection();
        }

        /// <summary>删除本次运行的临时根目录（Fixture 结束 / run-tests.ps1 -Clean）。</summary>
        public static void Cleanup()
        {
            SQLiteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(RunRootPath))
                {
                    Directory.Delete(RunRootPath, true);
                }
            }
            catch (IOException)
            {
                // 残留清理失败不影响测试结论，可由 run-tests.ps1 -Clean 兜底。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
