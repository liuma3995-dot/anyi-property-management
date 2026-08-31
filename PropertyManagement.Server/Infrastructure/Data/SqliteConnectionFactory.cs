using System.Data;
using System.Data.SQLite;

namespace PropertyManagement.Server.Infrastructure.Data
{
    /// <summary>SQLite 连接工厂实现：单机单文件数据库。</summary>
    public class SqliteConnectionFactory : IDbConnectionFactory
    {
        public IDbConnection OpenConnection()
        {
            DbConfig.EnsureDirectories();

            var connection = new SQLiteConnection(DbConfig.ConnectionString);
            connection.Open();
            return connection;
        }
    }
}
