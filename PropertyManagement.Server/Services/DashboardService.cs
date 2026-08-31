using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Server.Infrastructure.Data;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 仪表盘统计（UC-COM-006，M2-D6 骨架版）：
    /// 基于真实库表返回统计；空库返回 0/空列表；趋势类与周期计算字段随 M4~M6 填充。
    /// </summary>
    public class DashboardService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public DashboardService()
            : this(new SqliteConnectionFactory())
        {
        }

        public DashboardService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public DashboardDto GetDashboard()
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                var dto = new DashboardDto
                {
                    RecentReminders = new List<ReminderDto>(),
                    ReceivableTrend = string.Empty,
                    ReceivedTrend = string.Empty,
                    OverdueTrend = string.Empty
                };

                dto.PendingReminders = Count(connection,
                    "SELECT COUNT(1) FROM t_reminder WHERE status = 0");

                dto.ArrearCount = Count(connection,
                    "SELECT COUNT(1) FROM t_bill WHERE del_flag = 0 AND amount > paid_amount AND status IN (0,1,2)");

                dto.ArrearAmount = Sum(connection,
                    "SELECT COALESCE(SUM(amount - paid_amount), 0) FROM t_bill " +
                    "WHERE del_flag = 0 AND amount > paid_amount AND status IN (0,1,2)");

                dto.HandlingEmergency = Count(connection,
                    "SELECT COUNT(1) FROM t_emergency_event WHERE del_flag = 0 AND status = 1");

                dto.PendingReview = Count(connection,
                    "SELECT COUNT(1) FROM t_attendance WHERE result = 2");

                dto.HandlingDisputes = Count(connection,
                    "SELECT COUNT(1) FROM t_dispute_case WHERE del_flag = 0 AND status = 1");

                dto.RepairingDevices = Count(connection,
                    "SELECT COUNT(1) FROM t_device WHERE del_flag = 0 AND status = 1");

                dto.DutyToday = Count(connection,
                    "SELECT COUNT(1) FROM t_schedule WHERE del_flag = 0 AND status = 1 " +
                    "AND work_date = date('now','localtime')");

                dto.MaintenanceDue = 0; // 保养/年检到期按 P-04 周期计算，M6 填充

                // 本月应收/已收/收缴率（M4 财务切片细化口径）
                dto.MonthReceivable = Sum(connection,
                    "SELECT COALESCE(SUM(amount), 0) FROM t_bill WHERE del_flag = 0 " +
                    "AND strftime('%Y-%m', due_at) = strftime('%Y-%m', 'now', 'localtime')");

                dto.MonthReceived = Sum(connection,
                    "SELECT COALESCE(SUM(amount), 0) FROM t_payment WHERE status = 0 " +
                    "AND strftime('%Y-%m', paid_at) = strftime('%Y-%m', 'now', 'localtime')");

                dto.CollectionRate = dto.MonthReceivable > 0
                    ? Math.Round(dto.MonthReceived / dto.MonthReceivable * 100m, 1)
                    : 0m;

                dto.RecentReminders = connection
                    .Query<ReminderDto>(
                        "SELECT id, type, target_id AS TargetId, due_at AS DueAt, status " +
                        "FROM t_reminder WHERE status = 0 ORDER BY due_at LIMIT 6")
                    .ToList();

                return dto;
            }
        }

        private static int Count(IDbConnection connection, string sql)
        {
            return connection.ExecuteScalar<int>(sql);
        }

        private static decimal Sum(IDbConnection connection, string sql)
        {
            return connection.ExecuteScalar<decimal?>(sql) ?? 0m;
        }
    }
}
