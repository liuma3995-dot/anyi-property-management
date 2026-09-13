using System;
using System.Collections.Generic;
using System.Linq;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Services;
using PropertyManagement.Tests.Infrastructure;
using Xunit;

namespace PropertyManagement.Tests.Services
{
    /// <summary>参数/字典维护服务单元测试（BR-COM-05 字典可扩展、BR-COM-06 软删留痕与批量删除）。</summary>
    public class DictServiceTests : DbTestBase
    {
        private readonly DictService _service = new DictService();

        // ===================== BR-COM-05 字典可扩展，不改代码 =====================

        [Fact]
        public void CreateType_新增字典类型_列表可查()
        {
            var type = _service.CreateType(new DictTypeRequest { TypeCode = "test_ext_type", TypeName = "测试扩展类型" });

            Assert.Equal("test_ext_type", type.TypeCode); // CreateType 返回写入值（Id 由列表回读）
            Assert.Contains(_service.ListTypes(), x => x.TypeCode == "test_ext_type" && x.TypeName == "测试扩展类型");
        }

        [Fact]
        public void CreateItem_自定义字典项_业务下拉可读()
        {
            var type = _service.CreateType(new DictTypeRequest { TypeCode = "test_external_item_type", TypeName = "测试候选类型" });

            var item = _service.CreateItem(type.TypeCode, new DictItemRequest { ItemName = "测试项A" });

            List<DictItemDto> items = _service.ListItems(type.TypeCode);
            Assert.Contains(items, x => x.Id == item.Id && x.ItemName == "测试项A");
            Assert.Equal(DictItemStatus.Enabled, item.Status);
        }

        [Fact]
        public void CreateItem_同名字典项_抛ValidationFailed()
        {
            var type = _service.CreateType(new DictTypeRequest { TypeCode = "test_dup_item_type", TypeName = "重名校验类型" });
            _service.CreateItem(type.TypeCode, new DictItemRequest { ItemName = "重复项" });

            var ex = Assert.Throws<ApiException>(() =>
                _service.CreateItem(type.TypeCode, new DictItemRequest { ItemName = "重复项" }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("同名字典项已存在", ex.Message);
        }

        [Fact]
        public void CreateItem_名称为空_抛ValidationFailed()
        {
            var type = _service.CreateType(new DictTypeRequest { TypeCode = "test_empty_item_type", TypeName = "空名校验类型" });

            var ex = Assert.Throws<ApiException>(() =>
                _service.CreateItem(type.TypeCode, new DictItemRequest { ItemName = "   " }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("字典项名称不能为空", ex.Message);
        }

        [Fact]
        public void SetItemStatus_停用后_默认业务查询不再返回()
        {
            var item = _service.CreateItem("pay_mode", new DictItemRequest { ItemName = "按季（停用测试）" });

            _service.SetItemStatus(item.Id, DictItemStatus.Disabled);

            Assert.DoesNotContain(_service.ListItems("pay_mode"), x => x.Id == item.Id);
            Assert.Contains(_service.ListItems("pay_mode", true), x => x.Id == item.Id);
        }

        // ===================== BR-COM-06 软删除数据保留可查询（R12 批量删除口径） =====================

        [Fact]
        public void BatchDeleteItems_含未停用项_整批拒绝且不落库()
        {
            var enabled = _service.CreateItem("pay_mode", new DictItemRequest { ItemName = "未停用项" });
            var disabled = _service.CreateItem("pay_mode", new DictItemRequest { ItemName = "已停用项" });
            _service.SetItemStatus(disabled.Id, DictItemStatus.Disabled);

            var result = _service.BatchDeleteItems(new DictItemBatchDeleteRequest
            {
                Ids = new List<int> { enabled.Id, disabled.Id }
            });

            Assert.Equal(0, result.Deleted);
            Assert.Single(result.Blocked);
            Assert.Equal(enabled.Id, result.Blocked[0].Id);
            // 整批拒绝：已停用项也未被删除
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_dict_item WHERE id IN (@a, @b) AND del_flag = 1",
                new { a = enabled.Id, b = disabled.Id }));
        }

        [Fact]
        public void BatchDeleteItems_全部已停用_软删且默认查询不返回()
        {
            var first = _service.CreateItem("pay_mode", new DictItemRequest { ItemName = "停用项一" });
            var second = _service.CreateItem("pay_mode", new DictItemRequest { ItemName = "停用项二" });
            _service.SetItemStatus(first.Id, DictItemStatus.Disabled);
            _service.SetItemStatus(second.Id, DictItemStatus.Disabled);

            var result = _service.BatchDeleteItems(new DictItemBatchDeleteRequest
            {
                Ids = new List<int> { first.Id, second.Id }
            });

            Assert.Equal(2, result.Deleted);
            Assert.Empty(result.Blocked);
            Assert.Empty(_service.ListItems("pay_mode", true).Where(x => x.Id == first.Id || x.Id == second.Id));
            // 软删留痕：物理行仍在，del_flag=1，可追溯（BR-COM-06）
            Assert.Equal(2, ScalarInt("SELECT COUNT(1) FROM t_dict_item WHERE id IN (@a, @b) AND del_flag = 1",
                new { a = first.Id, b = second.Id }));
        }

        [Fact]
        public void BatchDeleteItems_空集合_抛ValidationFailed()
        {
            var ex = Assert.Throws<ApiException>(() => _service.BatchDeleteItems(new DictItemBatchDeleteRequest
            {
                Ids = new List<int>()
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("请选择要删除的字典项", ex.Message);
        }

        [Fact]
        public void BatchDeleteItems_含不存在项_抛NotFound()
        {
            var ex = Assert.Throws<ApiException>(() => _service.BatchDeleteItems(new DictItemBatchDeleteRequest
            {
                Ids = new List<int> { 999999 }
            }));

            Assert.Equal(ErrorCode.NotFound, ex.Code);
        }
    }
}
