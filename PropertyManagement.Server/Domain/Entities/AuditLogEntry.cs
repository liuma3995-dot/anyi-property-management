using System;

namespace PropertyManagement.Server.Domain.Entities
{
    /// <summary>审计日志（t_audit_log，只追加，BR-COM-01）。</summary>
    public class AuditLogEntry
    {
        public int Id { get; set; }

        public int? UserId { get; set; }

        public string Action { get; set; }

        public string TargetType { get; set; }

        public string TargetId { get; set; }

        public string Detail { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
