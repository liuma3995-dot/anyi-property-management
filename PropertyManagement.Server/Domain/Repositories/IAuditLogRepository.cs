using System.Data;
using PropertyManagement.Server.Domain.Entities;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>审计日志仓储（t_audit_log，只追加）。
    /// CHG-COM-01：支持操作人/角色/模块/结果/IP 留痕（migration_019 增列）。</summary>
    public interface IAuditLogRepository
    {
        void Insert(IDbConnection connection, IDbTransaction transaction, AuditLogEntry entry);

        /// <summary>带上下文留痕的审计写入（BR-COM-01）：user_name 优先冗余列，user_id 可空。</summary>
        void InsertDetailed(
            IDbConnection connection,
            IDbTransaction transaction,
            AuditLogEntry entry,
            string userName,
            string role,
            string module,
            string result,
            string ipAddr);

        int CountAll(IDbConnection connection);
    }
}
