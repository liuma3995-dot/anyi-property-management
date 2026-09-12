using System.Collections.Generic;
using System.Data;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Domain.Entities;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>字典项行（含 PG-COM-01 增强列 display_value/updated_by/updated_at）。</summary>
    public class DictItemRow
    {
        public int Id { get; set; }

        public string TypeCode { get; set; }

        public string ItemCode { get; set; }

        public string ItemName { get; set; }

        public string DisplayValue { get; set; }

        public string Remark { get; set; }

        public int Sort { get; set; }

        public DictItemStatus Status { get; set; }

        public string UpdatedBy { get; set; }

        public System.DateTime? UpdatedAt { get; set; }

        /// <summary>软删标记（0=正常 1=已删除，migration_028）。</summary>
        public int DelFlag { get; set; }
    }

    /// <summary>字典仓储（t_dict_type / t_dict_item，BR-COM-05）。</summary>
    public interface IDictRepository
    {
        IEnumerable<DictTypeEntry> ListTypes(IDbConnection connection);

        /// <summary>PG-COM-01：status=null 返回全部状态（含停用，供管理端恢复启用），0/1 按状态过滤。</summary>
        IEnumerable<DictItemRow> ListItems(IDbConnection connection, string typeCode, int? status);

        /// <summary>T4F-1-5：按名称查找字典项（全状态去重，避免与停用项重名）。</summary>
        DictItemRow GetItemByName(IDbConnection connection, string typeCode, string itemName);

        DictItemRow GetItem(IDbConnection connection, int id);

        /// <summary>T4F-1-5：新增字典项（自定义类别/计价方式/计费周期），写 updated_by/updated_at。</summary>
        int InsertItem(IDbConnection connection, IDbTransaction transaction, DictItemRow item);

        /// <summary>PG-COM-01：更新字典项；status 由服务层保留原值传入（编辑不改变启停状态）。</summary>
        void UpdateItem(IDbConnection connection, IDbTransaction transaction, DictItemRow item);

        void SetItemStatus(IDbConnection connection, IDbTransaction transaction, int id, int status, string updatedBy);

        /// <summary>批量软删字典项（仅服务层校验通过后调用）：del_flag=1 + 写修改人/修改时间。</summary>
        void SoftDeleteItems(IDbConnection connection, IDbTransaction transaction, IEnumerable<int> ids, string updatedBy);
    }
}
