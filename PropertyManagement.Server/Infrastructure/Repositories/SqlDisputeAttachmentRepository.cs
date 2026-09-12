using System.Collections.Generic;
using System.Data;
using Dapper;
using PropertyManagement.Contract.Dispute;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>调解协议扫描件仓储实现（SQLite / Dapper，F1）。</summary>
    public class SqlDisputeAttachmentRepository : IDisputeAttachmentRepository
    {
        private const string SelectSql =
            "SELECT id, case_id AS CaseId, file_name AS FileName, stored_path AS StoredPath, " +
            "COALESCE(content_type,'') AS ContentType, COALESCE(size_bytes,0) AS SizeBytes, " +
            "COALESCE(uploaded_by,'') AS UploadedBy, uploaded_at AS UploadedAt " +
            "FROM t_dispute_attachment ";

        public List<DisputeAttachmentDto> List(IDbConnection connection, int caseId)
        {
            return new List<DisputeAttachmentDto>(connection.Query<DisputeAttachmentDto>(
                SelectSql + "WHERE case_id = @caseId AND del_flag = 0 ORDER BY id", new { caseId }));
        }

        public int Count(IDbConnection connection, int caseId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_dispute_attachment WHERE case_id = @caseId AND del_flag = 0", new { caseId });
        }

        public DisputeAttachmentDto Get(IDbConnection connection, int caseId, int id)
        {
            return connection.QueryFirstOrDefault<DisputeAttachmentDto>(
                SelectSql + "WHERE id = @id AND case_id = @caseId AND del_flag = 0", new { id, caseId });
        }

        public int Insert(IDbConnection connection, IDbTransaction transaction, DisputeAttachmentDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_dispute_attachment (case_id, file_name, stored_path, content_type, size_bytes, uploaded_by, uploaded_at) " +
                "VALUES (@CaseId, @FileName, @StoredPath, @ContentType, @SizeBytes, @UploadedBy, @UploadedAt); " +
                "SELECT last_insert_rowid();",
                new { dto.CaseId, dto.FileName, dto.StoredPath, dto.ContentType, dto.SizeBytes, dto.UploadedBy,
                      UploadedAt = dto.UploadedAt == default ? System.DateTime.Now : dto.UploadedAt },
                transaction);
        }

        public void SoftDelete(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_dispute_attachment SET del_flag = 1 WHERE id = @id", new { id }, transaction);
        }
    }
}
