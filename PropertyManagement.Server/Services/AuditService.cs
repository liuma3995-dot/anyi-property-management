using System;
using System.Data;
using PropertyManagement.Server.Domain.Entities;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 审计日志服务（BR-COM-01 / DM-07 §三）：敏感操作留痕，只追加。
    /// 供 M4~M6 各业务切片复用；需与业务同事务的场景直接使用 IAuditLogRepository。
    /// </summary>
    public class AuditService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAuditLogRepository _auditLogs;

        public AuditService()
            : this(new SqliteConnectionFactory(), new SqlAuditLogRepository())
        {
        }

        public AuditService(IDbConnectionFactory connectionFactory, IAuditLogRepository auditLogs)
        {
            _connectionFactory = connectionFactory;
            _auditLogs = auditLogs;
        }

        public void Write(string action, string targetType, string targetId, string detail, int? userId = null)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                _auditLogs.Insert(
                    connection,
                    transaction,
                    new AuditLogEntry
                    {
                        UserId = userId,
                        Action = action,
                        TargetType = targetType,
                        TargetId = targetId,
                        Detail = detail
                    });

                transaction.Commit();
            }
        }
    }
}
