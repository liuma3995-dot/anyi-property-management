using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using PropertyManagement.Contract.Dispute;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 调解协议扫描件服务（F1，PG-DIS-03）：
    /// 上传（未结案 / 已结案均可，结案不强制上传）、列表、下载、删除。
    /// 规则（负责人已确认）：类型 pdf/jpg/jpeg/png；单文件 ≤20MB；单案件 ≤10 份；
    /// 删除仅管理员，且连物理文件一并删除；上传/删除写审计留痕。
    /// </summary>
    public class DisputeAttachmentService
    {
        private const long MaxSizeBytes = 20L * 1024 * 1024;
        private const int MaxCount = 10;

        private static readonly HashSet<string> AllowedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png" };

        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IDisputeRepository _dispute;
        private readonly IDisputeAttachmentRepository _attachments;
        private readonly AuditService _audit;

        public DisputeAttachmentService()
            : this(new SqliteConnectionFactory(), new SqlDisputeRepository(),
                   new SqlDisputeAttachmentRepository(), new AuditService())
        {
        }

        public DisputeAttachmentService(
            IDbConnectionFactory connectionFactory,
            IDisputeRepository dispute,
            IDisputeAttachmentRepository attachments,
            AuditService audit)
        {
            _connectionFactory = connectionFactory;
            _dispute = dispute;
            _attachments = attachments;
            _audit = audit;
        }

        public List<DisputeAttachmentDto> List(int caseId)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                EnsureCase(connection, caseId);
                return _attachments.List(connection, caseId).Select(Decorate).ToList();
            }
        }

        /// <summary>上传扫描件：校验案件存在 + 类型白名单 + 大小 + 数量上限，物理文件落 data/attachments。</summary>
        public DisputeAttachmentDto Upload(int caseId, string fileName, string contentType, byte[] content, string operatorName)
        {
            if (content == null || content.Length == 0)
            {
                throw ApiException.ValidationFailed("上传文件内容为空");
            }

            string originalName = string.IsNullOrWhiteSpace(fileName) ? "扫描件" : Path.GetFileName(fileName.Trim());
            string extension = Path.GetExtension(originalName);
            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            {
                throw ApiException.ValidationFailed("仅支持 PDF / JPG / JPEG / PNG 格式的扫描件");
            }
            if (content.Length > MaxSizeBytes)
            {
                throw ApiException.ValidationFailed("单个扫描件不得超过 20MB（当前 " + FormatSize(content.Length) + "）");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                DisputeCaseDto kase = EnsureCase(connection, caseId);
                int existing = _attachments.Count(connection, caseId);
                if (existing >= MaxCount)
                {
                    throw ApiException.Conflict("单个案件最多保存 " + MaxCount + " 份扫描件，请先删除无用附件");
                }

                string relative = Path.Combine("dispute", caseId.ToString(), Guid.NewGuid().ToString("N") + extension.ToLowerInvariant());
                string absolute = ToAbsolutePath(relative);
                Directory.CreateDirectory(Path.GetDirectoryName(absolute));
                File.WriteAllBytes(absolute, content);

                var dto = new DisputeAttachmentDto
                {
                    CaseId = caseId,
                    FileName = originalName,
                    StoredPath = relative,
                    ContentType = string.IsNullOrWhiteSpace(contentType) ? GuessContentType(extension) : contentType,
                    SizeBytes = content.Length,
                    UploadedBy = string.IsNullOrWhiteSpace(operatorName) ? "admin" : operatorName.Trim(),
                    UploadedAt = DateTime.Now
                };

                try
                {
                    using (IDbTransaction transaction = connection.BeginTransaction())
                    {
                        dto.Id = _attachments.Insert(connection, transaction, dto);
                        transaction.Commit();
                    }
                }
                catch
                {
                    // 入库失败 → 回收已落盘的物理文件，避免孤儿文件
                    TryDeleteFile(absolute);
                    throw;
                }

                _audit.Write("DISPUTE_ATTACHMENT_UPLOAD", "dispute_case", caseId.ToString(),
                    "上传调解协议扫描件：" + (string.IsNullOrWhiteSpace(kase.CaseNo) ? "JF-" + caseId : kase.CaseNo) +
                    "，文件 " + originalName + "（" + FormatSize(content.Length) + "）");

                return Decorate(dto);
            }
        }

        /// <summary>下载定位：返回物理文件绝对路径（校验归属与存在性，防目录穿越）。</summary>
        public DisputeAttachmentDto Locate(int caseId, int attachmentId)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                DisputeAttachmentDto dto = _attachments.Get(connection, caseId, attachmentId);
                if (dto == null)
                {
                    throw ApiException.NotFound("扫描件不存在或已删除");
                }

                string absolute = ToAbsolutePath(dto.StoredPath);
                if (!File.Exists(absolute))
                {
                    throw ApiException.NotFound("扫描件物理文件已丢失（" + dto.FileName + "）");
                }

                dto.StoredPath = absolute;
                return Decorate(dto);
            }
        }

        /// <summary>删除扫描件：仅管理员（当前内置 admin 账号），软删记录 + 连物理文件一并删除。</summary>
        public void Delete(int caseId, int attachmentId, string operatorName)
        {
            if (!string.Equals((operatorName ?? string.Empty).Trim(), "admin", StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.Forbidden("仅管理员可删除扫描件");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                DisputeAttachmentDto dto = _attachments.Get(connection, caseId, attachmentId);
                if (dto == null)
                {
                    throw ApiException.NotFound("扫描件不存在或已删除");
                }

                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    _attachments.SoftDelete(connection, transaction, attachmentId);
                    transaction.Commit();
                }

                TryDeleteFile(ToAbsolutePath(dto.StoredPath));

                _audit.Write("DISPUTE_ATTACHMENT_DELETE", "dispute_case", caseId.ToString(),
                    "删除调解协议扫描件：" + dto.FileName + "（附件 id " + attachmentId + "，物理文件已删除）");
            }
        }

        // ---------- 内部工具 ----------

        private DisputeCaseDto EnsureCase(IDbConnection connection, int caseId)
        {
            DisputeCaseDto kase = _dispute.GetCase(connection, caseId);
            if (kase == null)
            {
                throw ApiException.NotFound("纠纷案件不存在");
            }
            return kase;
        }

        /// <summary>相对路径 → 绝对路径，并确保仍位于附件根目录内（防目录穿越）。</summary>
        private static string ToAbsolutePath(string relativePath)
        {
            string root = Path.GetFullPath(DbConfig.AttachmentsDirectory);
            string full = Path.GetFullPath(Path.Combine(root, relativePath ?? string.Empty));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.BadRequest("非法的附件存储路径");
            }
            return full;
        }

        private static void TryDeleteFile(string absolutePath)
        {
            try
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }
            }
            catch
            {
                // 物理删除失败不阻断业务（记录已软删，可由运维清理）
            }
        }

        private static DisputeAttachmentDto Decorate(DisputeAttachmentDto dto)
        {
            if (dto == null)
            {
                return null;
            }
            dto.SizeText = FormatSize(dto.SizeBytes);
            dto.UploadedAtText = dto.UploadedAt.ToString("yyyy-MM-dd HH:mm");
            return dto;
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }
            if (bytes < 1024 * 1024)
            {
                return (bytes / 1024.0).ToString("0.0") + " KB";
            }
            return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
        }

        private static string GuessContentType(string extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".pdf": return "application/pdf";
                case ".png": return "image/png";
                default: return "image/jpeg";
            }
        }
    }
}
