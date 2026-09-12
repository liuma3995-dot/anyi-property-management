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
    /// <summary>
    /// 字典服务（T4F-1-6 + M6 D6-6 T6-6-2，UC-COM-004，BR-COM-05）：
    /// 类型/项读取、新增、编辑、启停；PG-COM-01 增强列（显示值/修改人/修改时间）落库；
    /// 编辑保留原状态（停用项不会被静默重启用）；ListItems 支持状态筛选。
    /// </summary>
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

        // ===================== 查询 =====================

        /// <summary>
        /// 字典项列表。includeDisabled=false 仅启用（业务下拉默认，兼容 /dicts/{code} 老行为）；
        /// includeDisabled=true 返回全部状态（PG-COM-01 管理端含停用可恢复）；
        /// status 显式指定（0 启用 / 1 停用）时优先于 includeDisabled。
        /// </summary>
        public List<DictItemDto> ListItems(string typeCode, bool includeDisabled = false, int? status = null)
        {
            if (string.IsNullOrWhiteSpace(typeCode))
            {
                throw ApiException.BadRequest("字典类型不能为空");
            }

            int? statusFilter = status.HasValue
                ? status
                : (includeDisabled ? (int?)null : (int)DictItemStatus.Enabled);

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return _dict.ListItems(connection, typeCode.Trim(), statusFilter)
                    .Select(MapToDto)
                    .ToList();
            }
        }

        public List<DictTypeDto> ListTypes()
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return connection.Query<DictTypeDto>(
                    "SELECT id, type_code AS TypeCode, type_name AS TypeName FROM t_dict_type ORDER BY id").ToList();
            }
        }

        // ===================== 新增 =====================

        /// <summary>轻量自定义新增（T4F-1-5，/dicts/{code}/items）：显示值默认与名称一致。</summary>
        public DictItemDto CreateItem(string typeCode, DictItemCreateRequest request,
            string operatorName = null, string ip = null)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ItemName))
            {
                throw ApiException.ValidationFailed("自定义项名称不能为空");
            }

            return CreateItem(typeCode, new DictItemRequest
            {
                ItemName = request.ItemName,
                Remark = request.Remark
            }, operatorName, ip);
        }

        /// <summary>完整新增（PG-COM-01，/system/dict-types/{code}/items）：支持显示值/排序。</summary>
        public DictItemDto CreateItem(string typeCode, DictItemRequest request,
            string operatorName = null, string ip = null)
        {
            if (string.IsNullOrWhiteSpace(typeCode))
            {
                throw ApiException.BadRequest("字典类型不能为空");
            }
            if (request == null || string.IsNullOrWhiteSpace(request.ItemName))
            {
                throw ApiException.ValidationFailed("字典项名称不能为空");
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

                var entry = new DictItemRow
                {
                    TypeCode = code,
                    ItemCode = NextItemCode(connection, code),
                    ItemName = name,
                    DisplayValue = string.IsNullOrWhiteSpace(request.DisplayValue) ? name : request.DisplayValue.Trim(),
                    Remark = request.Remark == null ? null : request.Remark.Trim(),
                    Sort = request.Sort > 0 ? request.Sort : 100 + CountItems(connection, code),
                    Status = DictItemStatus.Enabled,
                    UpdatedBy = operatorName
                };

                entry.Id = _dict.InsertItem(connection, transaction, entry);
                transaction.Commit();

                _audit.Write("DICT_ITEM_CREATE", "dict_item", entry.Id.ToString(),
                    "新增字典项：" + code + " / " + name, null, operatorName, ip, null, "成功");
                return MapToDto(entry);
            }
        }

        public DictTypeDto CreateType(DictTypeRequest request, string operatorName = null, string ip = null)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.TypeCode) || string.IsNullOrWhiteSpace(request.TypeName))
                throw ApiException.ValidationFailed("字典类型编码与名称不能为空");
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                connection.Execute(
                    "INSERT INTO t_dict_type (type_code, type_name) VALUES (@TypeCode, @TypeName)",
                    new { request.TypeCode, request.TypeName }, transaction);
                transaction.Commit();
                _audit.Write("DICT_TYPE_CREATE", "dict_type", request.TypeCode,
                    "新增字典类型：" + request.TypeName, null, operatorName, ip, null, "成功");
                return new DictTypeDto { TypeCode = request.TypeCode, TypeName = request.TypeName };
            }
        }

        // ===================== 编辑 / 启停 =====================

        /// <summary>
        /// 编辑字典项（PG-COM-01）：display_value/updated_by/updated_at 落库；
        /// 编辑保留原 Status（不写死 Enabled，停用项编辑后不静默重启用）；状态变更仅走 SetItemStatus。
        /// </summary>
        public DictItemDto UpdateItem(int id, DictItemRequest request, string operatorName = null, string ip = null)
        {
            if (request == null)
            {
                throw ApiException.BadRequest("请求不能为空");
            }

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                DictItemRow entry = _dict.GetItem(connection, id) ?? throw ApiException.NotFound("字典项不存在");

                var updated = new DictItemRow
                {
                    Id = id,
                    TypeCode = entry.TypeCode,
                    ItemCode = entry.ItemCode,
                    ItemName = string.IsNullOrWhiteSpace(request.ItemName) ? entry.ItemName : request.ItemName.Trim(),
                    DisplayValue = string.IsNullOrWhiteSpace(request.DisplayValue) ? entry.DisplayValue : request.DisplayValue.Trim(),
                    Remark = request.Remark ?? entry.Remark,
                    Sort = request.Sort,
                    Status = entry.Status, // 保留原状态（审计报告 PG-COM-01：编辑停用项被强制改回启用的根因）
                    UpdatedBy = operatorName
                };
                _dict.UpdateItem(connection, transaction, updated);
                transaction.Commit();
                _audit.Write("DICT_ITEM_UPDATE", "dict_item", id.ToString(),
                    "修改字典项：" + updated.ItemName + "（状态保持" + StatusText(entry.Status) + "）",
                    null, operatorName, ip, null, "成功");
                return MapToDto(updated);
            }
        }

        public DictItemDto SetItemStatus(int id, DictItemStatus status, string operatorName = null, string ip = null)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                DictItemRow entry = _dict.GetItem(connection, id) ?? throw ApiException.NotFound("字典项不存在");
                _dict.SetItemStatus(connection, transaction, id, (int)status, operatorName);
                transaction.Commit();
                _audit.Write("DICT_ITEM_STATUS", "dict_item", id.ToString(),
                    "字典项「" + entry.ItemName + "」状态：" + StatusText(status), null, operatorName, ip, null, "成功");
                entry.Status = status;
                return MapToDto(entry);
            }
        }

        /// <summary>
        /// 批量删除字典项（PG-COM-01 / R12，软删）：
        /// 仅允许删除**已停用**字典项；命中任一未停用项时**整批拒绝**并返回 blocked 明细（不落库、不写审计）；
        /// 删除口径为 del_flag=1（全仓软删），已删除项不再出现在管理端列表与业务下拉。
        /// </summary>
        public DictItemBatchDeleteResultDto BatchDeleteItems(DictItemBatchDeleteRequest request,
            string operatorName = null, string ip = null)
        {
            var ids = (request == null || request.Ids == null ? new List<int>() : request.Ids)
                .Distinct().Where(x => x > 0).ToList();
            if (ids.Count == 0)
            {
                throw ApiException.ValidationFailed("请选择要删除的字典项");
            }

            List<DictItemRow> rows;
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                rows = new List<DictItemRow>();
                foreach (int id in ids)
                {
                    DictItemRow row = _dict.GetItem(connection, id);
                    if (row == null)
                    {
                        throw ApiException.NotFound("字典项不存在或已删除（id=" + id + "）");
                    }
                    rows.Add(row);
                }

                var blocked = rows.Where(r => r.Status != DictItemStatus.Disabled)
                    .Select(r => new DictItemDeleteBlockedDto
                    {
                        Id = r.Id,
                        ItemName = r.ItemName,
                        Reason = "该字典项未停用，请先【停用】后再删除"
                    }).ToList();
                if (blocked.Count > 0)
                {
                    transaction.Rollback();
                    return new DictItemBatchDeleteResultDto { Deleted = 0, Blocked = blocked };
                }

                _dict.SoftDeleteItems(connection, transaction, ids, operatorName);
                transaction.Commit();
            }

            // 审计在业务事务提交后写入（AuditService 自建连接，事务内写会与 SQLite 写锁冲突）
            foreach (DictItemRow row in rows)
            {
                _audit.Write("DICT_ITEM_DELETE", "dict_item", row.Id.ToString(),
                    "删除字典项（软删）：" + row.TypeCode + " / " + row.ItemName,
                    null, operatorName, ip, "系统设置", "成功");
            }

            return new DictItemBatchDeleteResultDto
            {
                Deleted = rows.Count,
                Blocked = new List<DictItemDeleteBlockedDto>()
            };
        }

        // ===================== 私有辅助 =====================

        private static DictItemDto MapToDto(DictItemRow x)
        {
            return new DictItemDto
            {
                Id = x.Id,
                TypeCode = x.TypeCode,
                ItemCode = x.ItemCode,
                ItemName = x.ItemName,
                DisplayValue = x.DisplayValue,
                Remark = x.Remark,
                Sort = x.Sort,
                Status = x.Status,
                ModifiedBy = x.UpdatedBy,
                ModifiedAt = x.UpdatedAt
            };
        }

        private static string StatusText(DictItemStatus status)
        {
            return status == DictItemStatus.Enabled ? "启用" : "停用";
        }

        /// <summary>新增项编码：{类型前缀}-{序号}（原型 JFFS-01 风格），如 charge_mode → CHARGEMODE-01。</summary>
        private static string NextItemCode(IDbConnection connection, string typeCode)
        {
            string prefix = typeCode.Replace("_", "").ToUpperInvariant();
            if (prefix.Length > 8)
            {
                prefix = prefix.Substring(0, 8);
            }
            if (prefix.Length == 0)
            {
                prefix = "ITEM";
            }

            for (int i = 1; i < 100000; i++)
            {
                string candidate = prefix + "-" + i.ToString("00");
                int count = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_dict_item WHERE type_code = @typeCode AND item_code = @candidate",
                    new { typeCode, candidate });
                if (count == 0) { return candidate; }
            }
            return prefix + "-" + DateTime.Now.Ticks.ToString();
        }

        private static int CountItems(IDbConnection connection, string typeCode)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_dict_item WHERE type_code = @typeCode AND del_flag = 0", new { typeCode });
        }
    }
}
