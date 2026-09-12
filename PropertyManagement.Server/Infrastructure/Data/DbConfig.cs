using System;
using System.Configuration;
using System.IO;

namespace PropertyManagement.Server.Infrastructure.Data
{
    /// <summary>
    /// 数据目录配置（DZ-3 落地，M2-D3）。
    /// 默认统一 %ProgramData%\PropertyManagement\；支持 App.config DbDirectory 覆盖（开发/测试用）。
    /// </summary>
    internal static class DbConfig
    {
        private static readonly string OverrideDirectory = ConfigurationManager.AppSettings["DbDirectory"];

        /// <summary>根目录：%ProgramData%\PropertyManagement（或 DbDirectory 覆盖）。</summary>
        public static string RootDirectory
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(OverrideDirectory))
                {
                    return Path.GetFullPath(OverrideDirectory);
                }

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "PropertyManagement");
            }
        }

        public static string DataDirectory => Path.Combine(RootDirectory, "data");
        public static string LogDirectory => Path.Combine(RootDirectory, "logs");
        public static string BackupDirectory => Path.Combine(RootDirectory, "backups");
        public static string ExportDirectory => Path.Combine(RootDirectory, "exports");
        /// <summary>附件物理存储根目录（data/attachments；纠纷扫描件在 dispute\{caseId}\ 下）。</summary>
        public static string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public static string ConfigDirectory => Path.Combine(RootDirectory, "config");

        public static string DatabaseFile => Path.Combine(DataDirectory, "property.db");
        public static string TokenKeyFile => Path.Combine(ConfigDirectory, "token.key");

        /// <summary>SQLite 连接串：启用外键、ISO 8601 时间文本存储。</summary>
        public static string ConnectionString =>
            "Data Source=" + DatabaseFile + ";Version=3;Foreign Keys=True;DateTimeFormat=ISO8601;";

        /// <summary>确保数据目录骨架存在（data/logs/backups/exports/config）。</summary>
        public static void EnsureDirectories()
        {
            string[] directories =
            {
                DataDirectory,
                LogDirectory,
                BackupDirectory,
                ExportDirectory,
                AttachmentsDirectory,
                ConfigDirectory
            };

            foreach (string directory in directories)
            {
                Directory.CreateDirectory(directory);
            }
        }
    }
}
