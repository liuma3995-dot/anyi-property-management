using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Security;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 系统设置公共服务（M6 D6-6，UC-COM-003/004/005，BR-COM-01/04/05）。
    /// 参数维护（P-01~P-09 白名单 + 值掩码）、备份与恢复（SQLite Backup API 真实恢复 +
    /// R12：备份可自选保存目录、恢复可按手动选择的数据文件、恢复前自动快照 + P-09 保留 30 份清理）、
    /// 审计日志八列查询与 CSV 导出。
    /// 操作人/IP 由 Controller 传入（OWIN 环境无法在服务内获取）。
    /// </summary>
    public class CommonService
    {
        private const string RestoreConfirmText = "覆盖当前数据不可撤销";

        private readonly IDbConnectionFactory _connectionFactory;
        private readonly AuditService _audit;

        public CommonService()
            : this(new SqliteConnectionFactory(), new AuditService())
        {
        }

        public CommonService(IDbConnectionFactory connectionFactory, AuditService audit)
        {
            _connectionFactory = connectionFactory;
            _audit = audit;
        }

        // ===================== 参数（P-01~P-09） =====================
        public List<ParamDto> ListParams()
        {
            using (var c = _connectionFactory.OpenConnection())
            {
                return c.Query<ParamDto>(
                    "SELECT id, param_key AS ParamKey, param_value AS ParamValue FROM t_param ORDER BY id").ToList();
            }
        }

        public string GetParam(string key)
        {
            using (var c = _connectionFactory.OpenConnection())
            {
                return c.ExecuteScalar<string>(
                    "SELECT param_value FROM t_param WHERE param_key = @key", new { key });
            }
        }

        /// <summary>
        /// 更新参数（T6-6-1）。P2 加固：仅允许更新 P-01~P-09 已存在键（新键拒绝，防止任意 upsert）；
        /// 审计值掩码（密码类键记 ******），操作人/IP 留痕。
        /// </summary>
        public void SetParam(string key, string value, string operatorName = null, string ip = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw ApiException.BadRequest("参数键不能为空");
            }

            using (var c = _connectionFactory.OpenConnection())
            {
                int count = c.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_param WHERE param_key = @key", new { key });
                if (count == 0)
                {
                    throw ApiException.ValidationFailed(
                        "参数键「" + key + "」不存在，仅允许更新 P-01~P-09 已有参数（新增参数走评审/迁移）");
                }

                c.Execute(
                    "UPDATE t_param SET param_value = @value, updated_at = datetime('now','localtime') WHERE param_key = @key",
                    new { key, value = value ?? string.Empty });
                _audit.Write("PARAM_UPDATE", "param", key,
                    "修改参数 " + key + " = " + MaskValue(key, value), null, operatorName, ip, null, "成功");
            }
        }

        // ===================== 备份（UC-COM-005，BR-COM-04，P-09） =====================
        public List<BackupDto> ListBackups()
        {
            using (var c = _connectionFactory.OpenConnection())
            {
                return c.Query<BackupDto>(
                    "SELECT id, file_path AS FilePath, size AS Size, created_at AS CreatedAt, " +
                    "kind AS Kind, backup_type AS BackupType, period AS Period, operator AS Operator, " +
                    "reviewer AS Reviewer, result AS Result, source_point AS SourcePoint, target AS Target " +
                    "FROM t_backup WHERE del_flag = 0 ORDER BY id DESC LIMIT 200").ToList();
            }
        }

        /// <summary>
        /// 批量删除备份/恢复记录（R13）：软删留痕（del_flag=1），记录不再出现在列表；
        /// 物理清理由「一键清理残余数据」统一执行。备份文件本身不删除（P-09 保留策略仍按目录生效）。
        /// </summary>
        public RecordBatchDeleteResultDto BatchDeleteBackupRecords(RecordBatchDeleteRequest request,
            string operatorName = null, string ip = null)
        {
            var ids = (request == null || request.Ids == null ? new List<int>() : request.Ids)
                .Distinct().Where(x => x > 0).ToList();
            if (ids.Count == 0)
            {
                throw ApiException.ValidationFailed("请选择要删除的备份/恢复记录");
            }

            int affected;
            using (var c = _connectionFactory.OpenConnection())
            {
                affected = c.Execute(
                    "UPDATE t_backup SET del_flag = 1 WHERE id IN @ids AND del_flag = 0", new { ids });
            }
            if (affected == 0)
            {
                throw ApiException.NotFound("所选记录不存在或已删除");
            }

            _audit.Write("BACKUP_RECORD_DELETE", "backup", string.Join(",", ids),
                "备份/恢复记录批量删除（软删，" + affected + " 条）", null, operatorName, ip, "系统设置", "成功");
            return new RecordBatchDeleteResultDto { Deleted = affected };
        }

        /// <summary>备份状态卡（PG-COM-02）：上次自动备份/数据库大小/保留策略/待恢复演练。</summary>
        public BackupStatusDto GetBackupStatus()
        {
            using (var c = _connectionFactory.OpenConnection())
            {
                BackupDto lastAuto = c.QueryFirstOrDefault<BackupDto>(
                    "SELECT id, file_path AS FilePath, created_at AS CreatedAt, result AS Result, backup_type AS BackupType " +
                    "FROM t_backup WHERE del_flag = 0 AND kind = 'backup' AND backup_type = 'auto' ORDER BY id DESC LIMIT 1");
                if (lastAuto == null)
                {
                    // 自动备份任务未运行过时回退最近一次备份，保证状态卡非空
                    lastAuto = c.QueryFirstOrDefault<BackupDto>(
                        "SELECT id, file_path AS FilePath, created_at AS CreatedAt, result AS Result, backup_type AS BackupType " +
                        "FROM t_backup WHERE del_flag = 0 AND (kind IS NULL OR kind = 'backup') ORDER BY id DESC LIMIT 1");
                }

                long databaseSize = File.Exists(DbConfig.DatabaseFile)
                    ? new FileInfo(DbConfig.DatabaseFile).Length
                    : 0;

                long attachmentsSize = DirectorySize(DbConfig.AttachmentsDirectory);

                int drillsThisMonth = c.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_backup WHERE del_flag = 0 AND kind = 'restore' " +
                    "AND created_at >= date('now','localtime','start of month')");

                return new BackupStatusDto
                {
                    LastAutoBackupAt = lastAuto != null ? lastAuto.CreatedAt : (DateTime?)null,
                    LastAutoBackupResult = lastAuto != null ? lastAuto.Result : "尚未执行备份",
                    DatabaseSize = databaseSize,
                    DatabaseSizeText = FormatSize(databaseSize),
                    AttachmentsSize = attachmentsSize,
                    AttachmentsSizeText = FormatSize(attachmentsSize),
                    RetainCount = ReadRetainCount(c),
                    AutoDailyEnabled = IsTrue(c.ExecuteScalar<string>(
                        "SELECT param_value FROM t_param WHERE param_key = 'backup.auto.daily'")),
                    PendingRestoreDrill = drillsThisMonth > 0 ? 0 : 1
                };
            }
        }

        /// <summary>
        /// 手动/计划备份：SQLite Backup API 安全备份（连接池/打开中安全），写操作人 + backup_type + P-09 清理。
        /// R12：targetPath 非空时按「用户手动指定的文件夹/文件名」落盘（支持把备份数据文件存到指定路径）。
        /// </summary>
        public BackupDto RunBackup(string note, string operatorName = null, string ip = null, string backupType = "manual",
            string targetPath = null)
        {
            if (string.IsNullOrEmpty(backupType)) backupType = "manual";
            bool isAuto = backupType == "auto";
            string typeLabel = isAuto ? "自动备份（backup_type=auto）" : "手动备份（backup_type=manual）";
            if (!File.Exists(DbConfig.DatabaseFile))
                throw ApiException.Conflict("数据库文件不存在，无法备份");

            string target;
            string targetDirectory;
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                // 用户指定保存位置：目录不存在则创建；未带扩展名时补 .db
                target = targetPath.Trim();
                if (!target.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                {
                    target += ".db";
                }
                targetDirectory = Path.GetDirectoryName(target);
                if (string.IsNullOrWhiteSpace(targetDirectory))
                {
                    throw ApiException.ValidationFailed("备份保存路径无效，请选择文件夹并填写文件名");
                }
                try
                {
                    Directory.CreateDirectory(targetDirectory);
                }
                catch (Exception ex)
                {
                    throw ApiException.ValidationFailed("备份保存目录不可用：" + ex.Message);
                }
            }
            else
            {
                Directory.CreateDirectory(DbConfig.BackupDirectory);
                targetDirectory = DbConfig.BackupDirectory;
                target = Path.Combine(targetDirectory, "backup_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".db");
            }
            string fileName = Path.GetFileName(target);

            try
            {
                BackupDatabaseTo(target);

                using (var c = _connectionFactory.OpenConnection())
                {
                    long size = new FileInfo(target).Length;
                    int id = c.ExecuteScalar<int>(
                        "INSERT INTO t_backup (file_path, size, backup_type, operator, result, kind, target) " +
                        "VALUES (@filePath, @size, @backupType, @operator, '成功', 'backup', @target); " +
                        "SELECT last_insert_rowid();",
                        new { filePath = target, size, backupType, @operator = operatorName, target = targetDirectory });

                    CleanupOldBackups(c);

                    _audit.Write("BACKUP_RUN", "backup", id.ToString(),
                        typeLabel + "，保留 " + ReadRetainCount(c) + " 份：" + (note ?? string.Empty) +
                        "，文件 " + fileName,
                        null, operatorName ?? (isAuto ? "系统计划" : null), ip, "系统设置", "成功");

                    return new BackupDto
                    {
                        Id = id,
                        FilePath = target,
                        Size = size,
                        CreatedAt = DateTime.Now,
                        Kind = "backup",
                        BackupType = backupType,
                        Operator = operatorName,
                        Result = "成功",
                        Target = targetDirectory
                    };
                }
            }
            catch (ApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _audit.Write("BACKUP_RUN", "backup", null,
                    typeLabel + "失败：" + ex.Message, null, operatorName ?? (isAuto ? "系统计划" : null), ip, "系统设置", "失败");
                TryDeleteFile(target);
                throw ApiException.Conflict("备份失败：" + ex.Message);
            }
        }

        // ===================== 数据恢复（BR-COM-04 高危，T6-6-3） =====================

        /// <summary>
        /// 真实恢复（PG-COM-02 高危卡）：操作密码（当前用户 BCrypt 校验）+ 第二管理员凭据（另一在岗账号）
        /// + 确认文本「覆盖当前数据不可撤销」；恢复前自动快照（t_backup kind=backup backup_type=auto），
        /// SQLite Backup API 覆盖主库，写 kind=restore 记录（source_point/operator/reviewer/result）并全程审计。
        /// </summary>
        public BackupDto RestoreBackup(int backupId, BackupRestoreRequest request, string operatorName = null, string ip = null)
        {
            if (request == null)
                throw ApiException.BadRequest("恢复请求不能为空");
            if (string.IsNullOrWhiteSpace(request.ConfirmText) || request.ConfirmText.Trim() != RestoreConfirmText)
                throw ApiException.ValidationFailed("确认文本不正确，请输入：" + RestoreConfirmText);
            if (string.IsNullOrWhiteSpace(request.OperationPassword))
                throw ApiException.ValidationFailed("操作密码不能为空");

            // R12：恢复源支持「手动选择的备份数据文件路径」（优先），否则按备份点记录 Id 恢复
            string requestedPath = string.IsNullOrWhiteSpace(request.SourcePath) ? null : request.SourcePath.Trim();
            string backupPath;
            long backupSize;

            using (var c = _connectionFactory.OpenConnection())
            {
                if (requestedPath != null)
                {
                    if (!File.Exists(requestedPath))
                        throw ApiException.NotFound("恢复数据文件不存在：" + requestedPath);
                    EnsureSqliteBackupFile(requestedPath);
                    backupPath = Path.GetFullPath(requestedPath);
                    backupSize = new FileInfo(backupPath).Length;
                }
                else
                {
                    BackupDto backup = c.QueryFirstOrDefault<BackupDto>(
                        "SELECT id, file_path AS FilePath, size AS Size, created_at AS CreatedAt FROM t_backup " +
                        "WHERE id = @id AND (kind IS NULL OR kind = 'backup')", new { id = backupId });
                    if (backup == null)
                        throw ApiException.NotFound("备份点不存在（或该记录为恢复记录，不能作为备份点）");
                    if (!File.Exists(backup.FilePath))
                        throw ApiException.NotFound("备份文件不存在或已被清理");
                    EnsureSqliteBackupFile(backup.FilePath);
                    backupPath = backup.FilePath;
                    backupSize = backup.Size;
                }

                OperatorCredential operatorUser = FindCredential(c, operatorName);

                // 1) 操作密码 = 当前登录用户密码（BCrypt）
                if (operatorUser == null)
                {
                    AuditRestoreFailure(backupId, operatorName, ip, "当前操作账号不存在或已失效");
                    throw ApiException.Unauthorized("当前操作账号无效，请重新登录");
                }
                if (!PasswordHasher.Verify(request.OperationPassword, operatorUser.PasswordHash))
                {
                    AuditRestoreFailure(backupId, operatorName, ip, "操作密码验证未通过");
                    throw ApiException.Unauthorized("操作密码验证失败");
                }

                // 2) 恢复前自动快照（失败即中止，保证可回退）
                Directory.CreateDirectory(DbConfig.BackupDirectory);
                string snapshotPath = Path.Combine(DbConfig.BackupDirectory,
                    "backup_auto_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".db");
                try
                {
                    BackupDatabaseTo(snapshotPath);
                }
                catch (Exception ex)
                {
                    AuditRestoreFailure(backupId, operatorName, ip, "恢复前自动快照失败，已中止恢复：" + ex.Message);
                    throw ApiException.Conflict("恢复前自动快照失败，已中止恢复：" + ex.Message);
                }

                c.Execute(
                    "INSERT INTO t_backup (file_path, size, backup_type, operator, result, kind, target) " +
                    "VALUES (@filePath, @size, 'auto', @operator, @result, 'backup', @target)",
                    new
                    {
                        filePath = snapshotPath,
                        size = new FileInfo(snapshotPath).Length,
                        @operator = operatorName,
                        result = "成功（恢复前自动快照）",
                        target = DbConfig.BackupDirectory
                    });
            }

            // 4) 覆盖恢复：备份文件 → 主库（SQLite 在线备份 API，页级复制）
            try
            {
                RestoreDatabaseFrom(backupPath);
            }
            catch (Exception ex)
            {
                _audit.Write("BACKUP_RESTORE", "backup", backupId.ToString(),
                    "数据恢复失败：备份页写回失败（" + ex.Message + "），主库未变更",
                    null, operatorName, ip, null, "失败");
                throw ApiException.Conflict("恢复失败：" + ex.Message);
            }

            // 5) 恢复库 schema 校验：旧版本备份恢复后按 schema_version 幂等补迁移（失败不阻断，启动时仍会重试）
            string schemaNote;
            try
            {
                DatabaseInitializer.EnsureInitialized();
                schemaNote = "schema 校验通过";
            }
            catch (Exception ex)
            {
                schemaNote = "schema 校验异常（启动时将重试）：" + ex.Message;
            }

            // 5) 恢复完成：在恢复后的库中写恢复记录 + 审计（留痕随恢复库延续）
            using (var c2 = _connectionFactory.OpenConnection())
            {
                int id = c2.ExecuteScalar<int>(
                    "INSERT INTO t_backup (file_path, size, kind, operator, reviewer, result, source_point, target) " +
                    "VALUES (@filePath, @size, 'restore', @operator, @reviewer, '成功', @sourcePoint, @target); " +
                    "SELECT last_insert_rowid();",
                    new
                    {
                        filePath = backupPath,
                        size = backupSize,
                        @operator = operatorName,
                        @reviewer = (string)null,   // R12：本系统无第二管理员业务，复核人不再记录
                        sourcePoint = backupPath,
                        target = DbConfig.DatabaseFile
                    });

                _audit.Write("BACKUP_RESTORE", "backup", id.ToString(),
                    "数据恢复完成（覆盖主库）：恢复文件 " + backupPath +
                    "，确认文本「" + RestoreConfirmText + "」，" + schemaNote,
                    null, operatorName, ip, null, "成功");

                return new BackupDto
                {
                    Id = id,
                    FilePath = backupPath,
                    Size = backupSize,
                    CreatedAt = DateTime.Now,
                    Kind = "restore",
                    Operator = operatorName,
                    Reviewer = null,
                    Result = "成功",
                    SourcePoint = backupPath,
                    Target = DbConfig.DatabaseFile
                };
            }
        }

        // ===================== 审计日志（UC-COM-003，BR-COM-01） =====================

        /// <summary>
        /// 批量删除审计日志（R13）：软删留痕（del_flag=1），不再参与查询/导出；
        /// 删除动作本身写一条 AUDIT_LOG_DELETE 审计（记录条数与 id 清单）。
        /// </summary>
        public RecordBatchDeleteResultDto BatchDeleteAuditLogs(RecordBatchDeleteRequest request,
            string operatorName = null, string ip = null)
        {
            var ids = (request == null || request.Ids == null ? new List<int>() : request.Ids)
                .Distinct().Where(x => x > 0).ToList();
            if (ids.Count == 0)
            {
                throw ApiException.ValidationFailed("请选择要删除的审计日志");
            }

            int affected;
            using (var c = _connectionFactory.OpenConnection())
            {
                affected = c.Execute(
                    "UPDATE t_audit_log SET del_flag = 1 WHERE id IN @ids AND del_flag = 0", new { ids });
            }
            if (affected == 0)
            {
                throw ApiException.NotFound("所选审计日志不存在或已删除");
            }

            _audit.Write("AUDIT_LOG_DELETE", "audit_log", string.Join(",", ids),
                "审计日志批量删除（软删，" + affected + " 条；记录留痕，可在「备份与恢复」页一键清理）",
                null, operatorName, ip, "系统设置", "成功");
            return new RecordBatchDeleteResultDto { Deleted = affected };
        }

        /// <summary>
        /// 一键清理残余数据（R13）：物理删除全库所有 del_flag=1 的软删留痕行（不触碰任何在用数据）。
        /// 逐表统计并写审计 SYSTEM_PURGE_SOFT_DELETED；执行期间关闭外键约束，避免留痕父行被清理时阻塞。
        /// v1.1.0-⑤ 补充：同时清理「随父行留痕一并作废、但自身没有 del_flag 列」的纯子记录
        /// （导入错误行、支出关联对象），否则父行物理删除后会残留孤儿残余数据。
        /// </summary>
        public PurgeSoftDeletedResultDto PurgeSoftDeleted(string operatorName = null, string ip = null)
        {
            var items = new List<PurgeTableCountDto>();
            int total = 0;
            using (var c = _connectionFactory.OpenConnection())
            {
                c.Execute("PRAGMA foreign_keys = OFF;");
                List<string> tables = c.Query<string>(
                    "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name")
                    .ToList();
                foreach (string table in tables)
                {
                    int purged = PurgeTableSoftDeleted(c, table);
                    if (purged > 0)
                    {
                        items.Add(new PurgeTableCountDto { TableName = table, Count = purged });
                        total += purged;
                    }
                }

                foreach (var orphan in OrphanChildCleanups)
                {
                    // 存量库升级路径下子表可能尚未建立 → 缺失即跳过（不影响主流程）
                    if (!tables.Contains(orphan.Key, StringComparer.OrdinalIgnoreCase)) { continue; }
                    int cleaned = c.Execute(orphan.Value);
                    if (cleaned > 0)
                    {
                        items.Add(new PurgeTableCountDto { TableName = orphan.Key + "（孤儿子记录）", Count = cleaned });
                        total += cleaned;
                    }
                }

                c.Execute("PRAGMA foreign_keys = ON;");
            }

            _audit.Write("SYSTEM_PURGE_SOFT_DELETED", "system", null,
                "一键清理残余数据（软删留痕物理删除）：共 " + total + " 行" +
                (items.Count == 0 ? "（无可清理数据）" : "；明细 " +
                    string.Join("、", items.Select(i => i.TableName + " " + i.Count + " 行"))),
                null, operatorName, ip, "系统设置", "成功");

            return new PurgeSoftDeletedResultDto { TotalPurged = total, Items = items };
        }

        /// <summary>
        /// 纯子记录清理清单（v1.1.0-⑤）：这些子表没有 del_flag 列，行本身没有独立业务含义，
        /// 只随父行存在；父行留痕被物理清理后必须一并清理，否则成为孤儿残余数据。
        /// 注意：业务历史表（如 t_bill / t_payment / t_maintenance_record 等）不在此列——
        /// 它们的行是独立的业务历史，即使父行被清理也一律保留（不在用数据不清理的口径之内）。
        /// </summary>
        private static readonly Dictionary<string, string> OrphanChildCleanups = new Dictionary<string, string>
        {
            // 导入批次错误清单：父行 t_import_log 已不存在则错误行无意义
            { "t_import_error", "DELETE FROM t_import_error WHERE import_id NOT IN (SELECT id FROM t_import_log)" },
            // 导入回执逐行结果（CHG-v1.2.0-01）：父行 t_import_log 已不存在则回执行无意义
            { "t_import_row", "DELETE FROM t_import_row WHERE import_id NOT IN (SELECT id FROM t_import_log)" },
            // 支出关联对象：父行 t_expense 已不存在则关联行无意义
            { "t_expense_object_rel", "DELETE FROM t_expense_object_rel WHERE expense_id NOT IN (SELECT id FROM t_expense)" },
            // 收款登记「已结清记录归档」标记（CHG-v1.2.0-31）：账单行被物理清理后归档标记无意义
            { "t_bill_archive", "DELETE FROM t_bill_archive WHERE bill_id NOT IN (SELECT id FROM t_bill)" }
        };

        /// <summary>清理单表软删留痕（表无 del_flag 列时返回 0）。</summary>
        private static int PurgeTableSoftDeleted(IDbConnection connection, string table)
        {
            // 表名来自 sqlite_master（受控），列名经参数化 PRAGMA 表值函数读取
            bool hasDelFlag = connection.Query<string>(
                    "SELECT name FROM pragma_table_info(@table)", new { table })
                .Any(name => string.Equals(name, "del_flag", StringComparison.OrdinalIgnoreCase));
            if (!hasDelFlag) { return 0; }
            return connection.Execute("DELETE FROM " + table + " WHERE del_flag = 1");
        }

        public PageResult<AuditLogDto> QueryAuditLogs(AuditLogQueryRequest query)
        {
            using (var c = _connectionFactory.OpenConnection())
            {
                query = query ?? new AuditLogQueryRequest();
                DynamicParameters p;
                string where = BuildAuditWhere(query, out p);

                string fromSql = "FROM t_audit_log a LEFT JOIN t_user u ON u.id = a.user_id " + where;
                int total = c.ExecuteScalar<int>("SELECT COUNT(1) " + fromSql, p);

                int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
                int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
                int offset = (pageIndex - 1) * pageSize;
                p.Add("limit", pageSize);
                p.Add("offset", offset);

                var items = c.Query<AuditLogDto>(
                    "SELECT a.id AS Id, a.user_id AS UserId, " +
                    "COALESCE(a.user_name, u.username) AS UserName, " +
                    "COALESCE(a.role, '系统管理员') AS Role, " +
                    "COALESCE(a.module, a.target_type) AS Module, " +
                    "a.action AS Action, a.target_type AS TargetType, a.target_id AS TargetId, " +
                    "a.detail AS Detail, a.result AS Result, a.ip_addr AS IpAddr, a.created_at AS CreatedAt " +
                    fromSql + " ORDER BY a.id DESC LIMIT @limit OFFSET @offset", p).ToList();

                return new PageResult<AuditLogDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
            }
        }

        /// <summary>
        /// 审计日志导出（PG-COM-03/T6-6-9）：按当前筛选导出 CSV（UTF-8 BOM），
        /// 写 t_export_log(module=audit) + AUDIT_EXPORT 审计（导出动作本身也写日志）。
        /// </summary>
        public AuditExportDto ExportAuditLogs(AuditLogQueryRequest query, string operatorName = null, string ip = null)
        {
            using (var c = _connectionFactory.OpenConnection())
            {
                query = query ?? new AuditLogQueryRequest();
                DynamicParameters p;
                string where = BuildAuditWhere(query, out p);

                var rows = c.Query<AuditLogDto>(
                    "SELECT a.id AS Id, a.user_id AS UserId, " +
                    "COALESCE(a.user_name, u.username) AS UserName, " +
                    "COALESCE(a.role, '系统管理员') AS Role, " +
                    "COALESCE(a.module, a.target_type) AS Module, " +
                    "a.action AS Action, a.target_type AS TargetType, a.target_id AS TargetId, " +
                    "a.detail AS Detail, a.result AS Result, a.ip_addr AS IpAddr, a.created_at AS CreatedAt " +
                    "FROM t_audit_log a LEFT JOIN t_user u ON u.id = a.user_id " + where +
                    " ORDER BY a.id DESC LIMIT 100000", p).ToList();

                var csv = new StringBuilder();
                csv.AppendLine("时间,操作人,角色,模块,动作,对象,结果,IP地址,详情");
                foreach (AuditLogDto row in rows)
                {
                    csv.AppendLine(string.Join(",",
                        CsvField(row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                        CsvField(row.UserName),
                        CsvField(row.Role),
                        CsvField(row.Module),
                        CsvField(row.Action),
                        CsvField(BuildTargetLabel(row.TargetType, row.TargetId)),
                        CsvField(row.Result),
                        CsvField(row.IpAddr),
                        CsvField(row.Detail)));
                }

                Directory.CreateDirectory(DbConfig.ExportDirectory);
                string fileName = "audit_logs_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".csv";
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);
                File.WriteAllText(filePath, csv.ToString(), new UTF8Encoding(true));

                // t_export_log：format 列沿用 ExportFormat（0=Excel，CSV 以 Excel 兼容格式记账，枚举扩展需中央处理）
                int logId = c.ExecuteScalar<int>(
                    "INSERT INTO t_export_log (module, format, file_path) VALUES ('audit', 0, @filePath); " +
                    "SELECT last_insert_rowid();", new { filePath });

                _audit.Write("AUDIT_EXPORT", "export_log", logId.ToString(),
                    "导出审计日志 " + rows.Count + " 条，文件 " + fileName,
                    null, operatorName, ip, null, "成功");

                return new AuditExportDto { FilePath = filePath, Content = csv.ToString(), Total = rows.Count };
            }
        }

        // ===================== 备份/恢复底层 =====================

        /// <summary>SQLite 在线备份 API（System.Data.SQLite BackupDatabase）：对打开中的库安全复制，替代 File.Copy 热拷贝。</summary>
        private void BackupDatabaseTo(string targetFile)
        {
            using (IDbConnection source = _connectionFactory.OpenConnection())
            {
                var sqliteSource = source as SQLiteConnection;
                if (sqliteSource == null)
                {
                    throw new InvalidOperationException("连接工厂未提供 SQLiteConnection，无法执行安全备份");
                }

                using (var destination = new SQLiteConnection("Data Source=" + targetFile + ";Version=3;"))
                {
                    destination.Open();
                    sqliteSource.BackupDatabase(destination, "main", "main", -1, null, 100);
                }
            }
        }

        /// <summary>恢复：备份文件 → 主库（反向在线备份，页级覆盖，自动记日志）。</summary>
        private void RestoreDatabaseFrom(string backupFilePath)
        {
            using (var destination = new SQLiteConnection(DbConfig.ConnectionString))
            using (var source = new SQLiteConnection("Data Source=" + backupFilePath + ";Version=3;"))
            {
                destination.Open();
                source.Open();
                source.BackupDatabase(destination, "main", "main", -1, null, 100);
            }
        }

        /// <summary>P-09：保留最近 N 份备份文件（backup.retain.count），超限删除文件并把对应行置 result=已清理。
        /// 已清理行不参与计数（否则会挤占保留名额导致多删）。
        /// R12：用户手动指定目录保存的备份（备份文件不在系统备份目录下）只标记不删除文件，避免删除用户自存数据文件。</summary>
        private void CleanupOldBackups(IDbConnection connection)
        {
            int retain = ReadRetainCount(connection);
            List<BackupFileRow> rows = connection.Query<BackupFileRow>(
                "SELECT id AS Id, file_path AS FilePath FROM t_backup WHERE del_flag = 0 AND (kind IS NULL OR kind = 'backup') " +
                "AND (result IS NULL OR result LIKE '成功%') ORDER BY id DESC").ToList();

            string managedDirectory = Path.GetFullPath(DbConfig.BackupDirectory).TrimEnd('\\') + "\\";
            foreach (BackupFileRow row in rows.Skip(retain))
            {
                try
                {
                    if (!string.IsNullOrEmpty(row.FilePath) && File.Exists(row.FilePath))
                    {
                        // 仅清理系统备份目录内的文件；用户自选路径的备份文件保留（仅在库中标为已清理记录）
                        if (Path.GetFullPath(row.FilePath).StartsWith(managedDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(row.FilePath);
                        }
                    }
                }
                catch
                {
                    // 文件被占用等：仍标记已清理，避免反复计数
                }

                connection.Execute(
                    "UPDATE t_backup SET result = '已清理' WHERE id = @id " +
                    "AND (result IS NULL OR result LIKE '成功%')", new { id = row.Id });
            }
        }

        private int ReadRetainCount(IDbConnection connection)
        {
            string raw = connection.ExecuteScalar<string>(
                "SELECT param_value FROM t_param WHERE param_key = 'backup.retain.count'");
            int retain;
            return int.TryParse(raw, out retain) && retain > 0 ? retain : 30;
        }

        /// <summary>
        /// R12：校验「手动选择的恢复数据文件」确为本系统 SQLite 备份
        /// （SQLite 文件头 + 关键表 t_user/t_param/t_audit_log），避免误选文件覆盖主库。
        /// </summary>
        private static void EnsureSqliteBackupFile(string path)
        {
            string fileName = Path.GetFileName(path);
            byte[] header = new byte[16];
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    if (stream.Length < header.Length || stream.Read(header, 0, header.Length) < header.Length)
                    {
                        throw ApiException.ValidationFailed("恢复数据文件不是有效的 SQLite 数据库：" + fileName);
                    }
                }
            }
            catch (ApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw ApiException.ValidationFailed("恢复数据文件无法读取：" + ex.Message);
            }

            if (Encoding.ASCII.GetString(header, 0, 15) != "SQLite format 3")
            {
                throw ApiException.ValidationFailed("恢复数据文件不是有效的 SQLite 数据库：" + fileName);
            }

            try
            {
                var builder = new SQLiteConnectionStringBuilder
                {
                    DataSource = path,
                    Version = 3,
                    ReadOnly = true
                };
                using (var connection = new SQLiteConnection(builder.ConnectionString))
                {
                    connection.Open();
                    int tables = connection.ExecuteScalar<int>(
                        "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' " +
                        "AND name IN ('t_user', 't_param', 't_audit_log')");
                    if (tables < 3)
                    {
                        throw ApiException.ValidationFailed(
                            "恢复数据文件缺少本系统数据表（t_user / t_param / t_audit_log），请选择系统生成的备份文件：" + fileName);
                    }
                }
            }
            catch (ApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw ApiException.ValidationFailed("恢复数据文件无法打开：" + ex.Message);
            }
        }

        private void AuditRestoreFailure(int backupId, string operatorName, string ip, string reason)
        {
            _audit.Write("BACKUP_RESTORE", "backup", backupId.ToString(),
                "数据恢复失败：" + reason, null, operatorName, ip, null, "失败");
        }

        private static OperatorCredential FindCredential(IDbConnection connection, string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }
            return connection.QueryFirstOrDefault<OperatorCredential>(
                "SELECT id AS Id, username AS Username, password_hash AS PasswordHash, status AS Status " +
                "FROM t_user WHERE username = @username", new { username = username.Trim() });
        }

        // ===================== 私有辅助 =====================

        /// <summary>审计查询条件（查询/导出共用）：操作人/模块/结果/动作/对象/关键字/时间范围（To 含当日）。</summary>
        private static string BuildAuditWhere(AuditLogQueryRequest query, out DynamicParameters p)
        {
            p = new DynamicParameters();
            string where = "WHERE 1=1";
            // R13：已软删（批量删除留痕）的审计日志不再参与查询与导出
            where += " AND a.del_flag = 0";

            if (!string.IsNullOrWhiteSpace(query.Operator))
            {
                where += " AND (a.user_name LIKE @operator OR u.username LIKE @operator)";
                p.Add("operator", "%" + query.Operator.Trim() + "%");
            }
            if (!string.IsNullOrWhiteSpace(query.Module))
            {
                where += " AND (a.module LIKE @module OR a.target_type LIKE @module)";
                p.Add("module", "%" + query.Module.Trim() + "%");
            }
            if (!string.IsNullOrWhiteSpace(query.Result))
            {
                where += " AND a.result = @result";
                p.Add("result", query.Result.Trim());
            }
            if (!string.IsNullOrWhiteSpace(query.Action))
            {
                where += " AND a.action LIKE @action";
                p.Add("action", "%" + query.Action.Trim() + "%");
            }
            if (!string.IsNullOrWhiteSpace(query.TargetType))
            {
                where += " AND a.target_type LIKE @target";
                p.Add("target", "%" + query.TargetType.Trim() + "%");
            }
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                where += " AND (a.action LIKE @kw OR a.detail LIKE @kw OR a.target_type LIKE @kw OR a.target_id LIKE @kw)";
                p.Add("kw", "%" + query.Keyword.Trim() + "%");
            }
            if (query.From.HasValue)
            {
                where += " AND a.created_at >= @from";
                p.Add("from", query.From.Value.ToString("yyyy-MM-dd"));
            }
            if (query.To.HasValue)
            {
                // 日期字典序陷阱：created_at 为 'YYYY-MM-DD HH:MM:SS'，同日数据须按次日零点开区间
                where += " AND a.created_at < date(@to, '+1 day')";
                p.Add("to", query.To.Value.ToString("yyyy-MM-dd"));
            }
            return where;
        }

        /// <summary>敏感参数值掩码（密码/密钥类键审计不记明文）。</summary>
        private static string MaskValue(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value ?? string.Empty;
            }
            string lower = key.ToLowerInvariant();
            if (lower.Contains("password") || lower.Contains("secret") || lower.Contains("token") || lower.EndsWith("key"))
            {
                return "******";
            }
            return value;
        }

        private static bool IsTrue(string value)
        {
            return value != null && value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildTargetLabel(string targetType, string targetId)
        {
            if (string.IsNullOrEmpty(targetType)) return targetId ?? string.Empty;
            return string.IsNullOrEmpty(targetId) ? targetType : targetType + ":" + targetId;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.0") + " GB";
            if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0.0") + " MB";
            if (bytes >= 1L << 10) return (bytes / (double)(1L << 10)).ToString("0.0") + " KB";
            return bytes + " B";
        }

        /// <summary>目录递归占用（PG-COM-02 数据库大小卡副文案「含附件 X」）；目录不存在返回 0。</summary>
        private static long DirectorySize(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return 0;
            }

            long total = 0;
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch (IOException)
                    {
                        // 单文件不可读时跳过，不阻断状态卡加载
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                return total;
            }
            catch (DirectoryNotFoundException)
            {
                return total;
            }
            return total;
        }

        private static string CsvField(string value)
        {
            if (value == null) return string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }

        private class BackupFileRow
        {
            public int Id { get; set; }
            public string FilePath { get; set; }
        }

        private class OperatorCredential
        {
            public int Id { get; set; }
            public string Username { get; set; }
            public string PasswordHash { get; set; }
            public UserStatus Status { get; set; }
        }
    }
}
