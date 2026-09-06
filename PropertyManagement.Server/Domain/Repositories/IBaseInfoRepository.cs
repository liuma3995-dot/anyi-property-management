using System;
using System.Collections.Generic;
using System.Data;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>
    /// 基础信息仓储（M5 D5-1~D5-4）：小区/楼栋/单元/房产/业主/关系/车位/变更记录/导入/导出。
    /// 事务边界由服务层控制；返回契约 DTO（展示字段随 CHG-INF-01~06 补充）。
    /// </summary>
    public interface IBaseInfoRepository
    {
        // ---------- 小区（UC-INF-001） ----------
        List<CommunityDto> ListCommunities(IDbConnection connection, string keyword);
        CommunityDto GetCommunity(IDbConnection connection, int id);
        int InsertCommunity(IDbConnection connection, IDbTransaction transaction, CommunityDto dto);
        void UpdateCommunity(IDbConnection connection, IDbTransaction transaction, CommunityDto dto);
        void SoftDeleteCommunity(IDbConnection connection, IDbTransaction transaction, int id);

        // ---------- 楼栋（UC-INF-001，按小区） ----------
        List<BuildingDto> ListBuildings(IDbConnection connection, int? communityId, string keyword);
        BuildingDto GetBuilding(IDbConnection connection, int id);
        int InsertBuilding(IDbConnection connection, IDbTransaction transaction, BuildingDto dto);
        void UpdateBuilding(IDbConnection connection, IDbTransaction transaction, BuildingDto dto);
        void SoftDeleteBuilding(IDbConnection connection, IDbTransaction transaction, int id);
        int CountChildrenByBuilding(IDbConnection connection, int buildingId);

        // ---------- 单元（UC-INF-001，按楼栋） ----------
        List<UnitDto> ListUnits(IDbConnection connection, int? buildingId, string keyword);
        UnitDto GetUnit(IDbConnection connection, int id);
        int InsertUnit(IDbConnection connection, IDbTransaction transaction, UnitDto dto);
        void UpdateUnit(IDbConnection connection, IDbTransaction transaction, UnitDto dto);
        void SoftDeleteUnit(IDbConnection connection, IDbTransaction transaction, int id);
        int CountChildrenByUnit(IDbConnection connection, int unitId);

        // ---------- 房产（UC-INF-002，BR-INF-01） ----------
        PropertyDto GetProperty(IDbConnection connection, int id);
        PageResult<PropertyDto> QueryProperties(IDbConnection connection, BaseInfoQueryRequest query, out int total);
        int InsertProperty(IDbConnection connection, IDbTransaction transaction, PropertyDto dto);
        void UpdateProperty(IDbConnection connection, IDbTransaction transaction, PropertyDto dto);
        void SoftDeleteProperty(IDbConnection connection, IDbTransaction transaction, int id);
        PropertyDto GetPropertyByUnitRoom(IDbConnection connection, IDbTransaction transaction, int unitId, string roomNo, int excludeId);

        // ---------- 业主（UC-INF-003） ----------
        OwnerDto GetOwner(IDbConnection connection, int id);
        PageResult<OwnerDto> QueryOwners(IDbConnection connection, BaseInfoQueryRequest query, out int total);
        int InsertOwner(IDbConnection connection, IDbTransaction transaction, OwnerDto dto);
        void UpdateOwner(IDbConnection connection, IDbTransaction transaction, OwnerDto dto);
        void SoftDeleteOwner(IDbConnection connection, IDbTransaction transaction, int id);

        // ---------- 业主-房产关系（UC-INF-004，BR-INF-02） ----------
        OwnerPropertyRelationDto GetRelation(IDbConnection connection, int id);
        PageResult<OwnerPropertyRelationDto> QueryRelations(IDbConnection connection, BaseInfoQueryRequest query, out int total);
        List<OwnerPropertyRelationDto> ListRelationsByOwner(IDbConnection connection, int ownerId);
        List<OwnerPropertyRelationDto> ListRelationsByProperty(IDbConnection connection, int propertyId);
        int InsertRelation(IDbConnection connection, IDbTransaction transaction, OwnerPropertyRelationDto dto);
        void UpdateRelation(IDbConnection connection, IDbTransaction transaction, OwnerPropertyRelationDto dto);
        void SoftDeleteRelation(IDbConnection connection, IDbTransaction transaction, int id);
        int CountActiveOwnerRelationsByProperty(IDbConnection connection, IDbTransaction transaction, int propertyId, int excludeId);

        // ---------- 车位（UC-INF-005，BR-INF-03） ----------
        ParkingSpaceDto GetParking(IDbConnection connection, int id);
        PageResult<ParkingSpaceDto> QueryParkings(IDbConnection connection, BaseInfoQueryRequest query, out int total);
        int InsertParking(IDbConnection connection, IDbTransaction transaction, ParkingSpaceDto dto);
        void UpdateParking(IDbConnection connection, IDbTransaction transaction, ParkingSpaceDto dto);
        void SoftDeleteParking(IDbConnection connection, IDbTransaction transaction, int id);
        ParkingSpaceDto GetParkingByNo(IDbConnection connection, string spaceNo, int excludeId);
        int CountParkingsBoundToProperty(IDbConnection connection, IDbTransaction transaction, int propertyId, int excludeId, int propertyRightType);

        // ---------- 变更记录（BR-INF-04） ----------
        List<BaseChangeLogDto> ListChangeLogs(IDbConnection connection, int objectType, int objectId);
        void InsertChangeLog(IDbConnection connection, IDbTransaction transaction, BaseChangeLogDto dto);

        // ---------- 导入（UC-INF-006，BR-INF-05） ----------
        int InsertImportLog(IDbConnection connection, IDbTransaction transaction, ImportLogDto dto);
        void UpdateImportLog(IDbConnection connection, IDbTransaction transaction, int id, ImportStatus status, int success, int fail, string errorFile);
        ImportLogDto GetImportLog(IDbConnection connection, int id);
        List<ImportLogDto> ListImportLogs(IDbConnection connection);
        void InsertImportErrors(IDbConnection connection, IDbTransaction transaction, IEnumerable<ImportErrorItemDto> errors, int importId);
        List<ImportErrorItemDto> ListImportErrors(IDbConnection connection, int importId);

        // ---------- 导出（UC-INF-007） ----------
        int InsertExportLog(IDbConnection connection, IDbTransaction transaction, string module, int format, string filePath);
    }
}
