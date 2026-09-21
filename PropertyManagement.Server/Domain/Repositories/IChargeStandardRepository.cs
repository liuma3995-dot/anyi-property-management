using System.Collections.Generic;
using System.Data;
using PropertyManagement.Contract.Finance;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>
    /// 收费项目「价目表 + 计量变量」仓储（CHG-v1.1.2-26）。
    /// 三层模型：收费标准（价目表主体）→ 规格明细 → 收费项目（可出账项）；
    /// 计量单位与计算规则统一由 t_charge_variable 驱动。
    /// 事务边界由服务层控制。
    /// </summary>
    public interface IChargeStandardRepository
    {
        // ---------- 收费标准 ----------
        List<ChargeStandardDto> ListStandards(IDbConnection connection, string keyword, string category, bool includeDisabled);
        ChargeStandardDto GetStandard(IDbConnection connection, int id);
        int InsertStandard(IDbConnection connection, IDbTransaction transaction, ChargeStandardDto dto);
        void UpdateStandard(IDbConnection connection, IDbTransaction transaction, ChargeStandardDto dto);
        void SoftDeleteStandard(IDbConnection connection, IDbTransaction transaction, int id);
        int CountStandardItems(IDbConnection connection, int standardId);

        // ---------- 规格明细 ----------
        List<ChargeStandardSpecDto> ListSpecs(IDbConnection connection, int standardId, bool includeDisabled);
        ChargeStandardSpecDto GetSpec(IDbConnection connection, int id);
        int InsertSpec(IDbConnection connection, IDbTransaction transaction, ChargeStandardSpecDto dto);
        void UpdateSpec(IDbConnection connection, IDbTransaction transaction, ChargeStandardSpecDto dto);
        void SoftDeleteSpec(IDbConnection connection, IDbTransaction transaction, int id);
        void DeprecateSpecsByName(IDbConnection connection, IDbTransaction transaction, int standardId, string specName, int exceptId);
        int CountSpecBills(IDbConnection connection, int specId);

        // ---------- 计量变量 ----------
        List<ChargeVariableDto> ListVariables(IDbConnection connection, string keyword, bool includeDisabled);
        ChargeVariableDto GetVariable(IDbConnection connection, int id);
        int InsertVariable(IDbConnection connection, IDbTransaction transaction, ChargeVariableDto dto);
        void UpdateVariable(IDbConnection connection, IDbTransaction transaction, ChargeVariableDto dto);
        void SoftDeleteVariable(IDbConnection connection, IDbTransaction transaction, int id);
        int CountVariableUsage(IDbConnection connection, int variableId);
        /// <summary>引用某个计量变量的收费标准 ID 列表（变量单位变更时级联重算单价单位）。</summary>
        List<int> ListStandardIdsByVariable(IDbConnection connection, int variableId);

        // ---------- 收费标准 ↔ 变量 ----------
        List<int> ListStandardVariableIds(IDbConnection connection, int standardId);
        void ReplaceStandardVariables(IDbConnection connection, IDbTransaction transaction, int standardId, IEnumerable<int> variableIds, ISet<int> customVariableIds);
    }
}
