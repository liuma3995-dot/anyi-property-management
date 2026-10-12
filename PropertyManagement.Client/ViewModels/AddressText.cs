namespace PropertyManagement.Client.ViewModels
{
    /// <summary>
    /// 「楼栋/单元/房号」拼接口径唯一实现（CHG-v2.0.0-18）。
    ///
    /// 规则（与服务端 <c>SqlAddress.UnitSegment</c> 一致）：
    ///   ① 单元为空 → 该段整段省略，不出现多余分隔符；
    ///   ② 单元非空 → 已以「单元」结尾则原样输出，否则补「单元」（填「1」→「1单元」，填「1单元」→ 仍是「1单元」）；
    ///   ③ 楼栋 / 房号为空各自省略；三项全空返回空串（调用方据此决定是否追加）。
    ///
    /// 业主档案下拉、车位维护「绑定业主」下拉等展示口径都走这里，避免各写一套造成漂移。
    /// </summary>
    internal static class AddressText
    {
        public static string Build(string buildingNo, string unitNo, string roomNo)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(buildingNo)) { parts.Add(buildingNo.Trim()); }
            if (!string.IsNullOrWhiteSpace(unitNo))
            {
                string unit = unitNo.Trim();
                parts.Add(unit.EndsWith("单元", System.StringComparison.Ordinal) ? unit : unit + "单元");
            }
            if (!string.IsNullOrWhiteSpace(roomNo)) { parts.Add(roomNo.Trim()); }
            return string.Join("/", parts);
        }
    }
}
