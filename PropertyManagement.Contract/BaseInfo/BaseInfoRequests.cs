using System;
using System.Collections.Generic;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Contract.BaseInfo
{
    public class CommunityRequest
    {
        public string Name { get; set; }
        public string Address { get; set; }
    }

    public class BuildingRequest
    {
        public int CommunityId { get; set; }
        public string BuildingNo { get; set; }
        public int Floors { get; set; }
    }

    public class UnitRequest
    {
        public int BuildingId { get; set; }
        public string UnitNo { get; set; }
    }

    public class PropertyRequest
    {
        public int UnitId { get; set; }
        public string RoomNo { get; set; }
        public decimal Area { get; set; }
        public PropertyUsage Usage { get; set; }
        public PropertyStatus Status { get; set; }
    }

    public class OwnerRequest
    {
        public string Name { get; set; }
        public string IdCard { get; set; }
        public string Phone { get; set; }
    }

    public class OwnerPropertyRelationRequest
    {
        public int PropertyId { get; set; }
        public int OwnerId { get; set; }
        public OwnerRelType RelType { get; set; }
        public DateTime EffectiveAt { get; set; }
        public DateTime? ExpireAt { get; set; }
    }

    public class ParkingSpaceRequest
    {
        public string SpaceNo { get; set; }
        public ParkingSpaceType SpaceType { get; set; }
        public int? PropertyId { get; set; }
        public int? OwnerId { get; set; }
    }

    /// <summary>基础信息查询条件（房产/业主/车位，分页）。</summary>
    public class BaseInfoQueryRequest : PageRequest
    {
        public int? CommunityId { get; set; }
        public int? BuildingId { get; set; }
        public int? UnitId { get; set; }
        public string RoomNo { get; set; }
        public string OwnerName { get; set; }
        public string Phone { get; set; }
        public PropertyStatus? Status { get; set; }
        public string SpaceNo { get; set; }
        public ParkingSpaceType? SpaceType { get; set; }
    }

    /// <summary>基础数据导入请求（UC-INF-006：module + 模板文件）。</summary>
    public class ImportRequest
    {
        public ImportModule Module { get; set; }
        public string FileName { get; set; }
        public byte[] FileContent { get; set; }
    }

    /// <summary>基础信息导出请求（UC-INF-007，留痕 t_export_log）。</summary>
    public class BaseInfoExportRequest
    {
        public string ExportType { get; set; } // property / owner / parking
        public BaseInfoQueryRequest Filter { get; set; }
        public ExportFormat Format { get; set; }
    }
}
