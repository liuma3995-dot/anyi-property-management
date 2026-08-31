namespace PropertyManagement.Server.Domain.Entities
{
    /// <summary>字典类型（t_dict_type，BR-COM-05 扩展点）。</summary>
    public class DictTypeEntry
    {
        public int Id { get; set; }

        public string TypeCode { get; set; }

        public string TypeName { get; set; }
    }
}
