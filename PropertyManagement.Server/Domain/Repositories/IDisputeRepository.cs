using System;
using System.Collections.Generic;
using System.Data;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Dispute;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>民事纠纷调解仓储（M6 D6-4，UC-DIS-001~007 + BR-DIS-01~05）。</summary>
    public interface IDisputeRepository
    {
        List<DisputeTypeDto> ListTypes(IDbConnection connection);
        int InsertType(IDbConnection connection, IDbTransaction transaction, DisputeTypeDto dto);
        int CountCasesByType(IDbConnection connection, int typeId);
        void DeleteType(IDbConnection connection, IDbTransaction transaction, int id);

        DisputeCaseDto GetCase(IDbConnection connection, int id);
        PageResult<DisputeCaseDto> QueryCases(IDbConnection connection, DisputeQueryRequest query, out int total);
        int InsertCase(IDbConnection connection, IDbTransaction transaction, DisputeCaseDto dto);
        void UpdateCase(IDbConnection connection, IDbTransaction transaction, DisputeCaseDto dto);
        void SoftDeleteCase(IDbConnection connection, IDbTransaction transaction, int id);

        List<DisputePartyDto> ListParties(IDbConnection connection, int caseId);
        void InsertParty(IDbConnection connection, IDbTransaction transaction, DisputePartyDto dto);
        void DeletePartiesByCase(IDbConnection connection, IDbTransaction transaction, int caseId);

        List<DisputeRecordDto> ListRecords(IDbConnection connection, int caseId);
        int InsertRecord(IDbConnection connection, IDbTransaction transaction, DisputeRecordDto dto);

        void InsertStatusLog(IDbConnection connection, IDbTransaction transaction, int caseId, int oldStatus, int newStatus);
        List<DisputeStatusLogDto> ListStatusLogs(IDbConnection connection, int caseId);

        DisputeStatisticsDto Statistics(IDbConnection connection);
        int CountActiveCasesByProperty(IDbConnection connection, int propertyId, int typeId, DateTime since);

        /// <summary>调解员推荐（BR-DIS-03：在岗员工 + 历史结案/成功统计；typeId 过滤历史案件类型）。</summary>
        List<DisputeMediatorDto> RecommendMediators(IDbConnection connection, int? typeId, int? propertyId);

        /// <summary>写入通用提醒（t_reminder，如结案复盘提醒 T6-4-4）。</summary>
        void InsertReminder(IDbConnection connection, IDbTransaction transaction, string type, int targetId, DateTime dueAt);
    }
}
