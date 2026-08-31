using System.Data;
using PropertyManagement.Server.Domain.Entities;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>审计日志仓储（t_audit_log，只追加）。</summary>
    public interface IAuditLogRepository
    {
        void Insert(IDbConnection connection, IDbTransaction transaction, AuditLogEntry entry);

        int CountAll(IDbConnection connection);
    }
}
