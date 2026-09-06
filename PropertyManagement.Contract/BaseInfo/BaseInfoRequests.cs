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
        public OwnerIdCardType IdCardType { get; set; }
        public string IdCard { get; set; }
        public string Phone { get; set; }
        public string ResidentAddress { get; set; }
        public string EmergencyContactName { get; set; }
        public string EmergencyContactPhone { get; set; }
        public DateTime? CheckInDate { get; set; }
        public OwnerStatus Status { get; set; }
    }

    public class OwnerPropertyRelationRequest
    {
        public int PropertyId { get; set; }
        public int OwnerId { get; set; }
        public OwnerRelType RelType { get; set; }
        public decimal Share { get; set; }
        public DateTime EffectiveAt { get; set; }
        public DateTime? ExpireAt { get; set; }
        public OwnerRelStatus Status { get; set; }
    }

    public class ParkingSpaceRequest
    {
        public string SpaceNo { get; set; }
        public string Area { get; set; }
        public ParkingSpaceType SpaceType { get; set; }
        public ParkingSpaceStatus Status { get; set; }
        public int? PropertyId { get; set; }
        public int? OwnerId { get; set; }
        public decimal? MonthlyRent { get; set; }
        public RentMode RentMode { get; set; }
        public DateTime? RentTo { get; set; }
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
        public ParkingSpaceStatus? SpaceStatus { get; set; }
        public OwnerRelType? RelType { get; set; }
        public OwnerRelStatus? RelStatus { get; set; }
        public OwnerStatus? OwnerStatus { get; set; }
        public bool OnlyArrear { get; set; }
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

    /// <summary>解除业主-房产关系请求（填写解除原因，BR-INF-04 留痕）。</summary>
    public class ReleaseRelationRequest
    {
        public string Reason { get; set; }
    }

    /// <summary>业务参数写入请求（P4-3 车位统计卡说明等）。</summary>
    public class ParamValueRequest
    {
        public string Value { get; set; }
    }
}
