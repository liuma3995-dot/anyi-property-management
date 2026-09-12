using System;
using System.Data;
using Dapper;
using PropertyManagement.Server.Domain.Entities;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>t_audit_log 仓储实现（只追加，时间由数据库默认生成）。
    /// CHG-COM-01：user_name/role/module/result/ip_addr 增列留痕。</summary>
    public class SqlAuditLogRepository : IAuditLogRepository
    {
        public void Insert(IDbConnection connection, IDbTransaction transaction, AuditLogEntry entry)
        {
            InsertDetailed(connection, transaction, entry, null, null, null, null, null);
        }

        public void InsertDetailed(
            IDbConnection connection,
            IDbTransaction transaction,
            AuditLogEntry entry,
            string userName,
            string role,
            string module,
            string result,
            string ipAddr)
        {
            connection.Execute(
                "INSERT INTO t_audit_log (user_id, user_name, role, module, result, ip_addr, action, target_type, target_id, detail) " +
                "VALUES (@UserId, @UserName, @Role, @Module, @Result, @IpAddr, @Action, @TargetType, @TargetId, @Detail)",
                new
                {
                    entry.UserId,
                    entry.Action,
                    entry.TargetType,
                    entry.TargetId,
                    entry.Detail,
                    UserName = userName,
                    Role = role,
                    Module = module,
                    Result = result,
                    IpAddr = ipAddr
                },
                transaction);
        }

        public int CountAll(IDbConnection connection)
        {
            return connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_audit_log");
        }
    }
}
