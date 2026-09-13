using System;
using System.Configuration;
using System.IO;
using Dapper;
using PropertyManagement.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PropertyManagement.Tests
{
    /// <summary>
    /// T7-9-1 全链路冒烟：验证「老式 csproj + MSBuild + xunit.console + 真 SQLite 临时库」可用，
    /// 并确认测试工程的 App.config 被运行器采用（DbDirectory 生效）。
    /// </summary>
    public class SmokeTests
    {
        private readonly ITestOutputHelper _output;

        public SmokeTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void 冒烟_运行器采用测试工程AppConfig_且当前目录位于临时根目录()
        {
            string overrideDirectory = ConfigurationManager.AppSettings["DbDirectory"];
            _output.WriteLine("CurrentDirectory = " + Environment.CurrentDirectory);
            _output.WriteLine("DbDirectory(AppSettings) = " + overrideDirectory);
            _output.WriteLine("RunDirectory = " + TestDb.RunDirectory);
            _output.WriteLine("DatabaseFile = " + TestDb.DatabaseFile);

            Assert.Equal("db", overrideDirectory);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Environment.CurrentDirectory);
            Assert.Equal(TestDb.RunDirectory, Environment.CurrentDirectory);
        }

        [Fact]
        public void 冒烟_初始化测试库_建到当前SchemaVersion且位于临时目录()
        {
            TestDb.Reset();

            Assert.True(File.Exists(TestDb.DatabaseFile), "测试库文件应已创建：" + TestDb.DatabaseFile);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), TestDb.DatabaseFile);

            using (var connection = TestDb.OpenConnection())
            {
                int version = connection.ExecuteScalar<int>(
                    "SELECT MAX(version) FROM schema_version;");
                int tables = connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';");
                _output.WriteLine("SchemaVersion = " + version + "；表数量 = " + tables);

                Assert.True(version >= 29, "测试库 schema 版本应 >= 29，实际 " + version);
                Assert.True(tables > 20, "测试库表数量异常，实际 " + tables);
            }
        }

        [Fact]
        public void 冒烟_NLog输出重定向_不写生产日志目录()
        {
            var config = NLog.LogManager.Configuration;

            Assert.NotNull(config);
            Assert.NotEmpty(config.AllTargets);
            foreach (NLog.Targets.Target target in config.AllTargets)
            {
                var fileTarget = target as NLog.Targets.FileTarget;
                if (fileTarget == null)
                {
                    continue;
                }
                string fileName = fileTarget.FileName.ToString();
                Assert.StartsWith(TestDb.RunDirectory, fileName);
                Assert.DoesNotContain("CommonApplicationData", fileName);
            }
        }
    }
}
