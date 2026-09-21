using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PropertyManagement.Server.Domain
{
    /// <summary>
    /// 收费项目自定义计价公式（CHG-v1.1.2-06）。
    ///
    /// 背景：原口径下计价方式写死在代码里（仅「按建筑面积」做 单价 × 面积，其余一律等于单价），
    /// 与计费周期无关，导致「按年计费的物业费」生成账单后只按 1 个月计费且用户无法调整。
    ///
    /// 设计：只支持四则运算与括号，变量从账单上下文只读注入；**不使用任何动态求值**（无 eval、无反射），
    /// 由本类自带的词法 + 递归下降语法解析实现，纯函数、无副作用、可自检。
    ///
    /// 语法：expr := term { ('+' | '-') term }
    ///       term := factor { ('*' | '/') factor }
    ///       factor := ['-'] ( number | variable | '(' expr ')' )
    /// 变量：单价 / 面积 / 月数 / 天数 / 数量 / 年数（同时接受 pinyin 别名 unitPrice/area/months/days/quantity/years）。
    /// </summary>
    public static class ChargeFormula
    {
        /// <summary>可用变量（中文名 → 说明），供前端「变量说明」与校验提示使用。</summary>
        public static readonly string[] Variables = { "单价", "面积", "月数", "天数", "数量", "年数" };

        private static readonly Dictionary<string, string> Alias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "单价", "单价" }, { "unitprice", "单价" }, { "price", "单价" },
            { "面积", "面积" }, { "area", "面积" },
            { "月数", "月数" }, { "months", "月数" }, { "month", "月数" },
            { "天数", "天数" }, { "days", "天数" }, { "day", "天数" },
            { "数量", "数量" }, { "quantity", "数量" }, { "count", "数量" },
            { "年数", "年数" }, { "years", "年数" }, { "year", "年数" }
        };

        /// <summary>公式校验：通过返回 true；失败返回 false 并给出中文原因（供表单实时提示与接口 422）。</summary>
        public static bool TryValidate(string formula, out string error)
        {
            decimal value;
            return TryEvaluate(formula, DemoContext(), out value, out error);
        }

        /// <summary>
        /// 按公式计算金额；公式为空/空白时返回 false（调用方回落内置计价方式口径）。
        /// 变量缺失（如未维护建筑面积）或除零 → 返回 false 并给出中文原因，由调用方记入失败明细。
        /// </summary>
        public static bool TryEvaluate(string formula, IDictionary<string, decimal> context, out decimal result, out string error)
        {
            return TryEvaluate(formula, context, null, out result, out error);
        }

        /// <summary>
        /// CHG-v1.1.2-26：计量变量扩展 —— 除内置 5 个变量外，允许注入「变量库」中的自定义变量名。
        /// extraVariables 内为额外可识别变量名（须与 context 的键一致）；为空时等价于内置口径。
        /// </summary>
        public static bool TryEvaluate(string formula, IDictionary<string, decimal> context,
            ICollection<string> extraVariables, out decimal result, out string error)
        {
            result = 0m;
            error = null;
            if (string.IsNullOrWhiteSpace(formula))
            {
                error = "公式为空";
                return false;
            }

            try
            {
                var parser = new Parser(formula, context ?? new Dictionary<string, decimal>(), extraVariables);
                decimal value = parser.ParseExpression();
                if (parser.Position != parser.Length)
                {
                    error = "公式存在无法识别的内容（位置 " + (parser.Position + 1) + "），请检查是否多写了运算符或括号";
                    return false;
                }
                if (value < 0)
                {
                    error = "公式计算结果为负数，请检查公式是否写反";
                    return false;
                }
                result = Math.Round(value, 2, MidpointRounding.AwayFromZero);
                return true;
            }
            catch (FormulaException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>校验用演示上下文（只用于「公式是否合法」，不参与真实计费）。</summary>
        private static IDictionary<string, decimal> DemoContext()
        {
            return new Dictionary<string, decimal>
            {
                { "单价", 1m }, { "面积", 1m }, { "月数", 1m }, { "天数", 1m }, { "数量", 1m }
                , { "年数", 1m }
            };
        }

        private class FormulaException : Exception
        {
            public FormulaException(string message) : base(message) { }
        }

        private class Parser
        {
            private readonly string _text;
            private readonly IDictionary<string, decimal> _context;
            private readonly ICollection<string> _extraVariables;
            private int _pos;

            public Parser(string text, IDictionary<string, decimal> context)
                : this(text, context, null)
            {
            }

            public Parser(string text, IDictionary<string, decimal> context, ICollection<string> extraVariables)
            {
                _text = text;
                _context = context;
                _extraVariables = extraVariables;
            }

            public int Position { get { return _pos; } }

            public int Length { get { return _text.Length; } }

            public decimal ParseExpression()
            {
                decimal value = ParseTerm();
                while (true)
                {
                    SkipSpaces();
                    if (_pos >= _text.Length) { return value; }
                    char c = _text[_pos];
                    if (c == '+') { _pos++; value += ParseTerm(); }
                    else if (c == '-') { _pos++; value -= ParseTerm(); }
                    else if (c == '*' || c == '/') { return value; }
                    else if (c == ')') { return value; }
                    else { throw new FormulaException("公式存在无法识别的字符「" + c + "」（位置 " + (_pos + 1) + "）"); }
                }
            }

            private decimal ParseTerm()
            {
                decimal value = ParseFactor();
                while (true)
                {
                    SkipSpaces();
                    if (_pos >= _text.Length) { return value; }
                    char c = _text[_pos];
                    if (c == '*') { _pos++; value *= ParseFactor(); }
                    else if (c == '/')
                    {
                        _pos++;
                        decimal divisor = ParseFactor();
                        if (divisor == 0m) { throw new FormulaException("公式中存在除以 0 的运算"); }
                        value /= divisor;
                    }
                    else { return value; }
                }
            }

            private decimal ParseFactor()
            {
                SkipSpaces();
                if (_pos >= _text.Length) { throw new FormulaException("公式不完整：结尾缺少数字或变量"); }

                char c = _text[_pos];
                if (c == '-') { _pos++; return -ParseFactor(); }
                if (c == '+') { _pos++; return ParseFactor(); }
                if (c == '(')
                {
                    _pos++;
                    decimal inner = ParseExpression();
                    SkipSpaces();
                    if (_pos >= _text.Length || _text[_pos] != ')') { throw new FormulaException("公式括号不匹配：缺少右括号"); }
                    _pos++;
                    return inner;
                }
                if (char.IsDigit(c) || c == '.') { return ParseNumber(); }
                if (IsNameChar(c)) { return ParseVariable(); }
                throw new FormulaException("公式存在无法识别的字符「" + c + "」（位置 " + (_pos + 1) + "）");
            }

            private decimal ParseNumber()
            {
                int start = _pos;
                while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.')) { _pos++; }
                string token = _text.Substring(start, _pos - start);
                decimal value;
                if (!decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    throw new FormulaException("数字「" + token + "」格式不正确");
                }
                return value;
            }

            private decimal ParseVariable()
            {
                int start = _pos;
                while (_pos < _text.Length && IsNameChar(_text[_pos])) { _pos++; }
                string token = _text.Substring(start, _pos - start);

                string canonical;
                if (!Alias.TryGetValue(token, out canonical))
                {
                    // CHG-v1.1.2-26：变量库中的自定义变量（名称与上下文键一致）
                    if (_extraVariables != null && _extraVariables.Contains(token))
                    {
                        canonical = token;
                    }
                    else
                    {
                        string available = _extraVariables == null || _extraVariables.Count == 0
                            ? string.Join("、", Variables)
                            : string.Join("、", Variables) + "、" + string.Join("、", _extraVariables);
                        throw new FormulaException("公式中的变量「" + token + "」无效，可用变量：" + available);
                    }
                }

                decimal value;
                if (!_context.TryGetValue(canonical, out value))
                {
                    throw new FormulaException("变量「" + canonical + "」在当前缴费对象下没有取值（请检查缴费对象是否正确或数据是否维护完整）");
                }
                return value;
            }

            private static bool IsNameChar(char c)
            {
                return char.IsLetter(c) || c == '_';
            }

            private void SkipSpaces()
            {
                while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) { _pos++; }
            }
        }
    }
}
