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
    }
}
