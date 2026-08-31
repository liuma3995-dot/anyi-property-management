using System.Collections.Generic;
using System.Data;
using PropertyManagement.Server.Domain.Entities;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>系统参数仓储（t_param，P-01~P-09）。</summary>
    public interface IParamRepository
    {
        ParamEntry GetByKey(IDbConnection connection, string key);

        IEnumerable<ParamEntry> GetAll(IDbConnection connection);

        int GetInt(IDbConnection connection, string key, int defaultValue);
    }
}
