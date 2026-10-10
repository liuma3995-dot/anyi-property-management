namespace PropertyManagement.Server.Infrastructure
{
    /// <summary>
    /// 欠费「涉及户数」身份键口径统一处（CHG-v1.4.1-07）。
    ///
    /// 背景（负责人 2026-10-10 反馈）：原实现按「楼栋 + 房号」的**展示文本**去重 ——
    ///   ① 同楼栋 + 同房号 + 不同单元（如 4栋1单元602 与 4栋2单元602）被判成同一户
    ///      → 实测 42 条算成 39 户，实际应为 40 户；
    ///   ② 业主直缴账单的「房号」位是常量「业主直缴」、自定义缴费对象是缴款人名
    ///      → 同楼栋的两位业主直缴也会被并成 1 户。
    /// 结论：拿展示文本当身份键不成立，必须用**缴费对象身份**。
    ///
    /// 现口径（2026-10-10 负责人裁定 A：归属房产优先）：
    ///   房产账单 → 本房产；车位 / 业主直缴账单 → **业主主房产**（与「楼栋/房号」列同源）；
    ///   无主房产才退回车位 / 业主 / 自定义缴款人。
    ///
    /// 台账页（前端 HouseholdKey 字段）、台账导出（PDF/Excel）、仪表盘三处共用本类表达式，杜绝分叉。
    /// </summary>
    internal static class ArrearHousehold
    {
        /// <summary>归属业主表达式（房产关系 → 车位绑定 → 账单自带），与 QueryArrears 原口径等价。</summary>
        public static string OwnerIdExpr(string billAlias)
        {
            string b = billAlias;
            return "COALESCE(" +
                   "(SELECT r.owner_id FROM t_owner_property_rel r " +
                   " WHERE r.property_id = " + b + ".property_id AND r.del_flag = 0 " +
                   " ORDER BY r.id DESC LIMIT 1), " +
                   "(SELECT psk.owner_id FROM t_parking_space psk WHERE psk.id = " + b + ".parking_id), " +
                   b + ".owner_id)";
        }

        /// <summary>
        /// 业主主房产 id：取「业主-房产关系」最近一条有效关系（口径与台账「楼栋/房号」列的
        /// ORDER BY r6.id DESC LIMIT 1 完全一致，只取 id 而非路径文本）。
        /// </summary>
        public static string PrimaryPropertyIdExpr(string billAlias)
        {
            return "(SELECT r.property_id FROM t_owner_property_rel r " +
                   "JOIN t_property p2 ON p2.id = r.property_id AND p2.del_flag = 0 " +
                   "WHERE r.owner_id = " + OwnerIdExpr(billAlias) + " AND r.del_flag = 0 " +
                   "ORDER BY r.id DESC LIMIT 1)";
        }

        /// <summary>户数身份键（同键视为同一户）。</summary>
        public static string KeyExpr(string billAlias)
        {
            string b = billAlias;
            string primaryPropertyId = PrimaryPropertyIdExpr(b);
            return "CASE " +
                   "WHEN " + b + ".property_id IS NOT NULL THEN 'P' || " + b + ".property_id " +
                   "WHEN " + primaryPropertyId + " IS NOT NULL THEN 'P' || " + primaryPropertyId + " " +
                   "WHEN " + b + ".parking_id IS NOT NULL THEN 'K' || " + b + ".parking_id " +
                   "WHEN " + b + ".owner_id IS NOT NULL THEN 'O' || " + b + ".owner_id " +
                   "ELSE 'C' || COALESCE(NULLIF(" + b + ".payer_name, ''), 'bill:' || " + b + ".id) END";
        }

        /// <summary>
        /// 台账取数范围（欠费口径）：未删除、未缴清、状态在 待缴/逾期/部分缴，且**未被移出台账**。
        /// 仪表盘与本表达式同源 —— 保证「仪表盘卡片 ↔ 台账页」两处数字对得上（负责人 2026-10-10 裁定 A）。
        /// </summary>
        public static string ActiveBillScope(string billAlias)
        {
            string b = billAlias;
            return "FROM t_bill " + b +
                   " WHERE " + b + ".del_flag = 0 AND " + b + ".amount > " + b + ".paid_amount " +
                   "AND " + b + ".status IN (0,1,2) " +
                   "AND NOT EXISTS (SELECT 1 FROM t_arrear_dismiss d WHERE d.bill_id = " + b + ".id)";
        }
    }
}
