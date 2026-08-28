using System;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Contract.BaseInfo
{
    /// <summary>小区/项目（t_community）。</summary>
    public class CommunityDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
    }

    /// <summary>楼栋（t_building）。</summary>
    public class BuildingDto
    {
        public int Id { get; set; }
        public int CommunityId { get; set; }
        public string BuildingNo { get; set; }
        public int Floors { get; set; }
    }

    /// <summary>单元（t_unit）。</summary>
    public class UnitDto
    {
        public int Id { get; set; }
        public int BuildingId { get; set; }
        public string UnitNo { get; set; }
    }

    /// <summary>房产（t_property，BR-INF-01/02）。</summary>
    public class PropertyDto
    {
        public int Id { get; set; }
        public int UnitId { get; set; }
        public string RoomNo { get; set; }
        public decimal Area { get; set; }
        public PropertyUsage Usage { get; set; }
        public PropertyStatus Status { get; set; }
        public bool DelFlag { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>业主（t_owner，BR-INF-04 变更留痕）。</summary>
    public class OwnerDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string IdCard { get; set; }
        public string Phone { get; set; }
        public int Status { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>业主-房产关系（t_owner_property_rel，BR-INF-02 一房一业主）。</summary>
    public class OwnerPropertyRelationDto
    {
        public int Id { get; set; }
        public int PropertyId { get; set; }
        public int OwnerId { get; set; }
        public OwnerRelType RelType { get; set; }
        public DateTime EffectiveAt { get; set; }
        public DateTime? ExpireAt { get; set; }
    }

    /// <summary>车位（t_parking_space，BR-INF-03）。</summary>
    public class ParkingSpaceDto
    {
        public int Id { get; set; }
        public string SpaceNo { get; set; }
        public ParkingSpaceType SpaceType { get; set; }
        public int? PropertyId { get; set; }
        public int? OwnerId { get; set; }
    }

    /// <summary>基础信息变更记录（t_base_change_log，只追加）。</summary>
    public class BaseChangeLogDto
    {
        public int Id { get; set; }
        public BaseChangeObjectType ObjectType { get; set; }
        public int ObjectId { get; set; }
        public string ChangeContent { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    /// <summary>基础数据导入批次（t_import_log，UC-INF-006）。</summary>
    public class ImportLogDto
    {
        public int Id { get; set; }
        public ImportModule Module { get; set; }
        public string FileName { get; set; }
        public int Total { get; set; }
        public int Success { get; set; }
        public int Fail { get; set; }
        public string ErrorFile { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>导入错误行（逐行校验，错误清单导出）。</summary>
    public class ImportErrorItemDto
    {
        public int RowNo { get; set; }
        public string Content { get; set; }
        public string Error { get; set; }
    }

    /// <summary>导入结果（批次 + 错误清单预览）。</summary>
    public class ImportResultDto
    {
        public ImportLogDto Batch { get; set; }
        public System.Collections.Generic.List<ImportErrorItemDto> Errors { get; set; }
    }

    /// <summary>基础信息查询结果（房产/业主/车位列表通用，UC-INF-007）。</summary>
    public class BaseInfoQueryResultDto
    {
        public int Total { get; set; }
        public System.Collections.Generic.List<PropertyDto> Properties { get; set; }
        public System.Collections.Generic.List<OwnerDto> Owners { get; set; }
        public System.Collections.Generic.List<ParkingSpaceDto> ParkingSpaces { get; set; }
    }
}
