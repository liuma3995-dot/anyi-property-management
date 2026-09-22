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
        public bool DelFlag { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>楼栋（t_building）。</summary>
    public class BuildingDto
    {
        public int Id { get; set; }
        public int CommunityId { get; set; }
        public string CommunityName { get; set; }
        public string BuildingNo { get; set; }
        public int Floors { get; set; }
        public bool DelFlag { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>单元（t_unit）。</summary>
    public class UnitDto
    {
        public int Id { get; set; }
        public int BuildingId { get; set; }
        public string BuildingNo { get; set; }
        public string CommunityName { get; set; }
        public string UnitNo { get; set; }
        public bool DelFlag { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>房产（t_property，BR-INF-01/02）。</summary>
    public class PropertyDto
    {
        public int Id { get; set; }
        /// <summary>所属楼栋（t_property.building_id；楼栋与单元解耦，楼栋必填）。</summary>
        public int? BuildingId { get; set; }
        /// <summary>所属单元（可为空：部分楼栋无单元）。</summary>
        public int? UnitId { get; set; }
        public string UnitNo { get; set; }
        public string BuildingNo { get; set; }
        public string CommunityName { get; set; }
        public string UnitPath { get; set; }
        public string RoomNo { get; set; }
        public decimal Area { get; set; }
        public PropertyUsage Usage { get; set; }
        public PropertyStatus Status { get; set; }
        public string OwnerName { get; set; }
        public string OwnerPhone { get; set; }
        public decimal CurrentArrear { get; set; }
        public bool DelFlag { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>业主（t_owner，BR-INF-04 变更留痕）。</summary>
    public class OwnerDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public OwnerIdCardType IdCardType { get; set; }
        public string IdCard { get; set; }
        public string Phone { get; set; }
        public string ResidentAddress { get; set; }
        public string EmergencyContactName { get; set; }
        public string EmergencyContactPhone { get; set; }
        public DateTime? CheckInDate { get; set; }
        public OwnerStatus Status { get; set; }
        public string StatusText { get; set; }
        public int PropertyCount { get; set; }
        public decimal YearReceivable { get; set; }
        public decimal YearPaid { get; set; }
        public decimal CurrentArrear { get; set; }
        public bool DelFlag { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>业主-房产关系（t_owner_property_rel，BR-INF-02 一房一业主）。</summary>
    public class OwnerPropertyRelationDto
    {
        public int Id { get; set; }
        public int PropertyId { get; set; }
        public int OwnerId { get; set; }
        /// <summary>房产所属楼栋号（跨模块显示：楼栋+单元+房号）。</summary>
        public string BuildingNo { get; set; }
        /// <summary>房产所属单元号（可为空）。</summary>
        public string UnitNo { get; set; }
        public string PropertyRoomNo { get; set; }
        public string PropertyUnitPath { get; set; }
        public string OwnerName { get; set; }
        public string OwnerPhone { get; set; }
        public OwnerRelType RelType { get; set; }
        public decimal Share { get; set; }
        public DateTime EffectiveAt { get; set; }
        public DateTime? ExpireAt { get; set; }
        public OwnerRelStatus Status { get; set; }
        public string StatusText { get; set; }
        public bool DelFlag { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>车位（t_parking_space，BR-INF-03）。</summary>
    public class ParkingSpaceDto
    {
        public int Id { get; set; }
        public string SpaceNo { get; set; }
        public string Area { get; set; }
        public ParkingSpaceType SpaceType { get; set; }
        public ParkingSpaceStatus Status { get; set; }
        public int? PropertyId { get; set; }
        public int? OwnerId { get; set; }
        public string BindingProperty { get; set; }
        public string OwnerName { get; set; }
        public decimal? MonthlyRent { get; set; }
        public RentMode RentMode { get; set; }
        public DateTime? RentTo { get; set; }
        public string StatusText { get; set; }
        public string SpaceTypeText { get; set; }
        public bool DelFlag { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>基础信息变更记录（t_base_change_log，只追加）。</summary>
    public class BaseChangeLogDto
    {
        public int Id { get; set; }
        public BaseChangeObjectType ObjectType { get; set; }
        public int ObjectId { get; set; }
        public string ObjectTypeText { get; set; }
        public string ChangeContent { get; set; }
        public string FieldName { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Operator { get; set; }
        public string Channel { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    /// <summary>基础数据导入批次（t_import_log，UC-INF-006）。</summary>
    public class ImportLogDto
    {
        public int Id { get; set; }
        public ImportModule Module { get; set; }
        public string ModuleText { get; set; }
        public string FileName { get; set; }
        public int Total { get; set; }
        public int Success { get; set; }
        /// <summary>
        /// CHG-v1.1.2-01：覆盖条数 —— 命中既有记录（房产/业主/车位/关系）并按导入文件覆盖更新的行数。
        /// 与 <see cref="Success"/>（新增条数）分开统计，便于用户核对「重复数据做覆盖处理」的效果。
        /// </summary>
        public int Updated { get; set; }
        public int Fail { get; set; }
        public string ErrorFile { get; set; }
        public ImportStatus Status { get; set; }
        public string StatusText { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>导入错误行（逐行校验，错误清单导出）。</summary>
    public class ImportErrorItemDto
    {
        public int RowNo { get; set; }
        public string Field { get; set; }
        public string Content { get; set; }
        public string Reason { get; set; }
        public string Suggestion { get; set; }
    }

    /// <summary>
    /// 导入回执行（逐行处理结果，CHG-v1.2.0-01）。
    /// 每一行数据行都会在这里留一条记录：新增 / 覆盖（写清覆盖了谁、改了什么）/ 失败（原因与建议）。
    /// 用途：批次列表「下载回执」导出 Excel，让用户能追溯同名业主/重复房产被覆盖的对象。
    /// </summary>
    public class ImportRowResultDto
    {
        public int RowNo { get; set; }
        public ImportRowResult Result { get; set; }
        /// <summary>业务对象标识，如「1号楼1单元101」「张伟（档案 #8，电话 13800008888）」。</summary>
        public string ObjectKey { get; set; }
        /// <summary>覆盖明细：「字段：旧值 → 新值」，多字段用「；」分隔。</summary>
        public string ChangeSummary { get; set; }
        public string Reason { get; set; }
        public string Suggestion { get; set; }
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
