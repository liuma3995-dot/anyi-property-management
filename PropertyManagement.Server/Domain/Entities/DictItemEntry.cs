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

        public int Sort { get; set; }

        public DictItemStatus Status { get; set; }
    }
}
