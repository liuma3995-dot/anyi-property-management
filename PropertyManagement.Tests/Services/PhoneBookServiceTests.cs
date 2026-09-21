using System;
using System.Collections.Generic;
using System.Linq;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.PhoneBook;
using PropertyManagement.Server.Services;
using PropertyManagement.Tests.Infrastructure;
using Xunit;

namespace PropertyManagement.Tests.Services
{
    /// <summary>便民电话簿服务单元测试（BR-TEL-01~04）。</summary>
    public class PhoneBookServiceTests : DbTestBase
    {
        private readonly PhoneBookService _service = new PhoneBookService();

        // ===================== BR-TEL-01 员工通讯录同步 =====================

        [Fact]
        public void SyncEmployees_重复同步_幂等不产生重复条目()
        {
            int categoryId = TestData.PhoneCategory("员工通讯录测试");

            EmployeeSyncResultDto first = _service.SyncEmployees(new EmployeeSyncRequest { CategoryId = categoryId });
            int afterFirst = ScalarInt("SELECT COUNT(1) FROM t_phone_entry WHERE category_id = @id AND del_flag = 0",
                new { id = categoryId });
            EmployeeSyncResultDto second = _service.SyncEmployees(new EmployeeSyncRequest { CategoryId = categoryId });
            int afterSecond = ScalarInt("SELECT COUNT(1) FROM t_phone_entry WHERE category_id = @id AND del_flag = 0",
                new { id = categoryId });

            Assert.True(first.Synced > 0, "首次同步应新建在职员工条目");
            Assert.Equal(0, second.Synced);
            Assert.Equal(afterFirst, afterSecond);
        }

        [Fact]
        public void SaveEntry_手工新增员工条目_抛ValidationFailed()
        {
            int categoryId = TestData.PhoneCategory("员工条目测试");

            var ex = Assert.Throws<ApiException>(() => _service.SaveEntry(0, new PhoneEntryRequest
            {
                CategoryId = categoryId, Name = "冒名员工", Phone = "13800001234", EntryType = PhoneEntryType.Employee
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("同步员工通讯录", ex.Message);
        }

        [Fact]
        public void ResignEmployee_离职联动_员工条目自动停用()
        {
            int categoryId = TestData.PhoneCategory("离职联动测试");
            _service.SyncEmployees(new EmployeeSyncRequest { CategoryId = categoryId });
            int employeeId = TestData.EmployeeIdByName("张强");

            new OrgService().ResignEmployee(employeeId);

            Assert.Equal(1, ScalarInt(
                "SELECT status FROM t_phone_entry WHERE category_id = @cid AND employee_id = @eid AND del_flag = 0",
                new { cid = categoryId, eid = employeeId }));
            Assert.Equal(1, ScalarInt(
                "SELECT disable_source FROM t_phone_entry WHERE category_id = @cid AND employee_id = @eid AND del_flag = 0",
                new { cid = categoryId, eid = employeeId }));
        }

        // ===================== BR-TEL-02 停用条目保留历史但不展示 =====================

        [Fact]
        public void SetEntryStatus_停用_记录保留且业务默认查询不返回()
        {
            int categoryId = TestData.PhoneCategory("停用测试分类");
            var created = _service.SaveEntry(0, new PhoneEntryRequest
            {
                CategoryId = categoryId, Name = "测试条目", Phone = "13811112222", EntryType = PhoneEntryType.Normal
            });

            var disabled = _service.SetEntryStatus(created.Id, new PhoneEntryStatusRequest { Status = PhoneEntryStatus.Disabled });

            Assert.Equal(PhoneEntryStatus.Disabled, disabled.Status);
            Assert.NotNull(_service.GetEntry(created.Id)); // 历史保留，可按 id 追溯
            // 停用为状态位（非删除）：行保留、del_flag 仍为 0
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_phone_entry WHERE id = @id AND del_flag = 0", new { id = created.Id }));
            // 业务列表按启用态查询时不返回
            var enabledList = _service.QueryEntries(new PhoneEntryQueryRequest
            {
                CategoryId = categoryId, Status = PhoneEntryStatus.Enabled, PageSize = 100
            });
            Assert.DoesNotContain(enabledList.Items, x => x.Id == created.Id);
            var disabledList = _service.QueryEntries(new PhoneEntryQueryRequest
            {
                CategoryId = categoryId, Status = PhoneEntryStatus.Disabled, PageSize = 100
            });
            Assert.Contains(disabledList.Items, x => x.Id == created.Id);
        }

        [Fact]
        public void QueryEntries_按启用筛选_停用条目不出现在展示列表()
        {
            int categoryId = TestData.PhoneCategory("启用筛选分类");
            var created = _service.SaveEntry(0, new PhoneEntryRequest
            {
                CategoryId = categoryId, Name = "待停用条目", Phone = "13833334444", EntryType = PhoneEntryType.Normal
            });
            _service.SetEntryStatus(created.Id, new PhoneEntryStatusRequest { Status = PhoneEntryStatus.Disabled });

            var enabled = _service.QueryEntries(new PhoneEntryQueryRequest
            {
                CategoryId = categoryId, Status = PhoneEntryStatus.Enabled, PageSize = 100
            });

            Assert.Equal(0, enabled.Total);
            Assert.DoesNotContain(enabled.Items, x => x.Id == created.Id);
        }

        // ===================== BR-TEL-03 紧急号码置顶禁删 =====================

        [Fact]
        public void SetEntryStatus_停用紧急号码110_抛Conflict()
        {
            int emergencyId = ScalarInt("SELECT id FROM t_phone_entry WHERE phone = '110' AND del_flag = 0 LIMIT 1");
            Assert.True(emergencyId > 0, "seed 应存在 110 紧急号码");

            var ex = Assert.Throws<ApiException>(() => _service.SetEntryStatus(emergencyId,
                new PhoneEntryStatusRequest { Status = PhoneEntryStatus.Disabled }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("紧急电话", ex.Message);
            Assert.Equal(0, ScalarInt("SELECT status FROM t_phone_entry WHERE id = @id", new { id = emergencyId }));
        }

        [Fact]
        public void SetEntryTop_取消紧急号码置顶_抛Conflict()
        {
            int emergencyId = ScalarInt("SELECT id FROM t_phone_entry WHERE phone = '110' AND del_flag = 0 LIMIT 1");

            var ex = Assert.Throws<ApiException>(() => _service.SetEntryTop(emergencyId, false));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("不可取消", ex.Message);
        }

        [Fact]
        public void SaveEntry_紧急类型号码_自动置顶()
        {
            int categoryId = TestData.PhoneCategory("紧急类型测试");

            var created = _service.SaveEntry(0, new PhoneEntryRequest
            {
                CategoryId = categoryId, Name = "消防咨询", Phone = "96119", EntryType = PhoneEntryType.Emergency
            });

            Assert.True(created.IsTop);
            Assert.Equal(PhoneEntryType.Emergency, created.EntryType);
        }

        // ===================== BR-TEL-04 号码格式校验 =====================

        [Fact]
        public void SaveEntry_非法号码_抛ValidationFailed()
        {
            int categoryId = TestData.PhoneCategory("格式测试分类");

            var ex = Assert.Throws<ApiException>(() => _service.SaveEntry(0, new PhoneEntryRequest
            {
                CategoryId = categoryId, Name = "非法号码条目", Phone = "123456", EntryType = PhoneEntryType.Normal
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("号码格式不正确", ex.Message);
        }

        [Fact]
        public void SaveEntry_座机带区号_通过()
        {
            int categoryId = TestData.PhoneCategory("格式测试分类");

            var created = _service.SaveEntry(0, new PhoneEntryRequest
            {
                CategoryId = categoryId, Name = "物业服务中心前台", Phone = "0755-12345678", EntryType = PhoneEntryType.Normal
            });

            Assert.True(created.Id > 0);
        }

        [Fact]
        public void SaveEntry_手机号码_通过且号码重复抛Conflict()
        {
            int categoryId = TestData.PhoneCategory("格式测试分类");
            _service.SaveEntry(0, new PhoneEntryRequest
            {
                CategoryId = categoryId, Name = "重复号码条目", Phone = "13822223333", EntryType = PhoneEntryType.Normal
            });

            var ex = Assert.Throws<ApiException>(() => _service.SaveEntry(0, new PhoneEntryRequest
            {
                CategoryId = categoryId, Name = "另一条目", Phone = "13822223333", EntryType = PhoneEntryType.Normal
            }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("号码已存在", ex.Message);
        }
    }
}
