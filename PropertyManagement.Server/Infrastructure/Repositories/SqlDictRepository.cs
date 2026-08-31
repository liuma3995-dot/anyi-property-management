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
                "SELECT id, type_code AS TypeCode, item_code AS ItemCode, item_name AS ItemName, " +
                "sort, status FROM t_dict_item WHERE type_code = @typeCode AND status = 0 ORDER BY sort",
                new { typeCode });
        }
    }
}
