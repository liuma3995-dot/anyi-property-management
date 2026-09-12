using System;
using System.IO;
using PdfSharp.Fonts;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// PDF 中文字体解析（F2 结案报告导出与财务报表导出共用）。
    /// PDFsharp 的 GlobalFontSettings.FontResolver 全局只能设置一次，且必须在任何字体操作之前，
    /// 因此集中在本类做一次性初始化的线程安全封装；否则多个导出服务各自设置会抛异常。
    /// </summary>
    internal static class PdfFontSupport
    {
        private static readonly object SyncRoot = new object();
        private static bool _ready;

        /// <summary>确保中文字体解析器就绪（幂等、线程安全）。</summary>
        public static void Ensure()
        {
            if (_ready)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (_ready)
                {
                    return;
                }

                GlobalFontSettings.FontResolver = new WindowsFontResolver();
                _ready = true;
            }
        }

        /// <summary>Windows 系统字体解析器（中文导出，优先 SimHei，回退 SimSun）。</summary>
        private class WindowsFontResolver : IFontResolver
        {
            public string DefaultFontName
            {
                get { return "SimHei"; }
            }

            public byte[] GetFont(string faceName)
            {
                string fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                string simHei = Path.Combine(fontsDir, "simhei.ttf");
                if (File.Exists(simHei))
                {
                    return File.ReadAllBytes(simHei);
                }

                string simSun = Path.Combine(fontsDir, "simsun.ttc");
                if (File.Exists(simSun))
                {
                    return File.ReadAllBytes(simSun);
                }

                throw new InvalidOperationException("未找到中文字体（simhei.ttf / simsun.ttc），无法导出中文 PDF");
            }

            public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
            {
                return new FontResolverInfo("SimHei");
            }
        }
    }
}
