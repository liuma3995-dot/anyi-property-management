using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Domain.Entities;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>轻量字典服务（T4F-1-6：收费项目类别/计价方式/计费周期 读取与自定义新增，CHG-M4-11）。</summary>
    public class DictService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IDictRepository _dict;
        private readonly AuditService _audit;

        public DictService()
            : this(new SqliteConnectionFactory(), new SqlDictRepository(), new AuditService())
        {
        }

        public DictService(IDbConnectionFactory connectionFactory, IDictRepository dict, AuditService audit)
        {
            _connectionFactory = connectionFactory;
            _dict = dict;
            _audit = audit;
        }

        public List<DictItemDto> ListItems(string typeCode)
        {
            if (string.IsNullOrWhiteSpace(typeCode))
            {
                throw ApiException.BadRequest("字典类型不能为空");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _dict.ListItems(connection, typeCode.Trim())
                    .Select(x => new DictItemDto
                    {
                        Id = x.Id,
                        TypeCode = x.TypeCode,
                        ItemCode = x.ItemCode,
                        ItemName = x.ItemName,
                        Remark = x.Remark,
                        Sort = x.Sort,
                        Status = x.Status
                    }).ToList();
            }
        }

        public DictItemDto CreateItem(string typeCode, DictItemCreateRequest request)
        {
            if (string.IsNullOrWhiteSpace(typeCode))
            {
                throw ApiException.BadRequest("字典类型不能为空");
            }
            if (request == null || string.IsNullOrWhiteSpace(request.ItemName))
            {
                throw ApiException.ValidationFailed("自定义项名称不能为空");
            }

            string code = typeCode.Trim();
            string name = request.ItemName.Trim();
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                if (_dict.GetItemByName(connection, code, name) != null)
                {
                    throw ApiException.ValidationFailed("同名字典项已存在");
                }

                var entry = new DictItemEntry
                {
                    TypeCode = code,
                    ItemCode = NextItemCode(connection, code),
                    ItemName = name,
                    Remark = request.Remark == null ? null : request.Remark.Trim(),
                    Sort = 100 + CountItems(connection, code),
                    Status = DictItemStatus.Enabled
                };

                entry.Id = _dict.InsertItem(connection, transaction, entry);
                transaction.Commit();

                _audit.Write("DICT_ITEM_CREATE", "dict_item", entry.Id.ToString(),
                    "新增字典项：" + code + " / " + name);
                return new DictItemDto
                {
                    Id = entry.Id,
                    TypeCode = entry.TypeCode,
                    ItemCode = entry.ItemCode,
                    ItemName = entry.ItemName,
                    Remark = entry.Remark,
                    Sort = entry.Sort,
                    Status = entry.Status
                };
            }
        }

        private static string NextItemCode(IDbConnection connection, string typeCode)
        {
            for (int i = 1; i < 100000; i++)
            {
                string candidate = "c" + i.ToString();
                int count = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_dict_item WHERE type_code = @typeCode AND item_code = @candidate",
                    new { typeCode, candidate });
                if (count == 0) { return candidate; }
            }
            return "c" + DateTime.Now.Ticks.ToString();
        }

        private static int CountItems(IDbConnection connection, string typeCode)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_dict_item WHERE type_code = @typeCode", new { typeCode });
        }
    }
}
