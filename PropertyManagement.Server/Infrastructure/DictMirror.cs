using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;

namespace PropertyManagement.Server.Infrastructure
{
    /// <summary>
    /// 业务模块 → 参数字典（t_dict_item）**单向镜像**（CHG-v2.0.0-06，负责人 2026-10-11 裁定 8.A）。
    ///
    /// 背景（负责人 2026-10-11 反馈「支出登记新增分类没有同步到参数/字典维护 → 支出分类」）：
    /// 支出分类 / 电话分类 / 设备类型 / 纠纷类型 / 应急场景这 5 个维度各有「业务表 + 字典表」两本账，
    /// 业务模块写入后字典侧永远看不到。经只读实测（本轮），只有收费项目那 4 类字典是真正同源的。
    ///
    /// 口径（单向：**业务模块是唯一写入方**，字典侧只作统一查看）：
    ///   ① 业务新增 → 字典补一条（同 code 或同名已存在则不重复补）；
    ///   ② 业务改名 → 字典对应项同步改名（按 code 优先、名称兜底定位）；
    ///   ③ 业务删除（软删）→ 字典对应项一并软删；
    ///   ④ 字典侧删除**不回写**业务表；已软删的字典项也不会因为再次读取而"复活"（第 ④ 条关键）。
    /// </summary>
    internal static class DictMirror
    {
        public const string ExpenseCategory = "expense_category";
        public const string PhoneCategory = "phone_category";
        public const string EquipmentType = "equipment_type";
        public const string DisputeType = "dispute_type";
        public const string EmergencyScene = "emergency_scene";
        public const string ParkingType = "parking_type";

        /// <summary>字典项（含已软删：软删视为「管理员有意移除」，不再复活）。</summary>
        private class DictRow
        {
            public int Id { get; set; }
            public string ItemCode { get; set; }
            public string ItemName { get; set; }
            public int Sort { get; set; }
            public int DelFlag { get; set; }
        }

        /// <summary>按 code 或名称定位字典项（不过滤 del_flag —— 用于判断"是否被有意删除"）。</summary>
        private static DictRow FindItem(IDbConnection connection, string typeCode, string itemCode, string itemName)
        {
            return connection.QueryFirstOrDefault<DictRow>(
                "SELECT id AS Id, item_code AS ItemCode, item_name AS ItemName, sort AS Sort, del_flag AS DelFlag " +
                "FROM t_dict_item WHERE type_code = @typeCode AND del_flag = 0 " +
                "  AND ((@itemCode <> '' AND item_code = @itemCode) OR item_name = @itemName) " +
                "ORDER BY id LIMIT 1",
                new { typeCode, itemCode = itemCode ?? string.Empty, itemName = itemName ?? string.Empty });
        }

        /// <summary>
        /// 业务新增：字典补一条同名项（已存在同 code / 同名项时不动，含已软删的情况）。
        /// 返回是否真的新增了字典项。
        /// </summary>
        public static bool Ensure(IDbConnection connection, IDbTransaction transaction, string typeCode,
            string itemCode, string itemName, int sort, string operatorName = null)
        {
            if (connection == null || string.IsNullOrWhiteSpace(typeCode) || string.IsNullOrWhiteSpace(itemName))
            {
                return false;
            }
            string name = itemName.Trim();
            string code = string.IsNullOrWhiteSpace(itemCode) ? BuildCode(typeCode, name) : itemCode.Trim();

            DictRow existing = connection.QueryFirstOrDefault<DictRow>(
                "SELECT id AS Id, item_code AS ItemCode, item_name AS ItemName, sort AS Sort, del_flag AS DelFlag " +
                "FROM t_dict_item WHERE type_code = @typeCode " +
                "  AND ((@itemCode <> '' AND item_code = @itemCode) OR item_name = @itemName) " +
                "ORDER BY del_flag, id LIMIT 1",
                new { typeCode, itemCode = code, itemName = name }, transaction);
            if (existing != null) { return false; }

            connection.Execute(
                "INSERT INTO t_dict_item (type_code, item_code, item_name, sort, status, updated_by, updated_at) " +
                "VALUES (@typeCode, @code, @name, @sort, 0, @operatorName, datetime('now','localtime'))",
                new { typeCode, code, name, sort, operatorName }, transaction);
            return true;
        }

        /// <summary>业务改名：字典对应项同步改名（定位不到则按新增处理）。</summary>
        public static void Rename(IDbConnection connection, IDbTransaction transaction, string typeCode,
            string itemCode, string previousName, string itemName, string operatorName = null)
        {
            if (connection == null || string.IsNullOrWhiteSpace(typeCode) || string.IsNullOrWhiteSpace(itemName))
            {
                return;
            }
            string name = itemName.Trim();
            if (string.Equals(previousName == null ? null : previousName.Trim(), name, StringComparison.Ordinal))
            {
                return;
            }
            DictRow existing = FindItem(connection, typeCode, itemCode, previousName == null ? string.Empty : previousName.Trim());
            if (existing == null)
            {
                Ensure(connection, transaction, typeCode, itemCode, name, 0, operatorName);
                return;
            }
            connection.Execute(
                "UPDATE t_dict_item SET item_name = @name, updated_by = @operatorName, " +
                "updated_at = datetime('now','localtime') WHERE id = @id",
                new { id = existing.Id, name, operatorName }, transaction);
        }

        /// <summary>业务删除（软删）：字典对应项一并软删留痕。</summary>
        public static void Remove(IDbConnection connection, IDbTransaction transaction, string typeCode,
            string itemCode, string itemName, string operatorName = null)
        {
            if (connection == null || string.IsNullOrWhiteSpace(typeCode))
            {
                return;
            }
            DictRow existing = FindItem(connection, typeCode, itemCode, itemName == null ? string.Empty : itemName.Trim());
            if (existing == null || existing.DelFlag == 1) { return; }
            connection.Execute(
                "UPDATE t_dict_item SET del_flag = 1, updated_by = @operatorName, " +
                "updated_at = datetime('now','localtime') WHERE id = @id",
                new { id = existing.Id, operatorName }, transaction);
        }

        /// <summary>code 缺省生成规则：<c>类型前缀 + 名称哈希</c>（同一名称稳定可复现，不随 id 漂移）。</summary>
        private static string BuildCode(string typeCode, string name)
        {
            int hash = 17;
            foreach (char ch in name)
            {
                hash = hash * 31 + ch;
            }
            return typeCode.Replace("_", string.Empty) + "_" + (hash & 0x7FFFFFF).ToString("X");
        }

        /// <summary>
        /// 从业务表补齐字典（幂等）：供「系统设置 → 参数/字典维护」打开某类型前调用，
        /// 使**升级前就已存在**的业务数据也能在字典里看到（无需数据迁移脚本）。
        /// 返回实际补写/对齐的字典项数（0 = 无变化）。
        /// </summary>
        public static int SyncFromBusiness(IDbConnection connection, IDbTransaction transaction,
            string typeCode, string operatorName = null)
        {
            if (connection == null || string.IsNullOrWhiteSpace(typeCode)) { return 0; }
            switch (typeCode.Trim())
            {
                case ExpenseCategory:
                    return SyncRows(connection, transaction, typeCode, operatorName,
                        "SELECT COALESCE(NULLIF(category_type, ''), 'cat' || id) AS Code, name AS Name, id AS Sort " +
                        "FROM t_expense_category WHERE del_flag = 0 ORDER BY id");
                case PhoneCategory:
                    return SyncRows(connection, transaction, typeCode, operatorName,
                        "SELECT 'phone' || id AS Code, name AS Name, COALESCE(sort, id) AS Sort " +
                        "FROM t_phone_category WHERE del_flag = 0 ORDER BY sort, id");
                case EquipmentType:
                    return SyncRows(connection, transaction, typeCode, operatorName,
                        "SELECT 'dev' || id AS Code, name AS Name, id AS Sort " +
                        "FROM t_device_type WHERE del_flag = 0 ORDER BY id");
                case DisputeType:
                    return SyncRows(connection, transaction, typeCode, operatorName,
                        "SELECT 'dispute' || id AS Code, name AS Name, id AS Sort " +
                        "FROM t_dispute_type WHERE del_flag = 0 ORDER BY id");
                case EmergencyScene:
                    return SyncRows(connection, transaction, typeCode, operatorName,
                        "SELECT 'scene' || id AS Code, name AS Name, id AS Sort " +
                        "FROM t_emergency_scene WHERE del_flag = 0 ORDER BY id");
                case ParkingType:
                    // 车位维护的「类型」是**业务枚举**（产权 / 普通 / 临时，含 BR-INF-03 禁售规则），
                    // 不改成读字典；此处只把字典项**文案对齐**枚举口径，避免同一维度两套说法。
                    return AlignParkingTypeText(connection, transaction, operatorName);
                default:
                    return 0;
            }
        }

        private class BusinessRow
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public int Sort { get; set; }
        }

        /// <summary>
        /// 把业务表行补进字典，并把业务侧已不存在的字典项软删（= 「取值对齐」）。
        /// 两条安全阀：
        ///   ① 业务表为空时**只补不删** —— 避免某个模块刚好没有数据时把整本字典清空；
        ///   ② 已软删的字典项视为管理员有意移除，不复活（<see cref="Ensure"/> 内保证）。
        /// </summary>
        private static int SyncRows(IDbConnection connection, IDbTransaction transaction, string typeCode,
            string operatorName, string sql)
        {
            int changed = 0;
            List<BusinessRow> rows = connection.Query<BusinessRow>(sql, null, transaction).ToList();
            foreach (BusinessRow row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Name)) { continue; }
                string name = row.Name.Trim();
                if (Ensure(connection, transaction, typeCode, row.Code, name, row.Sort, operatorName)) { changed++; }
            }
            if (rows.Count > 0)
            {
                var alive = new HashSet<string>(
                    rows.Where(x => !string.IsNullOrWhiteSpace(x.Name)).Select(x => x.Name.Trim()),
                    StringComparer.Ordinal);
                changed += RetireStale(connection, transaction, typeCode, alive, operatorName);
            }
            return changed;
        }

        /// <summary>字典里业务侧已不存在的项 → 软删留痕（模块为唯一真值；避免同一维度两套说法并存）。</summary>
        private static int RetireStale(IDbConnection connection, IDbTransaction transaction, string typeCode,
            HashSet<string> aliveNames, string operatorName)
        {
            List<DictRow> live = connection.Query<DictRow>(
                "SELECT id AS Id, item_code AS ItemCode, item_name AS ItemName, del_flag AS DelFlag " +
                "FROM t_dict_item WHERE type_code = @typeCode AND del_flag = 0",
                new { typeCode }, transaction).ToList();
            int changed = 0;
            foreach (DictRow item in live)
            {
                if (!string.IsNullOrWhiteSpace(item.ItemName) && aliveNames.Contains(item.ItemName.Trim()))
                {
                    continue;
                }
                connection.Execute(
                    "UPDATE t_dict_item SET del_flag = 1, updated_by = @operatorName, " +
                    "updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                    new { id = item.Id, operatorName }, transaction);
                changed++;
            }
            return changed;
        }

        /// <summary>车位类型字典文案对齐业务枚举（产权 / 普通 / 临时）；item_code 保持不变，避免影响既有引用。</summary>
        private static int AlignParkingTypeText(IDbConnection connection, IDbTransaction transaction, string operatorName)
        {
            int changed = 0;
            changed += connection.Execute(
                "UPDATE t_dict_item SET item_name = '产权', updated_by = @operatorName, " +
                "updated_at = datetime('now','localtime') " +
                "WHERE type_code = @typeCode AND item_code = 'owned' AND del_flag = 0 AND item_name <> '产权'",
                new { typeCode = ParkingType, operatorName }, transaction);
            changed += connection.Execute(
                "UPDATE t_dict_item SET item_name = '普通', updated_by = @operatorName, " +
                "updated_at = datetime('now','localtime') " +
                "WHERE type_code = @typeCode AND item_code = 'civil' AND del_flag = 0 AND item_name <> '普通'",
                new { typeCode = ParkingType, operatorName }, transaction);
            changed += connection.Execute(
                "UPDATE t_dict_item SET item_name = '临时', updated_by = @operatorName, " +
                "updated_at = datetime('now','localtime') " +
                "WHERE type_code = @typeCode AND item_code = 'rent' AND del_flag = 0 AND item_name <> '临时'",
                new { typeCode = ParkingType, operatorName }, transaction);
            return changed;
        }
    }
}
