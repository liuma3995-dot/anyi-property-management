using System.Collections.Generic;
using System.Data;
using Dapper;

namespace PropertyManagement.Tests.Infrastructure
{
    /// <summary>
    /// 真实临时库测试基类（L2 轻集成单测）：每个用例前 <see cref="TestDb.Reset"/> 删库重建，
    /// 用例之间零耦合，也不读写生产/演示库。
    /// </summary>
    public abstract class DbTestBase
    {
        protected DbTestBase()
        {
            TestDb.Reset();
        }

        protected static IDbConnection Open()
        {
            return TestDb.OpenConnection();
        }

        protected static int ScalarInt(string sql, object param = null)
        {
            using (IDbConnection connection = Open())
            {
                return connection.ExecuteScalar<int>(sql, param);
            }
        }

        protected static string ScalarText(string sql, object param = null)
        {
            using (IDbConnection connection = Open())
            {
                return connection.ExecuteScalar<string>(sql, param);
            }
        }

        protected static int Execute(string sql, object param = null)
        {
            using (IDbConnection connection = Open())
            {
                return connection.Execute(sql, param);
            }
        }

        protected static List<T> QueryList<T>(string sql, object param = null)
        {
            using (IDbConnection connection = Open())
            {
                return connection.Query<T>(sql, param).AsList();
            }
        }
    }
}
