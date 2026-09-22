namespace PropertyManagement.Server.Infrastructure
{
    /// <summary>
    /// 地址「楼栋/单元/房号」SQL 片段口径统一处（CHG-v1.2.0-39）。
    ///
    /// 背景（负责人 2026-09-22 反馈）：欠费台账「楼栋/房号」列在单元名本身已含「单元」时
    /// 会拼成「1栋/1单元单元/101」—— 各取数点原实现都写成 unit_no || '单元'。
    ///
    /// 口径（与客户端 PaymentEntryViewModel / RefundAdjustmentViewModel 的地址格式化一致）：
    ///   ① 单元为空 → 该段整段省略，只显示「楼栋/房号」（无单元房产不出现多余分隔符）；
    ///   ② 单元非空 → 已以「单元」结尾则原样输出，否则补「单元」（单元填「1」→「1单元」，
    ///      填「1单元」→ 仍是「1单元」）。
    /// </summary>
    internal static class SqlAddress
    {
        /// <summary>单元段（含前导斜杠）；单元为空时返回空串。</summary>
        public static string UnitSegment(string unitColumn)
        {
            string unit = "TRIM(" + unitColumn + ")";
            return "CASE WHEN COALESCE(" + unit + ", '') <> '' " +
                   "THEN '/' || CASE WHEN " + unit + " LIKE '%单元' THEN " + unit + " " +
                   "ELSE " + unit + " || '单元' END " +
                   "ELSE '' END";
        }

        /// <summary>「楼栋/单元/房号」完整路径；楼栋、房号为空时各自省略。</summary>
        public static string BuildingUnitRoom(string buildingColumn, string unitColumn, string roomColumn)
        {
            return "COALESCE(" + buildingColumn + ", '') || " +
                   UnitSegment(unitColumn) + " || " +
                   "CASE WHEN COALESCE(" + roomColumn + ", '') <> '' THEN '/' || " + roomColumn + " ELSE '' END";
        }
    }
}
