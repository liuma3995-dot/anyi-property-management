using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Equipment;
using PropertyManagement.Server.Infrastructure.Data;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 待办中心（R17，UC-COM-006）：跨模块只读聚合「到期提醒 / 欠费催缴 / 纠纷处理」为统一待办项，
    /// 供顶部铃铛通知中心、仪表盘「待办事项」与状态栏摘要共用（单一数据源，避免多处口径不一致）。
    ///
    /// 说明：
    ///   · 到期类复用 <see cref="EquipmentService.QueryReminders"/>（顺带完成 t_reminder 物化，口径与「到期提醒」页一致）；
    ///   · 欠费/纠纷直接按业务表只读查询；
    ///   · 应急类不产生待办：客户端「应急发起/事件工作台/复盘记录」已按 R4 下线，仅保留统计卡展示。
    /// </summary>
    public class TodoService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly EquipmentService _equipment;

        public TodoService()
            : this(new SqliteConnectionFactory(), new EquipmentService())
        {
        }

        public TodoService(IDbConnectionFactory connectionFactory, EquipmentService equipment)
        {
            _connectionFactory = connectionFactory;
            _equipment = equipment;
        }

        /// <summary>待办总数（状态栏摘要用；不截断列表）。</summary>
        public int CountTodos()
        {
            return BuildItems().Count;
        }

        /// <summary>待办中心：按紧急度排序后截断到 limit 条，同时返回分类计数与总数。</summary>
        public TodoCenterDto Query(int limit = 20)
        {
            List<TodoItemDto> all = BuildItems()
                .OrderBy(x => LevelWeight(x.Level))
                .ThenBy(x => x.DueAt ?? DateTime.MaxValue)
                .ToList();

            return new TodoCenterDto
            {
                Total = all.Count,
                CountByKind = all.GroupBy(x => x.Kind).ToDictionary(g => g.Key, g => g.Count()),
                Items = all.Take(limit <= 0 ? 20 : limit).ToList()
            };
        }

        private List<TodoItemDto> BuildItems()
        {
            var items = new List<TodoItemDto>();
            items.AddRange(MaintenanceTodos());

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                items.AddRange(ArrearTodos(connection));
                items.AddRange(DisputeTodos(connection));
            }

            return items;
        }

        private static int LevelWeight(string level)
        {
            switch (level)
            {
                case "danger": return 0;
                case "warning": return 1;
                case "primary": return 2;
                default: return 3;
            }
        }

        /// <summary>到期提醒（保养/年检/质保/合同）：30 天窗口内未处理项，含逾期。</summary>
        private List<TodoItemDto> MaintenanceTodos()
        {
            var items = new List<TodoItemDto>();
            List<EquipmentReminderDto> reminders = _equipment.QueryReminders(30, "all", 0);

            foreach (EquipmentReminderDto r in reminders)
            {
                int days = r.RemainingDays;
                string level = days < 0 ? "danger" : (days <= 7 ? "warning" : "primary");
                string tag = days < 0 ? "逾期 " + Math.Abs(days) + " 天"
                    : (days == 0 ? "今日到期" : days + " 天后到期");
                string item = string.IsNullOrEmpty(r.ItemText) ? "到期提醒" : r.ItemText;

                items.Add(new TodoItemDto
                {
                    Id = "reminder-" + r.ReminderId,
                    Kind = "maintenance",
                    KindText = "到期提醒",
                    Title = (string.IsNullOrWhiteSpace(r.DeviceName) ? "设备" + r.DeviceId : r.DeviceName) + " " + item,
                    Meta = "设备台账  ·  截止 " + r.DueAt.ToString("MM-dd") +
                           (string.IsNullOrWhiteSpace(r.ResponsibleTeam) ? string.Empty : "  ·  责任班组 " + r.ResponsibleTeam),
                    Tag = tag,
                    Level = level,
                    DueAt = r.DueAt,
                    TargetModule = "equipment",
                    TargetPage = "到期提醒",
                    TargetId = r.ReminderId
                });
            }

            return items;
        }

        /// <summary>欠费催缴：部分缴/逾期且仍有余额的账单。</summary>
        private static List<TodoItemDto> ArrearTodos(IDbConnection connection)
        {
            var rows = connection.Query<ArrearTodoRow>(
                "SELECT b.id AS BillId, CAST(b.amount - b.paid_amount AS REAL) AS Amount, b.due_at AS DueAt, b.status AS Status, " +
                "COALESCE(p.room_no,'') AS RoomNo, COALESCE(bl.building_no,'') AS BuildingNo, " +
                "COALESCE(ps.space_no,'') AS SpaceNo, " +
                "COALESCE((SELECT ow.name FROM t_owner_property_rel rel JOIN t_owner ow ON ow.id = rel.owner_id " +
                "          WHERE rel.property_id = b.property_id AND rel.del_flag = 0 ORDER BY rel.id DESC LIMIT 1),'') AS OwnerName " +
                "FROM t_bill b " +
                "LEFT JOIN t_property p ON p.id = b.property_id " +
                "LEFT JOIN t_building bl ON bl.id = p.building_id " +
                "LEFT JOIN t_parking_space ps ON ps.id = b.parking_id " +
                "WHERE b.del_flag = 0 AND b.status IN (1, 2) AND b.amount > b.paid_amount " +
                "ORDER BY b.due_at LIMIT 50").ToList();

            var items = new List<TodoItemDto>();
            foreach (ArrearTodoRow row in rows)
            {
                int overdueDays = (int)Math.Floor((DateTime.Today - row.DueAt.Date).TotalDays);
                string subject = !string.IsNullOrWhiteSpace(row.RoomNo)
                    ? (string.IsNullOrWhiteSpace(row.BuildingNo) ? string.Empty : row.BuildingNo + " 栋 ") + row.RoomNo + " 室"
                    : (string.IsNullOrWhiteSpace(row.SpaceNo) ? "账单 #" + row.BillId : "车位 " + row.SpaceNo);
                string meta = "财务收费  ·  应收 ¥" + row.Amount.ToString("N2") +
                              (overdueDays > 0 ? "  ·  逾期 " + overdueDays + " 天" : "  ·  今日到期") +
                              (string.IsNullOrWhiteSpace(row.OwnerName) ? string.Empty : "  ·  业主 " + row.OwnerName);

                items.Add(new TodoItemDto
                {
                    Id = "bill-" + row.BillId,
                    Kind = "arrears",
                    KindText = "欠费催缴",
                    Title = subject + " 欠费 ¥" + row.Amount.ToString("N2"),
                    Meta = meta,
                    Tag = overdueDays > 0 ? "逾期" : "待催缴",
                    Level = overdueDays > 0 || row.Status == (int)BillStatus.Overdue ? "danger" : "warning",
                    DueAt = row.DueAt,
                    TargetModule = "finance",
                    TargetPage = "欠费台账",
                    TargetId = row.BillId
                });
            }

            return items;
        }

        /// <summary>纠纷处理：已登记/处理中案件（受理超过 30 天标记超期，口径同 PG-DIS-01）。</summary>
        private static List<TodoItemDto> DisputeTodos(IDbConnection connection)
        {
            var rows = connection.Query<DisputeTodoRow>(
                "SELECT c.id AS CaseId, 'JF-' || strftime('%y%m', c.occur_time) || '-' || c.id AS CaseNo, " +
                "COALESCE(c.detail,'') AS Detail, " +
                "COALESCE(c.location,'') AS Location, c.occur_time AS OccurTime, c.status AS Status, " +
                "COALESCE(t.name,'') AS TypeName " +
                "FROM t_dispute_case c LEFT JOIN t_dispute_type t ON t.id = c.type_id " +
                "WHERE c.del_flag = 0 AND c.status IN (0, 1) ORDER BY c.occur_time LIMIT 50").ToList();

            var items = new List<TodoItemDto>();
            foreach (DisputeTodoRow row in rows)
            {
                int days = (int)Math.Floor((DateTime.Today - row.OccurTime.Date).TotalDays);
                bool overdue = days > 30;
                string summary = string.IsNullOrWhiteSpace(row.Detail) ? row.Location : row.Detail;
                if (summary.Length > 24)
                {
                    summary = summary.Substring(0, 24) + "…";
                }

                items.Add(new TodoItemDto
                {
                    Id = "dispute-" + row.CaseId,
                    Kind = "dispute",
                    KindText = "纠纷处理",
                    Title = (string.IsNullOrWhiteSpace(row.CaseNo) ? "纠纷案件" : row.CaseNo + "  ") + summary,
                    Meta = "纠纷调解  ·  " + (string.IsNullOrWhiteSpace(row.TypeName) ? "未分类" : row.TypeName) +
                           "  ·  发生 " + row.OccurTime.ToString("MM-dd") + "（已 " + Math.Max(0, days) + " 天）",
                    Tag = overdue ? "已超期" : (row.Status == 0 ? "待受理" : "处理中"),
                    Level = overdue ? "danger" : "info",
                    DueAt = row.OccurTime,
                    TargetModule = "dispute",
                    TargetPage = "纠纷列表",
                    TargetId = row.CaseId
                });
            }

            return items;
        }

        private class ArrearTodoRow
        {
            public int BillId { get; set; }
            public decimal Amount { get; set; }
            public DateTime DueAt { get; set; }
            public int Status { get; set; }
            public string RoomNo { get; set; }
            public string BuildingNo { get; set; }
            public string SpaceNo { get; set; }
            public string OwnerName { get; set; }
        }

        private class DisputeTodoRow
        {
            public int CaseId { get; set; }
            public string CaseNo { get; set; }
            public string Detail { get; set; }
            public string Location { get; set; }
            public DateTime OccurTime { get; set; }
            public int Status { get; set; }
            public string TypeName { get; set; }
        }
    }
}
