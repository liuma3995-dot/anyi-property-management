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
    /// 数据库初始化（M2 D2-1/D2-2 + M4 增补）：
    /// 首次启动执行 schema.sql + seed.sql，并以 BCrypt 写入管理员种子；
    /// 存量库按 schema_version 执行增量迁移（migration_00X.sql，M4 起支持）；
    /// DevSeedEnabled=true 时（仅开发机，源码默认 false）执行 dev-seed.sql 演示造数。
    /// </summary>
    public static class DatabaseInitializer
    {
        private const int SchemaVersion = 56;

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static readonly string AdminUsername =
            ConfigurationManager.AppSettings["DefaultAdminUsername"] ?? "admin";

        private static readonly string DefaultAdminPassword =
            ConfigurationManager.AppSettings["DefaultAdminPassword"] ?? "Admin@123";

        private static readonly bool DevSeedEnabled = ReadDevSeedEnabled();

        /// <summary>确保数据库文件、表结构与种子数据就绪（启动时调用一次）。</summary>
        public static void EnsureInitialized()
        {
            DbConfig.EnsureDirectories();

            using (IDbConnection connection = new SqliteConnectionFactory().OpenConnection())
            {
                // 迁移可能重建表（DROP/RENAME 如 t_property），SQLite 外键约束会阻止；本连接先关闭外键（须在事务外执行）。
                connection.Execute("PRAGMA foreign_keys = OFF;");
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
                    MarkVersion(connection, transaction, 1);
                    current = 1;
                    Log.Info("数据库初始化完成（v1）");
                }

                if (current > SchemaVersion)
                {
                    throw new InvalidOperationException(
                        "数据库版本高于程序预期（" + current + " > " + SchemaVersion + "），请升级程序后再启动。");
                }

                if (current < SchemaVersion)
                {
                    Log.Info("检测到数据库版本 {0}，执行增量迁移至 v{1}", current, SchemaVersion);
                    for (int version = current + 1; version <= SchemaVersion; version++)
                    {
                        string scriptName = "migration_" + version.ToString("000") + ".sql";
                        ExecuteScript(connection, transaction, LoadSql(scriptName));
                        MarkVersion(connection, transaction, version);
                        Log.Info("迁移完成：{0}（v{1}）", scriptName, version);
                    }
                }

                if (DevSeedEnabled)
                {
                    ExecuteScript(connection, transaction, LoadSql("dev-seed.sql"));
                    Log.Info("DevSeedEnabled=true：已执行演示造数 dev-seed.sql（仅开发演示，正式安装请关闭）");
                }

                transaction.Commit();
                }
            }

            Log.Info("数据库就绪：{0}", DbConfig.DatabaseFile);
        }

        private static bool ReadDevSeedEnabled()
        {
            string value = ConfigurationManager.AppSettings["DevSeedEnabled"];
            bool enabled;
            return bool.TryParse(value, out enabled) && enabled;
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
