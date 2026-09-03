using System.Collections.Generic;
using System.Data;
using Dapper;
using PropertyManagement.Server.Domain.Entities;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>t_dict_type / t_dict_item 仓储实现（只读；CRUD 在 M6 系统设置切片落地）。</summary>
    public class SqlDictRepository : IDictRepository
    {
        public IEnumerable<DictTypeEntry> ListTypes(IDbConnection connection)
        {
            return connection.Query<DictTypeEntry>(
                "SELECT id, type_code AS TypeCode, type_name AS TypeName FROM t_dict_type ORDER BY id");
        }

        public IEnumerable<DictItemEntry> ListItems(IDbConnection connection, string typeCode)
        {
            return connection.Query<DictItemEntry>(
                "SELECT id, type_code AS TypeCode, item_code AS ItemCode, item_name AS ItemName, remark, " +
                "sort, status FROM t_dict_item WHERE type_code = @typeCode AND status = 0 ORDER BY sort",
                new { typeCode });
        }

        public DictItemEntry GetItemByName(IDbConnection connection, string typeCode, string itemName)
        {
            return connection.QueryFirstOrDefault<DictItemEntry>(
                "SELECT id, type_code AS TypeCode, item_code AS ItemCode, item_name AS ItemName, remark, " +
                "sort, status FROM t_dict_item WHERE type_code = @typeCode AND item_name = @itemName AND status = 0 LIMIT 1",
                new { typeCode, itemName });
        }

        public int InsertItem(IDbConnection connection, IDbTransaction transaction, DictItemEntry item)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_dict_item (type_code, item_code, item_name, remark, sort, status) " +
                "VALUES (@TypeCode, @ItemCode, @ItemName, @Remark, @Sort, @Status); SELECT last_insert_rowid();",
                new { item.TypeCode, item.ItemCode, item.ItemName, item.Remark, item.Sort, item.Status }, transaction);
        }
    }
}
