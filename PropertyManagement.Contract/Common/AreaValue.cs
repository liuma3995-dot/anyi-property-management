using System;
using System.Globalization;
using System.Text;

namespace PropertyManagement.Contract.Common
{
    /// <summary>
    /// 建筑面积口径（v1.1.0 · F-01／F-02）——前后端唯一真值来源。
    ///
    /// 背景：V1.0.0 房产表单的建筑面积输入框把文本双向直连到 <c>decimal</c> 属性并逐字符回写，
    /// 中间态（如 <c>123.</c>）无法转换 → 输入框内容被回滚 → 小数点永远输不进去（Win7 现场反馈 F-01）。
    /// 同时列表展示用 <c>ToString("0.0")</c> 强制 1 位小数，把 138.66 显示成 138.7（F-02）。
    ///
    /// 口径（2026-09-16 负责人裁定）：
    /// 1) <b>输入</b>：允许全角数字/句点；尾随小数点属合法中间态（<c>123.</c> → 123）；最多 2 位小数，超出即提示；
    /// 2) <b>展示</b>：最多 2 位小数、去尾零、<b>不做四舍五入</b>（显示即存值），统一带「㎡」；
    /// 3) <b>导入/服务端</b>：只要求「正数」，<b>不新增小数位硬限制</b>（宽容解析，避免误伤历史数据）。
    /// </summary>
    public static class AreaValue
    {
        /// <summary>输入与展示允许的最大小数位。</summary>
        public const int DisplayDecimals = 2;

        /// <summary>面积单位（展示用）。</summary>
        public const string Unit = "㎡";

        /// <summary>
        /// 文本归一化：全角数字 → 半角、全角句点/句号 → 半角小数点、去空白、去千分位逗号。
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                char c = ch;
                if (c >= '\uFF10' && c <= '\uFF19') c = (char)(c - 0xFEE0);   // ０-９ → 0-9
                if (char.IsWhiteSpace(c)) continue;                           // 半角/全角空白
                if (c == '\u3002' || c == '\uFF0E') c = '.';                  // 。／． → .
                if (c == ',' || c == '\uFF0C') continue;                      // 千分位逗号
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>归一化文本的小数位（尾随小数点按 0 位计）。</summary>
        public static int CountDecimals(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return 0;
            int dot = normalized.IndexOf('.');
            if (dot < 0 || dot == normalized.Length - 1) return 0;
            return normalized.Length - dot - 1;
        }

        /// <summary>
        /// 宽容解析（导入／服务端）：只把文本转成数值，**不限制小数位**；
        /// 正数判定由调用方负责（沿用原「建筑面积必须大于 0」口径）。
        /// </summary>
        public static bool TryParseLoose(string text, out decimal value)
        {
            value = 0m;
            string n = Normalize(text);
            if (n.Length == 0) return false;
            if (n.EndsWith(".", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 1);
            if (n.Length == 0) return false;
            return decimal.TryParse(n, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// 表单输入解析（严格）：尾随小数点合法（<c>123.</c> → 123）、最多 2 位小数、拒绝其它字符。
        /// 返回 false 时 <paramref name="error"/> 为可直接展示的行内提示。
        /// </summary>
        public static bool TryParseInput(string text, out decimal value, out string error)
        {
            value = 0m;
            error = null;
            string n = Normalize(text);
            if (n.Length == 0) { error = "请输入建筑面积"; return false; }
            foreach (char c in n)
            {
                if ((c < '0' || c > '9') && c != '.') { error = "建筑面积格式不正确，如 88.5"; return false; }
            }
            if (n.IndexOf('.') != n.LastIndexOf('.')) { error = "建筑面积格式不正确，如 88.5"; return false; }
            if (CountDecimals(n) > DisplayDecimals) { error = "建筑面积最多 2 位小数"; return false; }
            string body = n.EndsWith(".", StringComparison.Ordinal) ? n.Substring(0, n.Length - 1) : n;
            if (body.Length == 0) { error = "建筑面积格式不正确，如 88.5"; return false; }
            if (!decimal.TryParse(body, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
            {
                error = "建筑面积格式不正确，如 88.5";
                return false;
            }
            return true;
        }

        /// <summary>小数位（按 decimal 自带刻度，保留尾零语义）。</summary>
        public static int ScaleOf(decimal value)
        {
            return (decimal.GetBits(value)[3] >> 16) & 0xFF;
        }

        /// <summary>是否为「超出 2 位小数」的存量值（历史导入数据可能高精度）。</summary>
        public static bool HasExcessDecimals(decimal value)
        {
            return ScaleOf(value) > DisplayDecimals;
        }

        /// <summary>
        /// 展示文本（去尾零），不含单位：
        /// 小数位 ≤ 2 → 最多 2 位展示（不四舍五入，138.66 不再变 138.7）；
        /// 小数位 &gt; 2 → **原样输出**，保证「显示即存值」，不篡改存量高精度数据。
        /// </summary>
        public static string Format(decimal value)
        {
            return HasExcessDecimals(value)
                ? value.ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>展示文本（含单位）：列表／详情／导出回显统一走这里。</summary>
        public static string FormatWithUnit(decimal value)
        {
            return Format(value) + Unit;
        }
    }
}
