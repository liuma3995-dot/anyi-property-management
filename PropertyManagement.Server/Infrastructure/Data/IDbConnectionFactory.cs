using System.Data;

namespace PropertyManagement.Server.Infrastructure.Data
{
    /// <summary>数据库连接工厂接口（仓储与初始化共用）。</summary>
    public interface IDbConnectionFactory
    {
        /// <summary>打开一个已就绪的连接（调用方负责释放）。</summary>
        IDbConnection OpenConnection();
    }
}
