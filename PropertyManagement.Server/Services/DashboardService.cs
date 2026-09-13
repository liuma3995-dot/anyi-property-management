using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
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
        private readonly TodoService _todos;

        public DashboardService()
            : this(new SqliteConnectionFactory(), new TodoService())
        {
        }

        public DashboardService(IDbConnectionFactory connectionFactory, TodoService todos)
        {
            _connectionFactory = connectionFactory;
            _todos = todos;
        }

        /// <summary>仪表盘统计（默认当前月口径）。</summary>
        public DashboardDto GetDashboard()
        {
            return GetDashboard(null);
        }

        /// <summary>
        /// 仪表盘统计（R17：支持按月份查看财务口径）。
        /// period 为 yyyy-MM（空/非法回退当前月）：作用「本月应收/已收/收缴率/趋势/收缴概览」；
        /// 待办与运营指标（应急/纠纷/设备/值班）保持实时口径，避免历史月份出现假的"当前状态"。
        /// </summary>
        public DashboardDto GetDashboard(string period)
        {
            DateTime monthStart = ResolveMonthStart(period, out string periodText);
            string previousPeriod = monthStart.AddMonths(-1).ToString("yyyy-MM");

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                var dto = new DashboardDto
                {
                    RecentReminders = new List<ReminderDto>(),
                    Todos = new List<TodoItemDto>(),
                    TodoCountByKind = new Dictionary<string, int>(),
                    Period = periodText,
                    ReceivableTrend = string.Empty,
                    ReceivedTrend = string.Empty,
                    OverdueTrend = string.Empty
                };

                // 待办（顶部铃铛同源）：总数进入「今日共有 N 项待办」，前 5 条用于仪表盘待办面板
                TodoCenterDto todoCenter = _todos.Query(5);
                dto.Todos = todoCenter.Items;
                dto.TodoCountByKind = todoCenter.CountByKind;
                dto.PendingReminders = todoCenter.Total;

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

                // P-04 保养/年检到期（30 天内）：保养=最近保养（或投运）+类型周期；年检=最近年检（或投运）+1 年
                dto.MaintenanceDue = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_device d LEFT JOIN t_device_type t ON t.id = d.type_id " +
                    "WHERE d.del_flag = 0 AND d.status IN (0,1) AND (" +
                    "date(COALESCE((SELECT MAX(m.m_date) FROM t_maintenance_record m WHERE m.device_id = d.id), d.enable_date, d.created_at), " +
                    "'+' || COALESCE(t.maintenance_cycle, 30) || ' day') <= date('now','localtime','+30 day') OR " +
                    "date(COALESCE((SELECT MAX(i.i_date) FROM t_inspection_record i WHERE i.device_id = d.id), d.enable_date, d.created_at), '+1 year') " +
                    "<= date('now','localtime','+30 day'))");

                // 应收/已收/收缴率（R17：按所选月份口径）
                dto.MonthReceivable = Sum(connection,
                    "SELECT COALESCE(SUM(amount), 0) FROM t_bill WHERE del_flag = 0 " +
                    "AND strftime('%Y-%m', due_at) = @period", new { period = periodText });

                dto.MonthReceived = Sum(connection,
                    "SELECT COALESCE(SUM(amount), 0) FROM t_payment WHERE status = 0 " +
                    "AND strftime('%Y-%m', paid_at) = @period", new { period = periodText });

                dto.CollectionRate = dto.MonthReceivable > 0
                    ? Math.Round(dto.MonthReceived / dto.MonthReceivable * 100m, 1)
                    : 0m;

                // 环比：与上一月对比（应收/已收按百分比，逾期户数按户数差）
                decimal previousReceivable = Sum(connection,
                    "SELECT COALESCE(SUM(amount), 0) FROM t_bill WHERE del_flag = 0 " +
                    "AND strftime('%Y-%m', due_at) = @period", new { period = previousPeriod });
                decimal previousReceived = Sum(connection,
                    "SELECT COALESCE(SUM(amount), 0) FROM t_payment WHERE status = 0 " +
                    "AND strftime('%Y-%m', paid_at) = @period", new { period = previousPeriod });
                dto.ReceivableTrend = PercentTrend(dto.MonthReceivable, previousReceivable);
                dto.ReceivedTrend = PercentTrend(dto.MonthReceived, previousReceived);

                int currentOverdue = Count(connection,
                    "SELECT COUNT(1) FROM t_bill WHERE del_flag = 0 AND amount > paid_amount AND status IN (1,2) " +
                    "AND strftime('%Y-%m', due_at) = @period", new { period = periodText });
                int previousOverdue = Count(connection,
                    "SELECT COUNT(1) FROM t_bill WHERE del_flag = 0 AND amount > paid_amount AND status IN (1,2) " +
                    "AND strftime('%Y-%m', due_at) = @period", new { period = previousPeriod });
                int overdueDelta = currentOverdue - previousOverdue;
                dto.OverdueTrend = "较上月 " + (overdueDelta >= 0 ? "+" : string.Empty) + overdueDelta + " 户";

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

        private static int Count(IDbConnection connection, string sql, object param)
        {
            return connection.ExecuteScalar<int>(sql, param);
        }

        private static decimal Sum(IDbConnection connection, string sql)
        {
            return connection.ExecuteScalar<decimal?>(sql) ?? 0m;
        }

        private static decimal Sum(IDbConnection connection, string sql, object param)
        {
            return connection.ExecuteScalar<decimal?>(sql, param) ?? 0m;
        }

        /// <summary>环比文案：上月为 0 时以 +100%/0% 兜底，避免除零。</summary>
        private static string PercentTrend(decimal current, decimal previous)
        {
            if (previous <= 0m)
            {
                return current > 0m ? "较上月 +100%" : "较上月 0%";
            }

            decimal delta = Math.Round((current - previous) / previous * 100m, 1);
            return "较上月 " + (delta >= 0 ? "+" : string.Empty) + delta.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>解析统计月份（yyyy-MM；空/非法回退当前月）。</summary>
        private static DateTime ResolveMonthStart(string period, out string periodText)
        {
            DateTime parsed;
            if (!string.IsNullOrWhiteSpace(period) &&
                DateTime.TryParseExact(period.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsed))
            {
                periodText = parsed.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                return parsed;
            }

            var today = DateTime.Today;
            var start = new DateTime(today.Year, today.Month, 1);
            periodText = start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            return start;
        }
    }
}
