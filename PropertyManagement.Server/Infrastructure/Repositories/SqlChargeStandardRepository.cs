using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>收费项目「价目表 + 计量变量」SQLite 实现（CHG-v1.1.2-26，Dapper）。</summary>
    public class SqlChargeStandardRepository : IChargeStandardRepository
    {
        private const string StandardColumns =
            "s.id, s.name, s.category, s.remark, s.status, s.del_flag AS DelFlag, " +
            "(SELECT COUNT(1) FROM t_charge_item i WHERE i.standard_id = s.id AND i.del_flag = 0) AS ItemCount";

        private const string SpecColumns =
            "id, standard_id AS StandardId, spec_name AS SpecName, match_usage AS MatchUsage, " +
            "match_status AS MatchStatus, match_space_type AS MatchSpaceType, match_building AS MatchBuilding, " +
            "is_fallback AS IsFallback, unit_price AS UnitPrice, formula, formula_vars AS FormulaVarsJson, " +
            "price_unit AS PriceUnit, cycle_name AS CycleName, effective_from AS EffectiveFrom, remark, status";

        // ---------- 收费标准 ----------
        public List<ChargeStandardDto> ListStandards(IDbConnection connection, string keyword, string category, bool includeDisabled)
        {
            string sql = "SELECT " + StandardColumns + " FROM t_charge_standard s WHERE s.del_flag = 0";
            var parameters = new Dictionary<string, object>();
            if (!includeDisabled) { sql += " AND s.status = 0"; }
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                sql += " AND (s.name LIKE @kw OR s.category LIKE @kw)";
                parameters["kw"] = "%" + keyword.Trim() + "%";
            }
            if (!string.IsNullOrWhiteSpace(category))
            {
                sql += " AND s.category = @category";
                parameters["category"] = category.Trim();
            }
            sql += " ORDER BY s.id";

            List<ChargeStandardDto> list = connection.Query<ChargeStandardDto>(sql, parameters).ToList();
            foreach (ChargeStandardDto dto in list)
            {
                // FIX-v1.1.2-02（负责人反馈「新增 3 条规格只剩 2 条」）：
                // 价目表列表必须返回**含停用**的规格 —— 改价后旧规格转为停用，
                // 若此处只取启用项，旧规格就从界面上「消失」，用户无法确认改价是否留痕。
                // 出账匹配仍只使用启用规格（ChargeSpecMatcher 内按 status == 0 过滤），计费口径不变。
                dto.Specs = ListSpecs(connection, dto.Id, true);
                dto.Variables = ListStandardVariables(connection, dto.Id);
            }
            return list;
        }

        public ChargeStandardDto GetStandard(IDbConnection connection, int id)
        {
            ChargeStandardDto dto = connection.QueryFirstOrDefault<ChargeStandardDto>(
                "SELECT " + StandardColumns + " FROM t_charge_standard s WHERE s.id = @id AND s.del_flag = 0", new { id });
            if (dto == null) { return null; }
            dto.Specs = ListSpecs(connection, id, true);
            dto.Variables = ListStandardVariables(connection, id);
            return dto;
        }

        public int InsertStandard(IDbConnection connection, IDbTransaction transaction, ChargeStandardDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_charge_standard (name, category, remark, status, del_flag) " +
                "VALUES (@Name, @Category, @Remark, @Status, 0); SELECT last_insert_rowid();",
                new { dto.Name, dto.Category, dto.Remark, dto.Status }, transaction);
        }

        public void UpdateStandard(IDbConnection connection, IDbTransaction transaction, ChargeStandardDto dto)
        {
            connection.Execute(
                "UPDATE t_charge_standard SET name = @Name, category = @Category, remark = @Remark, status = @Status, " +
                "updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.Name, dto.Category, dto.Remark, dto.Status }, transaction);
        }

        public void SoftDeleteStandard(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_charge_standard SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                new { id }, transaction);
            connection.Execute("UPDATE t_charge_standard_spec SET del_flag = 1 WHERE standard_id = @id", new { id }, transaction);
        }

        public int CountStandardItems(IDbConnection connection, int standardId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_charge_item WHERE standard_id = @standardId AND del_flag = 0", new { standardId });
        }

        // ---------- 规格明细 ----------
        public List<ChargeStandardSpecDto> ListSpecs(IDbConnection connection, int standardId, bool includeDisabled)
        {
            string sql = "SELECT " + SpecColumns + " FROM t_charge_standard_spec WHERE standard_id = @standardId AND del_flag = 0";
            if (!includeDisabled) { sql += " AND status = 0"; }
            sql += " ORDER BY is_fallback, id";
            return connection.Query<ChargeStandardSpecDto>(sql, new { standardId })
                .Select(HydrateVars).ToList();
        }

        public ChargeStandardSpecDto GetSpec(IDbConnection connection, int id)
        {
            ChargeStandardSpecDto dto = connection.QueryFirstOrDefault<ChargeStandardSpecDto>(
                "SELECT " + SpecColumns + " FROM t_charge_standard_spec WHERE id = @id AND del_flag = 0", new { id });
            return dto == null ? null : HydrateVars(dto);
        }

        public int InsertSpec(IDbConnection connection, IDbTransaction transaction, ChargeStandardSpecDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_charge_standard_spec (standard_id, spec_name, match_usage, match_status, match_space_type, " +
                "match_building, is_fallback, unit_price, formula, formula_vars, price_unit, cycle_name, effective_from, " +
                "remark, status, del_flag) VALUES (@StandardId, @SpecName, @MatchUsage, @MatchStatus, @MatchSpaceType, " +
                "@MatchBuilding, @IsFallbackInt, @UnitPrice, @Formula, @FormulaVarsJson, @PriceUnit, @CycleName, @EffectiveFrom, " +
                "@Remark, @Status, 0); SELECT last_insert_rowid();",
                new
                {
                    dto.StandardId, dto.SpecName, dto.MatchUsage, dto.MatchStatus, dto.MatchSpaceType, dto.MatchBuilding,
                    IsFallbackInt = dto.IsFallback ? 1 : 0, dto.UnitPrice, dto.Formula, dto.PriceUnit, dto.CycleName,
                    dto.EffectiveFrom, dto.Remark, dto.Status,
                    FormulaVarsJson = string.IsNullOrWhiteSpace(dto.FormulaVarsJson) ? null : dto.FormulaVarsJson
                }, transaction);
        }

        public void UpdateSpec(IDbConnection connection, IDbTransaction transaction, ChargeStandardSpecDto dto)
        {
            connection.Execute(
                "UPDATE t_charge_standard_spec SET spec_name = @SpecName, match_usage = @MatchUsage, match_status = @MatchStatus, " +
                "match_space_type = @MatchSpaceType, match_building = @MatchBuilding, is_fallback = @IsFallbackInt, " +
                "unit_price = @UnitPrice, formula = @Formula, formula_vars = @FormulaVarsJson, price_unit = @PriceUnit, " +
                "cycle_name = @CycleName, effective_from = @EffectiveFrom, remark = @Remark, status = @Status, " +
                "updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new
                {
                    dto.Id, dto.SpecName, dto.MatchUsage, dto.MatchStatus, dto.MatchSpaceType, dto.MatchBuilding,
                    IsFallbackInt = dto.IsFallback ? 1 : 0, dto.UnitPrice, dto.Formula, dto.PriceUnit, dto.CycleName,
                    dto.EffectiveFrom, dto.Remark, dto.Status,
                    FormulaVarsJson = string.IsNullOrWhiteSpace(dto.FormulaVarsJson) ? "" : dto.FormulaVarsJson
                }, transaction);
        }

        public void SoftDeleteSpec(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_charge_standard_spec SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                new { id }, transaction);
        }

        public void DeprecateSpecsByName(IDbConnection connection, IDbTransaction transaction, int standardId, string specName, int exceptId)
        {
            connection.Execute(
                "UPDATE t_charge_standard_spec SET status = 1, updated_at = datetime('now','localtime') " +
                "WHERE standard_id = @standardId AND spec_name = @specName AND id <> @exceptId AND del_flag = 0 AND status = 0",
                new { standardId, specName, exceptId }, transaction);
        }

        public int CountSpecBills(IDbConnection connection, int specId)
        {
            return connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_bill WHERE charge_spec_id = @specId", new { specId });
        }

        // ---------- 计量变量 ----------
        public List<ChargeVariableDto> ListVariables(IDbConnection connection, string keyword, bool includeDisabled)
        {
            string sql = "SELECT id, var_code AS VarCode, var_name AS VarName, unit, value_type AS ValueType, source, " +
                         "default_value AS DefaultValue, object_scope AS ObjectScope, field_key AS FieldKey, " +
                         "is_builtin AS IsBuiltin, remark, status, sort, " +
                         "(SELECT COUNT(1) FROM t_charge_standard_variable sv WHERE sv.variable_id = t_charge_variable.id) AS UsedCount " +
                         "FROM t_charge_variable WHERE del_flag = 0";
            var parameters = new Dictionary<string, object>();
            if (!includeDisabled) { sql += " AND status = 0"; }
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                sql += " AND (var_name LIKE @kw OR var_code LIKE @kw OR unit LIKE @kw)";
                parameters["kw"] = "%" + keyword.Trim() + "%";
            }
            sql += " ORDER BY sort, id";
            return connection.Query<ChargeVariableDto>(sql, parameters).ToList();
        }

        public ChargeVariableDto GetVariable(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<ChargeVariableDto>(
                "SELECT id, var_code AS VarCode, var_name AS VarName, unit, value_type AS ValueType, source, " +
                "default_value AS DefaultValue, object_scope AS ObjectScope, field_key AS FieldKey, " +
                "is_builtin AS IsBuiltin, remark, status, sort FROM t_charge_variable WHERE id = @id AND del_flag = 0", new { id });
        }

        public int InsertVariable(IDbConnection connection, IDbTransaction transaction, ChargeVariableDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_charge_variable (var_code, var_name, unit, value_type, source, default_value, object_scope, " +
                "field_key, is_builtin, remark, status, del_flag, sort) VALUES (@VarCode, @VarName, @Unit, @ValueType, @Source, " +
                "@DefaultValue, @ObjectScope, @FieldKey, @IsBuiltinInt, @Remark, @Status, 0, @Sort); SELECT last_insert_rowid();",
                new
                {
                    dto.VarCode, dto.VarName, dto.Unit, dto.ValueType, dto.Source, dto.DefaultValue, dto.ObjectScope,
                    dto.FieldKey, IsBuiltinInt = dto.IsBuiltin ? 1 : 0, dto.Remark, dto.Status, dto.Sort
                }, transaction);
        }

        public void UpdateVariable(IDbConnection connection, IDbTransaction transaction, ChargeVariableDto dto)
        {
            connection.Execute(
                "UPDATE t_charge_variable SET var_name = @VarName, unit = @Unit, value_type = @ValueType, source = @Source, " +
                "default_value = @DefaultValue, object_scope = @ObjectScope, remark = @Remark, status = @Status, sort = @Sort, " +
                "updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.VarName, dto.Unit, dto.ValueType, dto.Source, dto.DefaultValue, dto.ObjectScope, dto.Remark, dto.Status, dto.Sort },
                transaction);
        }

        public void SoftDeleteVariable(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_charge_variable SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                new { id }, transaction);
        }

        public int CountVariableUsage(IDbConnection connection, int variableId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_charge_standard_variable WHERE variable_id = @variableId", new { variableId });
        }

        public List<int> ListStandardIdsByVariable(IDbConnection connection, int variableId)
        {
            return connection.Query<int>(
                "SELECT standard_id FROM t_charge_standard_variable WHERE variable_id = @variableId",
                new { variableId }).ToList();
        }

        // ---------- 收费标准 ↔ 变量 ----------
        public List<int> ListStandardVariableIds(IDbConnection connection, int standardId)
        {
            return connection.Query<int>(
                "SELECT variable_id FROM t_charge_standard_variable WHERE standard_id = @standardId ORDER BY sort, id",
                new { standardId }).ToList();
        }

        public void ReplaceStandardVariables(IDbConnection connection, IDbTransaction transaction, int standardId,
            IEnumerable<int> variableIds, ISet<int> customVariableIds)
        {
            connection.Execute("DELETE FROM t_charge_standard_variable WHERE standard_id = @standardId", new { standardId }, transaction);
            int sort = 0;
            foreach (int variableId in (variableIds ?? Enumerable.Empty<int>()).Distinct())
            {
                connection.Execute(
                    "INSERT INTO t_charge_standard_variable (standard_id, variable_id, is_custom, sort) VALUES (@standardId, @variableId, @isCustom, @sort)",
                    new { standardId, variableId, isCustom = customVariableIds != null && customVariableIds.Contains(variableId) ? 1 : 0, sort = ++sort },
                    transaction);
            }
        }

        private List<ChargeVariableDto> ListStandardVariables(IDbConnection connection, int standardId)
        {
            return connection.Query<ChargeVariableDto>(
                "SELECT v.id, v.var_code AS VarCode, v.var_name AS VarName, v.unit, v.value_type AS ValueType, v.source, " +
                "v.default_value AS DefaultValue, v.object_scope AS ObjectScope, v.field_key AS FieldKey, " +
                "v.is_builtin AS IsBuiltin, v.remark, v.status, v.sort, sv.is_custom AS IsCustom " +
                "FROM t_charge_standard_variable sv JOIN t_charge_variable v ON v.id = sv.variable_id " +
                "WHERE sv.standard_id = @standardId AND v.del_flag = 0 ORDER BY sv.sort, v.sort", new { standardId }).ToList();
        }

        private static ChargeStandardSpecDto HydrateVars(ChargeStandardSpecDto dto)
        {
            dto.FormulaVars = string.IsNullOrWhiteSpace(dto.FormulaVarsJson)
                ? new List<ChargeFormulaVarDto>()
                : Newtonsoft.Json.JsonConvert.DeserializeObject<List<ChargeFormulaVarDto>>(dto.FormulaVarsJson);
            return dto;
        }
    }
}
