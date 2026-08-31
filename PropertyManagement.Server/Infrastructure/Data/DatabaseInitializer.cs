using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Reflection;
using System.Text;
using Dapper;
using NLog;
using PropertyManagement.Server.Infrastructure.Security;

namespace PropertyManagement.Server.Infrastructure.Data
{
    /// <summary>
    /// 数据库初始化（M2 D2-1/D2-2）：
    /// 首次启动执行 schema.sql + seed.sql，并以 BCrypt 写入管理员种子；
    /// 后续启动仅校验 schema_version，幂等；版本不一致时快速失败（升级走后续切片）。
    /// </summary>
    public static class DatabaseInitializer
    {
        private const int SchemaVersion = 1;
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static readonly string AdminUsername =
            ConfigurationManager.AppSettings["DefaultAdminUsername"] ?? "admin";

        private static readonly string DefaultAdminPassword =
            ConfigurationManager.AppSettings["DefaultAdminPassword"] ?? "Admin@123";

        /// <summary>确保数据库文件、表结构与种子数据就绪（启动时调用一次）。</summary>
        public static void EnsureInitialized()
        {
            DbConfig.EnsureDirectories();

            using (IDbConnection connection = new SqliteConnectionFactory().OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                EnsureVersionTable(connection, transaction);
                int current = GetCurrentVersion(connection, transaction);

                if (current == 0)
                {
                    Log.Info("首次启动：执行数据库初始化（schema v{0}）", SchemaVersion);
                    ExecuteScript(connection, transaction, LoadSql("schema.sql"));
                    ExecuteScript(connection, transaction, LoadSql("seed.sql"));
                    SeedAdmin(connection, transaction);
                    MarkVersion(connection, transaction, SchemaVersion);
                    Log.Info("数据库初始化完成");
                }
                else if (current == SchemaVersion)
                {
                    Log.Info("数据库版本一致（schema v{0}），跳过初始化", SchemaVersion);
                }
                else if (current > SchemaVersion)
                {
                    throw new InvalidOperationException(
                        "数据库版本高于程序预期（" + current + " > " + SchemaVersion + "），请升级程序后再启动。");
                }
                else
                {
                    throw new InvalidOperationException(
                        "数据库版本低于程序预期（" + current + " < " + SchemaVersion +
                        "），请先备份数据文件，再按迁移流程升级。");
                }

                transaction.Commit();
            }

            Log.Info("数据库就绪：{0}", DbConfig.DatabaseFile);
        }

        private static void EnsureVersionTable(IDbConnection connection, IDbTransaction transaction)
        {
            connection.Execute(
                "CREATE TABLE IF NOT EXISTS schema_version (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                "version INTEGER NOT NULL, " +
                "applied_at TEXT NOT NULL DEFAULT (datetime('now','localtime')))",
                transaction: transaction);
        }

        private static int GetCurrentVersion(IDbConnection connection, IDbTransaction transaction)
        {
            int? version = connection.ExecuteScalar<int?>(
                "SELECT MAX(version) FROM schema_version",
                transaction: transaction);
            return version ?? 0;
        }

        private static void MarkVersion(IDbConnection connection, IDbTransaction transaction, int version)
        {
            connection.Execute(
                "INSERT INTO schema_version (version) VALUES (@version)",
                new { version },
                transaction);
        }

        private static void ExecuteScript(
            IDbConnection connection,
            IDbTransaction transaction,
            string script)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                throw new InvalidOperationException("数据库脚本为空，初始化中止。");
            }

            connection.Execute(script, transaction: transaction);
        }

        private static void SeedAdmin(IDbConnection connection, IDbTransaction transaction)
        {
            int count = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_user WHERE username = @username",
                new { username = AdminUsername },
                transaction);

            if (count > 0)
            {
                return;
            }

            string hash = PasswordHasher.Hash(DefaultAdminPassword);
            connection.Execute(
                "INSERT INTO t_user (username, password_hash, status) VALUES (@username, @hash, 0)",
                new { username = AdminUsername, hash },
                transaction);

            Log.Info("已初始化管理员账号：{0}", AdminUsername);
        }

        private static string LoadSql(string name)
        {
            string resourceName = "PropertyManagement.Server.Infrastructure.Data." + name;

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("找不到内嵌资源：" + resourceName);
                }

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
