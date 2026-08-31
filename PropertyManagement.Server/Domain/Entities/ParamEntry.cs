namespace PropertyManagement.Server.Domain.Entities
{
    /// <summary>系统参数（t_param，P-01~P-09）。</summary>
    public class ParamEntry
    {
        public int Id { get; set; }

        public string ParamKey { get; set; }

        public string ParamValue { get; set; }

        public string Remark { get; set; }
    }
}
