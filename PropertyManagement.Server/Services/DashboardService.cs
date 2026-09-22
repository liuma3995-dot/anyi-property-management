using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

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
        /// <summary>CHG-v1.1.2-55：报表口径收入聚合（与财务报表同源，保证两页数字一致）。</summary>
        private readonly IFinanceRepository _finance;

        public DashboardService()
            : this(new SqliteConnectionFactory(), new TodoService(), new SqlFinanceRepository())
        {
        }

        public DashboardService(IDbConnectionFactory connectionFactory, TodoService todos)
            : this(connectionFactory, todos, new SqlFinanceRepository())
        {
        }

        public DashboardService(IDbConnectionFactory connectionFactory, TodoService todos, IFinanceRepository finance)
        {
            _connectionFactory = connectionFactory;
            _todos = todos;
            _finance = finance;
        }

        /// <summary>仪表盘统计（默认当前月口径）。</summary>
        public DashboardDto GetDashboard()
        {
            return GetDashboard(null);
        }

        /// <summary>
        /// 仪表盘统计（R17：支持按月份查看财务口径；CHG-v1.2.0-26：**再支持按年份**）。
        /// period 为 `yyyy-MM`（按月）或 `yyyy`（按年），空/非法回退当前月：作用「本期应收/已收/收缴率/趋势/收缴概览」；
        /// 待办与运营指标（应急/纠纷/设备/值班）保持实时口径，避免历史周期出现假的"当前状态"。
        /// </summary>
        public DashboardDto GetDashboard(string period)
        {
            DateTime windowStart = ResolvePeriodStart(period, out string periodText, out bool annual);
            DateTime windowEnd = annual ? windowStart.AddYears(1) : windowStart.AddMonths(1);
            // 上一周期：按月比上月，按年比上年
            DateTime previousStart = annual ? windowStart.AddYears(-1) : windowStart.AddMonths(-1);
            string previousPeriod = annual
                ? previousStart.Year.ToString(CultureInfo.InvariantCulture)
                : previousStart.ToString("yyyy-MM");

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                var dto = new DashboardDto
                {
                    RecentReminders = new List<ReminderDto>(),
                    Todos = new List<TodoItemDto>(),
                    TodoCountByKind = new Dictionary<string, int>(),
                    Period = periodText,
                    Annual = annual,
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

                // CHG-v1.1.2-55：应收/已收/收缴率口径校正
                // ①「本期应收」＝**账期归属本期**的账单应收（按账期起始日归期，原实现按到期日归期）；
                // ②「本期已收」＝本期**实收现金净额**，与财务报表「收入合计」同源（原实现只算收款毛额，
                //    未扣退款/调减冲正、未计调增补收 → 与财务报表差 0.35 这类小额差异）；
                // ③ 收缴率＝本期账期账单「已收 / 应收」（分子分母同源），不再出现「已收按收款日 + 应收按到期日」
                //    导致的 >100%（实测曾出现 926.5%）。
                // CHG-v1.2.0-26：同一套口径支持**按年**（strftime('%Y') 归年），月度/年度只是归期粒度不同。
                string periodFormat = annual ? "%Y" : "%Y-%m";
                string periodScope = "FROM t_bill b LEFT JOIN t_billing_cycle cy ON cy.id = b.cycle_id " +
                                     "WHERE b.del_flag = 0 " +
                                     "AND strftime('" + periodFormat + "', COALESCE(cy.start_date, b.due_at)) = @period";
                dto.MonthReceivable = Sum(connection, "SELECT COALESCE(SUM(b.amount), 0) " + periodScope,
                    new { period = periodText });
                dto.MonthCycleReceived = Sum(connection, "SELECT COALESCE(SUM(b.paid_amount), 0) " + periodScope,
                    new { period = periodText });
                dto.MonthReceived = SumReportIncome(connection, windowStart, windowEnd);

                dto.CollectionRate = dto.MonthReceivable > 0
                    ? Math.Round(dto.MonthCycleReceived / dto.MonthReceivable * 100m, 1)
                    : 0m;

                // 环比：与上一周期对比（应收/已收按百分比，逾期户数按户数差）
                decimal previousReceivable = Sum(connection, "SELECT COALESCE(SUM(b.amount), 0) " + periodScope,
                    new { period = previousPeriod });
                decimal previousCycleReceived = Sum(connection, "SELECT COALESCE(SUM(b.paid_amount), 0) " + periodScope,
                    new { period = previousPeriod });
                decimal previousReceived = SumReportIncome(connection, previousStart, windowStart);
                dto.ReceivableTrend = PercentTrend(dto.MonthReceivable, previousReceivable, annual);
                dto.ReceivedTrend = PercentTrend(dto.MonthReceived, previousReceived, annual);
                // 收缴率环比改用「百分点差」（原实现借用了应收环比，语义不对）
                decimal previousRate = previousReceivable > 0
                    ? Math.Round(previousCycleReceived / previousReceivable * 100m, 1)
                    : 0m;
                dto.CollectionRateTrend = RateTrend(dto.CollectionRate, previousRate, annual);

                int currentOverdue = Count(connection,
                    "SELECT COUNT(1) FROM t_bill WHERE del_flag = 0 AND amount > paid_amount AND status IN (1,2) " +
                    "AND strftime('" + periodFormat + "', due_at) = @period", new { period = periodText });
                // BUG 修正：上月逾期户数原样用了本月的 @period（复制粘贴笔误），导致「较上月」恒为 0 户
                int previousOverdue = Count(connection,
                    "SELECT COUNT(1) FROM t_bill WHERE del_flag = 0 AND amount > paid_amount AND status IN (1,2) " +
                    "AND strftime('" + periodFormat + "', due_at) = @period", new { period = previousPeriod });
                int overdueDelta = currentOverdue - previousOverdue;
                dto.OverdueTrend = (annual ? "较上年 " : "较上月 ") +
                    (overdueDelta >= 0 ? "+" : string.Empty) + overdueDelta + " 户";

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

        /// <summary>CHG-v1.1.2-55：本月已收（现金净额）＝财务报表「收入合计」口径。</summary>
        private decimal SumReportIncome(IDbConnection connection, DateTime from, DateTime to)
        {
            return _finance.SumReportIncome(connection, from, to, null);
        }

        /// <summary>收缴率环比（百分点差，如「较上月 +1.2 个百分点」/「较上年 +1.2 个百分点」）。</summary>
        private static string RateTrend(decimal current, decimal previous, bool annual)
        {
            decimal delta = Math.Round(current - previous, 1);
            return (annual ? "较上年 " : "较上月 ") + (delta >= 0 ? "+" : string.Empty) +
                   delta.ToString("0.#", CultureInfo.InvariantCulture) + " 个百分点";
        }

        /// <summary>
        /// 环比文案（CHG-v1.2.0-26：按年口径写「较上年」，按月写「较上月」）；
        /// 上一周期为 0 时以 +100%/0% 兜底，避免除零。
        /// </summary>
        private static string PercentTrend(decimal current, decimal previous, bool annual)
        {
            string label = annual ? "较上年 " : "较上月 ";
            if (previous <= 0m)
            {
                return current > 0m ? label + "+100%" : label + "0%";
            }

            decimal delta = Math.Round((current - previous) / previous * 100m, 1);
            return label + (delta >= 0 ? "+" : string.Empty) + delta.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>
        /// 解析统计周期（CHG-v1.2.0-26）：
        /// `yyyy-MM` → 该月 1 号（按月）；`yyyy`（4 位年）→ 该年 1 月 1 号（**按年**）；空/非法回退当前月。
        /// </summary>
        private static DateTime ResolvePeriodStart(string period, out string periodText, out bool annual)
        {
            var today = DateTime.Today;
            string raw = (period ?? string.Empty).Trim();

            // 按年：仅 4 位数字（如 2026）
            int year;
            if (raw.Length == 4 && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out year) &&
                year >= 1900 && year <= 2999)
            {
                annual = true;
                periodText = year.ToString(CultureInfo.InvariantCulture);
                return new DateTime(year, 1, 1);
            }

            DateTime parsed;
            if (raw.Length > 0 &&
                DateTime.TryParseExact(raw + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsed))
            {
                annual = false;
                periodText = parsed.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                return parsed;
            }

            annual = false;
            DateTime start = new DateTime(today.Year, today.Month, 1);
            periodText = start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            return start;
        }
    }
}
