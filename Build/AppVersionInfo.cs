using System;
using System.Reflection;

namespace PropertyManagement
{
    /// <summary>
    /// 运行期版本读取（唯一入口，v1.1.1 起）：界面文案与健康检查统一取此处，
    /// 值来自程序集特性，特性由 Build\ProductInfo.targets 按 Build\Version.props 生成。
    /// </summary>
    internal static class AppVersionInfo
    {
        /// <summary>三位展示版本（如 1.1.1）。</summary>
        internal static readonly string Display = ResolveDisplay();

        /// <summary>程序集版本（如 1.1.1.0），用于“关于/详情”类信息。</summary>
        internal static readonly Version Assembly = typeof(AppVersionInfo).Assembly.GetName().Version;

        /// <summary>界面版本串（如 ANYI  V1.1.1）。</summary>
        internal static readonly string BrandLine = "ANYI  V" + Display;

        private static string ResolveDisplay()
        {
            var assembly = typeof(AppVersionInfo).Assembly;
            var attributes = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
            if (attributes != null && attributes.Length > 0)
            {
                var informational = (AssemblyInformationalVersionAttribute)attributes[0];
                if (!string.IsNullOrEmpty(informational.InformationalVersion))
                {
                    return informational.InformationalVersion;
                }
            }

            // 兜底：程序集特性缺失时回落为程序集版本的前三位
            var version = assembly.GetName().Version;
            return version == null
                ? "0.0.0"
                : version.Major + "." + version.Minor + "." + version.Build;
        }
    }
}
