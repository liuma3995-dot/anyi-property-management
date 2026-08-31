using System;
using System.Data;
using Dapper;
using PropertyManagement.Server.Domain.Entities;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>t_audit_log 仓储实现（只追加，时间由数据库默认生成）。</summary>
    public class SqlAuditLogRepository : IAuditLogRepository
    {
        public void Insert(IDbConnection connection, IDbTransaction transaction, AuditLogEntry entry)
        {
            connection.Execute(
                "INSERT INTO t_audit_log (user_id, action, target_type, target_id, detail) " +
                "VALUES (@UserId, @Action, @TargetType, @TargetId, @Detail)",
                entry,
                transaction);
        }

        public int CountAll(IDbConnection connection)
        {
            return connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_audit_log");
        }
    }
}
