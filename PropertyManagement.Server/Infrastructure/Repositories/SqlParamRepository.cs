using System.Collections.Generic;
using System.Data;
using Dapper;
using PropertyManagement.Server.Domain.Entities;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>t_param 仓储实现（P-01~P-09）。</summary>
    public class SqlParamRepository : IParamRepository
    {
        public ParamEntry GetByKey(IDbConnection connection, string key)
        {
            return connection.QueryFirstOrDefault<ParamEntry>(
                "SELECT id, param_key AS ParamKey, param_value AS ParamValue, remark " +
                "FROM t_param WHERE param_key = @key",
                new { key });
        }

        public IEnumerable<ParamEntry> GetAll(IDbConnection connection)
        {
            return connection.Query<ParamEntry>(
                "SELECT id, param_key AS ParamKey, param_value AS ParamValue, remark " +
                "FROM t_param ORDER BY param_key");
        }

        public int GetInt(IDbConnection connection, string key, int defaultValue)
        {
            ParamEntry entry = GetByKey(connection, key);
            if (entry == null || string.IsNullOrWhiteSpace(entry.ParamValue))
            {
                return defaultValue;
            }

            int value;
            return int.TryParse(entry.ParamValue, out value) ? value : defaultValue;
        }
    }
}
