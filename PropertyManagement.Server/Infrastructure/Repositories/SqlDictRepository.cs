using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using PropertyManagement.Server.Domain.Entities;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>t_dict_type / t_dict_item 仓储实现（PG-COM-01：显示值/修改人/修改时间增强）。</summary>
    public class SqlDictRepository : IDictRepository
    {
        private const string ItemColumns =
            "id AS Id, type_code AS TypeCode, item_code AS ItemCode, item_name AS ItemName, " +
            "display_value AS DisplayValue, remark AS Remark, sort AS Sort, status AS Status, " +
            "updated_by AS UpdatedBy, updated_at AS UpdatedAt, del_flag AS DelFlag";

        public IEnumerable<DictTypeEntry> ListTypes(IDbConnection connection)
        {
            return connection.Query<DictTypeEntry>(
                "SELECT id, type_code AS TypeCode, type_name AS TypeName FROM t_dict_type ORDER BY id");
        }

        public IEnumerable<DictItemRow> ListItems(IDbConnection connection, string typeCode, int? status)
        {
            // status=null 返回全部状态（PG-COM-01 管理端含停用可恢复）；否则按状态过滤
            // del_flag=0：已删除（R12 批量删除）字典项不再出现在任何列表
            return connection.Query<DictItemRow>(
                "SELECT " + ItemColumns + " FROM t_dict_item " +
                "WHERE type_code = @typeCode AND del_flag = 0 AND (@status IS NULL OR status = @status) " +
                "ORDER BY sort, id",
                new { typeCode, status });
        }

        public DictItemRow GetItemByName(IDbConnection connection, string typeCode, string itemName)
        {
            // 全状态查重（BR-COM-05）：与停用项重名同样拒绝，避免启用后重名混淆
            return connection.QueryFirstOrDefault<DictItemRow>(
                "SELECT " + ItemColumns + " FROM t_dict_item " +
                "WHERE type_code = @typeCode AND del_flag = 0 AND item_name = @itemName LIMIT 1",
                new { typeCode, itemName });
        }

        public int InsertItem(IDbConnection connection, IDbTransaction transaction, DictItemRow item)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_dict_item (type_code, item_code, item_name, display_value, remark, sort, status, updated_by, updated_at) " +
                "VALUES (@TypeCode, @ItemCode, @ItemName, @DisplayValue, @Remark, @Sort, @Status, @UpdatedBy, datetime('now','localtime')); " +
                "SELECT last_insert_rowid();",
                item, transaction);
        }

        public DictItemRow GetItem(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<DictItemRow>(
                "SELECT " + ItemColumns + " FROM t_dict_item WHERE id = @id AND del_flag = 0", new { id });
        }

        public void UpdateItem(IDbConnection connection, IDbTransaction transaction, DictItemRow item)
        {
            // status 由服务层传入原值（编辑保留原状态，不再写死 Enabled）
            connection.Execute(
                "UPDATE t_dict_item SET item_name = @ItemName, display_value = @DisplayValue, remark = @Remark, " +
                "sort = @Sort, status = @Status, updated_by = @UpdatedBy, updated_at = datetime('now','localtime') " +
                "WHERE id = @Id",
                item, transaction);
        }

        public void SetItemStatus(IDbConnection connection, IDbTransaction transaction, int id, int status, string updatedBy)
        {
            connection.Execute(
                "UPDATE t_dict_item SET status = @status, updated_by = @updatedBy, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND del_flag = 0", new { id, status, updatedBy }, transaction);
        }

        public void SoftDeleteItems(IDbConnection connection, IDbTransaction transaction, IEnumerable<int> ids, string updatedBy)
        {
            var idList = (ids ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (idList.Count == 0) { return; }
            connection.Execute(
                "UPDATE t_dict_item SET del_flag = 1, updated_by = @updatedBy, updated_at = datetime('now','localtime') " +
                "WHERE id IN @ids AND del_flag = 0",
                new { ids = idList, updatedBy }, transaction);
        }
    }
}
