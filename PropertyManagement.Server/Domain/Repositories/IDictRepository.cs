using System.Collections.Generic;
using System.Data;
using PropertyManagement.Server.Domain.Entities;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>字典仓储（t_dict_type / t_dict_item，BR-COM-05）。</summary>
    public interface IDictRepository
    {
        IEnumerable<DictTypeEntry> ListTypes(IDbConnection connection);

        IEnumerable<DictItemEntry> ListItems(IDbConnection connection, string typeCode);

        /// <summary>T4F-1-5：按名称查找启用字典项（自定义去重）。</summary>
        DictItemEntry GetItemByName(IDbConnection connection, string typeCode, string itemName);

        /// <summary>T4F-1-5：新增字典项（自定义类别/计价方式/计费周期）。</summary>
        int InsertItem(IDbConnection connection, IDbTransaction transaction, DictItemEntry item);
    }
}
