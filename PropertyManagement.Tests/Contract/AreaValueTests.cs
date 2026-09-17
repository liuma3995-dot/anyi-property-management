using System;
using PropertyManagement.Contract.Common;
using Xunit;

namespace PropertyManagement.Tests.Contract
{
    /// <summary>
    /// 建筑面积口径单元测试（v1.1.0 · F-01／F-02，CHG-v1.1.0-03）。
    /// 口径：输入最多 2 位小数、尾随小数点合法；展示最多 2 位去尾零且**不四舍五入**。
    /// </summary>
    public class AreaValueTests
    {
        [Theory]
        [InlineData("123.", 123)]
        [InlineData("123.6", 123.6)]
        [InlineData("123.66", 123.66)]
        [InlineData("0.5", 0.5)]
        public void TryParseInput_中间态与合法值_可解析(string text, double expected)
        {
            decimal value;
            string error;
            Assert.True(AreaValue.TryParseInput(text, out value, out error));
            Assert.Equal((decimal)expected, value);
            Assert.Null(error);
        }

        [Theory]
        [InlineData("123。66", "123.66")]
        [InlineData("１２３．６６", "123.66")]
        [InlineData("1,234.5", "1234.5")]
        [InlineData(" 88 ", "88")]
        public void Normalize_全角与千分位_归一化为半角(string text, string expected)
        {
            Assert.Equal(expected, AreaValue.Normalize(text));
        }

        [Fact]
        public void TryParseInput_全角句点_等价半角小数点()
        {
            decimal value;
            string error;
            Assert.True(AreaValue.TryParseInput("123。66", out value, out error));
            Assert.Equal(123.66m, value);
        }

        [Theory]
        [InlineData("")]
        [InlineData(".")]
        [InlineData("12.3.4")]
        [InlineData("abc")]
        [InlineData("88.666")]
        public void TryParseInput_非法输入_拒绝并给出行内提示(string text)
        {
            decimal value;
            string error;
            Assert.False(AreaValue.TryParseInput(text, out value, out error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void TryParseInput_超过两位小数_提示文案明确()
        {
            decimal value;
            string error;
            Assert.False(AreaValue.TryParseInput("88.666", out value, out error));
            Assert.Equal("建筑面积最多 2 位小数", error);
        }

        [Theory]
        [InlineData("138.66", 138.66)]
        [InlineData("138.666", 138.666)]
        [InlineData("88.5", 88.5)]
        public void TryParseLoose_导入解析_不限制小数位(string text, double expected)
        {
            decimal value;
            Assert.True(AreaValue.TryParseLoose(text, out value));
            Assert.Equal((decimal)expected, value);
        }

        [Fact]
        public void Format_两位小数_不四舍五入()
        {
            Assert.Equal("138.66", AreaValue.Format(138.66m));
            Assert.Equal("138.66㎡", AreaValue.FormatWithUnit(138.66m));
        }

        [Fact]
        public void Format_尾零_去掉()
        {
            Assert.Equal("138.6", AreaValue.Format(138.60m));
            Assert.Equal("88", AreaValue.Format(88.00m));
        }

        [Fact]
        public void Format_仅一位小数_原样展示()
        {
            Assert.Equal("88.5", AreaValue.Format(88.5m));
            Assert.Equal("88.5㎡", AreaValue.FormatWithUnit(88.5m));
        }

        [Fact]
        public void Format_存量高精度值_原样展示不篡改()
        {
            // 历史导入可能留下 3 位以上小数：显示必须等于存值，不能被四舍五入
            Assert.Equal("88.125", AreaValue.Format(88.125m));
            Assert.True(AreaValue.HasExcessDecimals(88.125m));
            Assert.False(AreaValue.HasExcessDecimals(88.12m));
        }
    }
}
