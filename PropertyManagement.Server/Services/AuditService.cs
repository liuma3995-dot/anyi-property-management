using System;
using System.Collections.Generic;
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
    /// CHG-COM-01（PG-COM-03 八列）：Write 增加可选 userName/ip/module/result 参数，
    /// 默认 null 不破坏既有调用；HTTP 上下文无法在服务内获取，由各 Controller 传入。
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

        /// <summary>
        /// 写审计（BR-COM-01）。userId 沿用历史第 5 参；userName/ip/module/result 为可选增强，
        /// 操作人请由 Controller 从 OWIN 环境读取后传入（AuthMiddleware.UsernameEnvKey / RemoteIpAddress）。
        /// </summary>
        public void Write(
            string action,
            string targetType,
            string targetId,
            string detail,
            int? userId = null,
            string userName = null,
            string ip = null,
            string module = null,
            string result = null)
        {
            string resolvedModule = ResolveModule(module, targetType);
            string role = string.IsNullOrWhiteSpace(userName) ? null : "系统管理员";

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                _auditLogs.InsertDetailed(
                    connection,
                    transaction,
                    new AuditLogEntry
                    {
                        UserId = userId,
                        Action = action,
                        TargetType = targetType,
                        TargetId = targetId,
                        Detail = detail
                    },
                    userName,
                    role,
                    resolvedModule,
                    result,
                    ip);

                transaction.Commit();
            }
        }

        /// <summary>模块归并：显式传入优先；否则按 target_type 归并，兜底返回 target_type 原文。</summary>
        private static string ResolveModule(string module, string targetType)
        {
            if (!string.IsNullOrWhiteSpace(module))
            {
                return module.Trim();
            }

            if (string.IsNullOrWhiteSpace(targetType))
            {
                return null;
            }

            string key = targetType.Trim().ToLowerInvariant();

            if (key == "param" || key == "backup" || key.StartsWith("dict") || key == "export_log")
            {
                return "系统设置";
            }
            if (key == "user")
            {
                return "系统认证";
            }
            if (key == "report_log")
            {
                return "报表导出";
            }
            if (key == "charge_item" || key == "bill" || key == "bill_generate_log" || key == "receipt" || key == "payment" || key == "refund")
            {
                return "财务收费";
            }
            if (key == "expense" || key == "expense_category")
            {
                return "财务支出";
            }
            if (key == "owner" || key == "property" || key == "parking")
            {
                return "基础信息";
            }
            if (key == "employee" || key == "department" || key == "position" || key == "schedule" || key == "attendance")
            {
                return "人员组织";
            }
            if (key == "emergency")
            {
                return "应急处置";
            }
            if (key == "dispute")
            {
                return "纠纷调解";
            }
            if (key == "device" || key == "vendor")
            {
                return "设备台账";
            }
            if (key == "phone_entry" || key == "phone_category")
            {
                return "便民电话簿";
            }
            return targetType.Trim();
        }
    }
}
