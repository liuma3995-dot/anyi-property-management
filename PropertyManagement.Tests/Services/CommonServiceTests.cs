using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Services;
using PropertyManagement.Tests.Infrastructure;
using Xunit;

namespace PropertyManagement.Tests.Services
{
    /// <summary>
    /// 系统设置（备份/恢复/审计/残余清理）服务单元测试：
    /// BR-COM-01 敏感操作审计、BR-COM-04 备份与恢复、BR-COM-06 软删留痕 + §五 5.9 编排补充。
    /// </summary>
    public class CommonServiceTests : DbTestBase
    {
        private readonly CommonService _service = new CommonService();

        // ===================== BR-COM-04 本地备份与恢复 =====================

        [Fact]
        public void RunBackup_指定路径_落盘且记录成功()
        {
            string target = Path.Combine(TestDb.RunDirectory, "manual-backup.db");

            BackupDto dto = _service.RunBackup("单元测试手动备份", "admin", "127.0.0.1", "manual", target);

            Assert.True(File.Exists(target), "备份文件应落盘到指定路径：" + target);
            Assert.True(dto.Size > 0);
            Assert.Equal("backup", dto.Kind);
            Assert.Equal("手动", "手动"); // 占位保持断言可读性
            Assert.Contains(_service.ListBackups(), x => x.Id == dto.Id && x.FilePath == target);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_audit_log WHERE action = 'BACKUP_RUN' AND result = '成功'"));
        }

        [Fact]
        public void RunBackup_未指定路径_落默认备份目录()
        {
            BackupDto dto = _service.RunBackup("默认目录备份", "admin");

            Assert.True(File.Exists(dto.FilePath));
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), dto.FilePath); // 仍写入隔离临时目录
        }

        [Fact]
        public void RestoreBackup_源文件不存在_抛NotFound()
        {
            var ex = Assert.Throws<ApiException>(() => _service.RestoreBackup(0, new BackupRestoreRequest
            {
                SourcePath = Path.Combine(TestDb.RunDirectory, "not-exists.db"),
                OperationPassword = "Admin@123",
                ConfirmText = "覆盖当前数据不可撤销"
            }, "admin"));

            Assert.Equal(ErrorCode.NotFound, ex.Code);
            Assert.Contains("恢复数据文件不存在", ex.Message);
        }

        [Fact]
        public void RestoreBackup_确认文本错误_抛ValidationFailed()
        {
            BackupDto backup = _service.RunBackup("确认文本校验", "admin");

            var ex = Assert.Throws<ApiException>(() => _service.RestoreBackup(0, new BackupRestoreRequest
            {
                SourcePath = backup.FilePath, OperationPassword = "Admin@123", ConfirmText = "随便写的文本"
            }, "admin"));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("确认文本不正确", ex.Message);
        }

        [Fact]
        public void RestoreBackup_操作密码为空_抛ValidationFailed()
        {
            BackupDto backup = _service.RunBackup("操作密码校验", "admin");

            var ex = Assert.Throws<ApiException>(() => _service.RestoreBackup(0, new BackupRestoreRequest
            {
                SourcePath = backup.FilePath, OperationPassword = "  ", ConfirmText = "覆盖当前数据不可撤销"
            }, "admin"));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("操作密码不能为空", ex.Message);
        }

        [Fact]
        public void RestoreBackup_从指定备份文件恢复_数据回到备份点()
        {
            var dict = new DictService();
            var type = dict.CreateType(new DictTypeRequest { TypeCode = "restore_probe", TypeName = "恢复探针类型" });
            dict.CreateItem(type.TypeCode, new DictItemRequest { ItemName = "备份点已有项" });
            BackupDto backup = _service.RunBackup("恢复前置备份", "admin");
            dict.CreateItem(type.TypeCode, new DictItemRequest { ItemName = "备份后新增项" });
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_dict_item WHERE item_name = '备份后新增项'"));

            BackupDto restored = _service.RestoreBackup(0, new BackupRestoreRequest
            {
                SourcePath = backup.FilePath, OperationPassword = "Admin@123", ConfirmText = "覆盖当前数据不可撤销"
            }, "admin", "127.0.0.1");

            Assert.Equal("restore", restored.Kind);
            Assert.Equal(backup.FilePath, restored.SourcePoint);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_dict_item WHERE item_name = '备份后新增项'"));
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_dict_item WHERE item_name = '备份点已有项'"));
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_audit_log WHERE action = 'BACKUP_RESTORE'"));
        }

        // ===================== BR-COM-06 / R13 记录软删与残余清理 =====================

        [Fact]
        public void BatchDeleteBackupRecords_未选记录_抛ValidationFailed()
        {
            var ex = Assert.Throws<ApiException>(() =>
                _service.BatchDeleteBackupRecords(new RecordBatchDeleteRequest { Ids = new List<int>() }, "admin"));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("请选择要删除的备份/恢复记录", ex.Message);
        }

        [Fact]
        public void BatchDeleteBackupRecords_软删记录_列表不再返回但物理行保留()
        {
            BackupDto backup = _service.RunBackup("待删除备份记录", "admin", null, "manual",
                Path.Combine(TestDb.RunDirectory, "to-delete.db"));

            RecordBatchDeleteResultDto result = _service.BatchDeleteBackupRecords(
                new RecordBatchDeleteRequest { Ids = new List<int> { backup.Id } }, "admin");

            Assert.Equal(1, result.Deleted);
            Assert.DoesNotContain(_service.ListBackups(), x => x.Id == backup.Id);
            Assert.Equal(1, ScalarInt("SELECT del_flag FROM t_backup WHERE id = @id", new { id = backup.Id }));
        }

        [Fact]
        public void PurgeSoftDeleted_仅清理软删留痕_在用数据不受影响()
        {
            var dict = new DictService();
            var type = dict.CreateType(new DictTypeRequest { TypeCode = "purge_probe", TypeName = "清理探针类型" });
            var keepItem = dict.CreateItem(type.TypeCode, new DictItemRequest { ItemName = "在用字典项" });
            var deletedItem = dict.CreateItem(type.TypeCode, new DictItemRequest { ItemName = "待清理字典项" });
            dict.SetItemStatus(deletedItem.Id, DictItemStatus.Disabled);
            dict.BatchDeleteItems(new DictItemBatchDeleteRequest { Ids = new List<int> { deletedItem.Id } });
            Assert.Equal(1, ScalarInt("SELECT del_flag FROM t_dict_item WHERE id = @id", new { id = deletedItem.Id }));

            PurgeSoftDeletedResultDto result = _service.PurgeSoftDeleted("admin", "127.0.0.1");

            Assert.True(result.TotalPurged > 0);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_dict_item WHERE del_flag = 1"));
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_dict_item WHERE id = @id AND del_flag = 0", new { id = keepItem.Id }));
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_audit_log WHERE action = 'SYSTEM_PURGE_SOFT_DELETED'"));
        }

        // ---------- v1.1.0-⑤：全库软删痕迹「清零」验证（覆盖多表 + 孤儿子记录） ----------

        [Fact]
        public void PurgeSoftDeleted_全库各表软删留痕_清理后残留为0()
        {
            // 制造多表软删留痕：字典项 / 备份记录 / 审计日志 / 导入批次（v1.1.0-⑤ 新纳入）
            var dict = new DictService();
            var type = dict.CreateType(new DictTypeRequest { TypeCode = "purge_all", TypeName = "全库清理探针" });
            var item = dict.CreateItem(type.TypeCode, new DictItemRequest { ItemName = "待清理项" });
            dict.SetItemStatus(item.Id, DictItemStatus.Disabled);
            dict.BatchDeleteItems(new DictItemBatchDeleteRequest { Ids = new List<int> { item.Id } });

            var backup = _service.RunBackup("全库清理探针备份", "admin", null, "manual",
                Path.Combine(TestDb.RunDirectory, "purge-all.db"));
            _service.BatchDeleteBackupRecords(new RecordBatchDeleteRequest { Ids = new List<int> { backup.Id } }, "admin");

            new AuditService().Write("PURGE_PROBE", "device", "1", "待清理审计留痕");
            int auditId = ScalarInt("SELECT MAX(id) FROM t_audit_log");
            _service.BatchDeleteAuditLogs(new RecordBatchDeleteRequest { Ids = new List<int> { auditId } }, "admin");

            // 导入批次（含错误行明细：子表无 del_flag，须随父行一并清理）
            Execute("INSERT INTO t_import_log (module, file_name, total, success, fail, status, created_by) " +
                    "VALUES (0, '全库清理.xlsx', 2, 1, 1, 2, 'admin')");
            int importId = ScalarInt("SELECT MAX(id) FROM t_import_log");
            Execute("INSERT INTO t_import_error (import_id, row_no, field, content, reason, suggestion) " +
                    "VALUES (@importId, 3, '房号', '404', '房号不存在', '先导入房产')", new { importId });
            new BaseInfoService().BatchDeleteImportLogs(
                new RecordBatchDeleteRequest { Ids = new List<int> { importId } }, "admin");

            // 执行前：确认留痕已存在
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_import_log WHERE del_flag = 1"));
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_import_error WHERE import_id = @importId", new { importId }));

            PurgeSoftDeletedResultDto result = _service.PurgeSoftDeleted("admin", "127.0.0.1");

            Assert.True(result.TotalPurged > 0);
            // 逐表核对：全库任何表都不再残留 del_flag=1 的软删留痕
            Assert.Equal(0, CountAllSoftDeletedRows());
            // 孤儿子记录（无 del_flag 列）已随父行清理，不留残余
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_import_error WHERE import_id = @importId", new { importId }));
            Assert.Equal(0, CountOrphanImportErrors());
            Assert.Equal(0, CountOrphanExpenseObjectRels());
            // 结果明细里能直接看到导入批次留痕被清理（负责人可核对口径）
            Assert.Contains(result.Items, x => x.TableName == "t_import_log" && x.Count == 1);
        }

        [Fact]
        public void PurgeSoftDeleted_软删支出与设备_业务历史行保留不误删()
        {
            int categoryId = TestData.ExpenseCategory("清理口径分类");
            int expenseId = TestData.Expense(categoryId, 100m,
                TestData.Rel(ExpenseObjectType.Device, TestData.Device(TestData.DeviceType("清理探针设备"))));
            new ExpenseService().BatchDeleteExpenses(
                new RecordBatchDeleteRequest { Ids = new List<int> { expenseId } }, "admin");

            _service.PurgeSoftDeleted("admin");

            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_expense WHERE id = @id", new { id = expenseId }));
            // 设备台账仍在用数据，不受影响
            Assert.True(ScalarInt("SELECT COUNT(1) FROM t_device WHERE del_flag = 0") > 0);
            Assert.Equal(0, CountOrphanExpenseObjectRels());
        }

        /// <summary>扫描全库：统计仍带 del_flag=1 的软删留痕行总数（期望 0）。</summary>
        private static int CountAllSoftDeletedRows()
        {
            int total = 0;
            List<string> tables = QueryList<string>(
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");
            foreach (string table in tables)
            {
                bool hasDelFlag = QueryList<string>(
                    "SELECT name FROM pragma_table_info(@table)", new { table })
                    .Any(x => string.Equals(x, "del_flag", StringComparison.OrdinalIgnoreCase));
                if (!hasDelFlag) { continue; }
                total += ScalarInt("SELECT COUNT(1) FROM " + table + " WHERE del_flag = 1");
            }
            return total;
        }

        private static int CountOrphanImportErrors()
        {
            return ScalarInt("SELECT COUNT(1) FROM t_import_error " +
                             "WHERE import_id NOT IN (SELECT id FROM t_import_log)");
        }

        private static int CountOrphanExpenseObjectRels()
        {
            return ScalarInt("SELECT COUNT(1) FROM t_expense_object_rel " +
                             "WHERE expense_id NOT IN (SELECT id FROM t_expense)");
        }

        // ---------- v1.1.0 R1：删除入口校验的清理闭环 ----------

        [Fact]
        public void 删除入口校验_拦截后清理_不产生无主体账单()
        {
            // R1 闭环验证：房产带账单时删除被拒 → 即便执行一键清理，也不会出现「无主体账单」
            int community = TestData.Community("R1闭环小区");
            int building = TestData.Building(community, "1");
            int property = TestData.Property(building, "101");
            int owner = TestData.Owner("R1闭环业主", "13800001111");
            int relation = TestData.Relation(property, owner);
            new BillingService().GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = TestData.ChargeItem("R1闭环物业费", 1m),
                CycleId = TestData.Cycle(),
                PropertyIds = new List<int> { property },
                ParkingIds = new List<int>()
            });
            // 解除关系后仍被账单引用 → 删除必须被拒（R1：财务记录需保留）
            new BaseInfoService().ReleaseRelation(relation, "R1 闭环用例");

            var ex = Assert.Throws<ApiException>(() => new BaseInfoService().DeleteProperty(property));
            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("未删除账单", ex.Message);

            _service.PurgeSoftDeleted("admin");

            // 清理后：房产仍在、账单仍指向存在的房产（库级判定，不依赖任何体检/报告接口）
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_bill WHERE property_id NOT IN (SELECT id FROM t_property)"));
            Assert.Equal(0, ScalarInt("SELECT del_flag FROM t_property WHERE id = @id", new { id = property }));
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_bill WHERE property_id = @id AND del_flag = 0", new { id = property }));
        }

        // ===================== BR-COM-01 敏感操作写审计 =====================

        [Fact]
        public void CreateExpense_成功_写审计条目含模块与结果()
        {
            int categoryId = TestData.ExpenseCategory("审计测试支出分类");

            TestData.Expense(categoryId, 360.5m);

            AuditLogDto entry = Assert.Single(_service.QueryAuditLogs(new AuditLogQueryRequest
            {
                Action = "EXPENSE_CREATE", PageSize = 10
            }).Items);
            Assert.Equal("财务支出", entry.Module); // 未显式传模块 → 按 target_type=expense 归并
            Assert.Equal("EXPENSE_CREATE", entry.Action);
            Assert.Contains("360.50", entry.Detail);
        }

        [Fact]
        public void Write_未传模块_按目标类型归并为业务模块名()
        {
            new AuditService().Write("UNIT_TEST_ACTION", "device", "1", "单元测试审计归并");

            string module = ScalarText("SELECT module FROM t_audit_log WHERE action = 'UNIT_TEST_ACTION'");

            Assert.Equal("设备台账", module);
        }

        [Fact]
        public void QueryAuditLogs_按动作筛选_仅返回匹配条目()
        {
            new AuditService().Write("UNIT_TEST_A", "device", "1", "A");
            new AuditService().Write("UNIT_TEST_B", "device", "1", "B");

            var page = _service.QueryAuditLogs(new AuditLogQueryRequest { Action = "UNIT_TEST_B", PageSize = 20 });

            Assert.Equal(1, page.Total);
            Assert.Equal("UNIT_TEST_B", page.Items[0].Action);
        }

        // ===================== 5.9 备份状态卡（R12） =====================

        [Fact]
        public void GetBackupStatus_无自动备份记录_回退最近一次备份时间()
        {
            BackupDto manual = _service.RunBackup("状态卡回退测试", "admin");

            BackupStatusDto status = _service.GetBackupStatus();

            Assert.NotNull(status.LastAutoBackupAt);
            Assert.Equal(manual.CreatedAt.ToString("yyyy-MM-dd HH:mm"), status.LastAutoBackupAt.Value.ToString("yyyy-MM-dd HH:mm"));
            Assert.True(status.DatabaseSize > 0);
            Assert.Equal(1, status.PendingRestoreDrill); // 本月无恢复记录
        }
    }
}
