using System.Collections.Generic;
using System.Data;
using PropertyManagement.Contract.Dispute;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>调解协议扫描件仓储（t_dispute_attachment，F1：上传 / 列表 / 下载 / 删除）。</summary>
    public interface IDisputeAttachmentRepository
    {
        List<DisputeAttachmentDto> List(IDbConnection connection, int caseId);
        int Count(IDbConnection connection, int caseId);
        DisputeAttachmentDto Get(IDbConnection connection, int caseId, int id);
        int Insert(IDbConnection connection, IDbTransaction transaction, DisputeAttachmentDto dto);
        void SoftDelete(IDbConnection connection, IDbTransaction transaction, int id);
    }
}
