using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Server.Domain.Entities
{
    /// <summary>字典项（t_dict_item，被引用禁删 BR-COM-05）。</summary>
    public class DictItemEntry
    {
        public int Id { get; set; }

        public string TypeCode { get; set; }

        public string ItemCode { get; set; }

        public string ItemName { get; set; }

        public string Remark { get; set; }    // T4F-1-5：附加信息（自定义计价方式单位文本）

        public int Sort { get; set; }

        public DictItemStatus Status { get; set; }
    }
}
