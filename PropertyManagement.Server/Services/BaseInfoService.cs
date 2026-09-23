using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using Dapper;
using NLog;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 基础信息服务（M5 D5-1~D5-4）：小区/楼栋/单元/房产/业主/关系/车位 CRUD + 变更留痕 + Excel 导入导出。
    /// 业务规则：BR-INF-01（房号唯一）、BR-INF-02（一房一业主）、BR-INF-03（车位唯一/「普通」类型禁售）、
    /// BR-INF-04（变更留痕）、BR-INF-05（导入错误行不入库）。
    /// </summary>
    public class BaseInfoService
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IBaseInfoRepository _repo;
        private readonly AuditService _audit = new AuditService();

        public BaseInfoService()
            : this(new SqliteConnectionFactory(), new SqlBaseInfoRepository())
        {
        }

        public BaseInfoService(IDbConnectionFactory connectionFactory, IBaseInfoRepository repo)
        {
            _connectionFactory = connectionFactory;
            _repo = repo;
        }

        // ===================== 小区 =====================
        public List<CommunityDto> ListCommunities(string keyword) =>
            WithConnection(c => _repo.ListCommunities(c, keyword));

        public CommunityDto GetCommunity(int id) =>
            WithConnection(c => _repo.GetCommunity(c, id) ?? throw ApiException.NotFound("小区不存在"));

        public CommunityDto SaveCommunity(int id, CommunityRequest request) =>
            WithTransaction((connection, transaction) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("小区名称不能为空");
                if (id > 0)
                {
                    var existing = _repo.GetCommunity(connection, id) ?? throw ApiException.NotFound("小区不存在");
                    var dto = new CommunityDto { Id = id, Name = request.Name.Trim(), Address = request.Address };
                    _repo.UpdateCommunity(connection, transaction, dto);
                    return dto;
                }
                var created = new CommunityDto { Name = request.Name.Trim(), Address = request.Address };
                created.Id = _repo.InsertCommunity(connection, transaction, created);
                return created;
            });

        public void DeleteCommunity(int id) =>
            WithTransaction((connection, transaction) =>
            {
                var children = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_building WHERE community_id = @id AND del_flag = 0", new { id });
                if (children > 0) throw ApiException.Conflict("该小区下存在楼栋，不能删除");
                _repo.SoftDeleteCommunity(connection, transaction, id);
            });

        // ===================== 楼栋 =====================
        public List<BuildingDto> ListBuildings(int? communityId, string keyword) =>
            WithConnection(c => _repo.ListBuildings(c, communityId, keyword));

        public BuildingDto GetBuilding(int id) =>
            WithConnection(c => _repo.GetBuilding(c, id) ?? throw ApiException.NotFound("楼栋不存在"));

        public BuildingDto SaveBuilding(int id, BuildingRequest request) =>
            WithTransaction((connection, transaction) =>
            {
                if (request.CommunityId <= 0) throw ApiException.ValidationFailed("请选择所属小区");
                if (string.IsNullOrWhiteSpace(request.BuildingNo)) throw ApiException.ValidationFailed("楼栋号不能为空");
                if (id > 0)
                {
                    var dto = new BuildingDto { Id = id, CommunityId = request.CommunityId, BuildingNo = request.BuildingNo.Trim(), Floors = request.Floors, DelFlag = false };
                    _repo.UpdateBuilding(connection, transaction, dto);
                    return dto;
                }
                var created = new BuildingDto { CommunityId = request.CommunityId, BuildingNo = request.BuildingNo.Trim(), Floors = request.Floors };
                created.Id = _repo.InsertBuilding(connection, transaction, created);
                return created;
            });

        public void DeleteBuilding(int id) =>
            WithTransaction((connection, transaction) =>
            {
                if (_repo.CountChildrenByBuilding(connection, id) > 0) throw ApiException.Conflict("该楼栋下存在单元，不能删除");
                _repo.SoftDeleteBuilding(connection, transaction, id);
            });

        // ===================== 单元 =====================
        public List<UnitDto> ListUnits(int? buildingId, string keyword) =>
            WithConnection(c => _repo.ListUnits(c, buildingId, keyword));

        public UnitDto GetUnit(int id) =>
            WithConnection(c => _repo.GetUnit(c, id) ?? throw ApiException.NotFound("单元不存在"));

        public UnitDto SaveUnit(int id, UnitRequest request) =>
            WithTransaction((connection, transaction) =>
            {
                if (request.BuildingId <= 0) throw ApiException.ValidationFailed("请选择所属楼栋");
                if (string.IsNullOrWhiteSpace(request.UnitNo)) throw ApiException.ValidationFailed("单元号不能为空");
                if (id > 0)
                {
                    var dto = new UnitDto { Id = id, BuildingId = request.BuildingId, UnitNo = request.UnitNo.Trim(), DelFlag = false };
                    _repo.UpdateUnit(connection, transaction, dto);
                    return dto;
                }
                var created = new UnitDto { BuildingId = request.BuildingId, UnitNo = request.UnitNo.Trim() };
                created.Id = _repo.InsertUnit(connection, transaction, created);
                return created;
            });

        public void DeleteUnit(int id) =>
            WithTransaction((connection, transaction) =>
            {
                if (_repo.CountChildrenByUnit(connection, id) > 0) throw ApiException.Conflict("该单元下存在房产，不能删除");
                _repo.SoftDeleteUnit(connection, transaction, id);
            });

        // ===================== 房产（BR-INF-01） =====================
        public PropertyDto GetProperty(int id) =>
            WithConnection(c => _repo.GetProperty(c, id) ?? throw ApiException.NotFound("房产不存在"));

        public PageResult<PropertyDto> QueryProperties(BaseInfoQueryRequest query) =>
            WithConnection(c => _repo.QueryProperties(c, query, out int total));

        public PropertyDto SaveProperty(int id, PropertyRequest request) =>
            WithTransaction((connection, transaction) =>
            {
                if (request.BuildingId <= 0) throw ApiException.ValidationFailed("请选择所属楼栋");
                if (string.IsNullOrWhiteSpace(request.RoomNo)) throw ApiException.ValidationFailed("房号不能为空");
                if (request.Area <= 0) throw ApiException.ValidationFailed("建筑面积必须大于 0");

                string room = request.RoomNo.Trim();
                // 单元改为可选：给出单元时需属于所选楼栋（部分楼栋无单元）
                int? unitId = request.UnitId.HasValue && request.UnitId.Value > 0 ? request.UnitId : (int?)null;
                if (unitId.HasValue)
                {
                    var unit = _repo.GetUnit(connection, unitId.Value);
                    if (unit == null || unit.BuildingId != request.BuildingId)
                        throw ApiException.ValidationFailed("所选单元不属于该楼栋");
                }

                // BR-INF-01 房号唯一：有单元按「单元+房号」，无单元按「楼栋+房号」
                var duplicate = unitId.HasValue
                    ? _repo.GetPropertyByUnitRoom(connection, transaction, unitId.Value, room, id)
                    : _repo.GetPropertyByBuildingRoom(connection, transaction, request.BuildingId, room, id);
                if (duplicate != null)
            throw ApiException.Conflict(unitId.HasValue ? "该单元下房号已存在" : "该楼栋下房号已存在");

                if (id > 0)
                {
                    var dto = new PropertyDto { Id = id, BuildingId = request.BuildingId, UnitId = unitId, RoomNo = room, Area = request.Area, Usage = request.Usage, Status = request.Status };
                    _repo.UpdateProperty(connection, transaction, dto);
                    WriteChangeLog(connection, transaction, BaseChangeObjectType.Property, id, "基础信息", "房产更新", "房产 " + room + " 更新", "后台维护");
                    return dto;
                }
                var created = new PropertyDto { BuildingId = request.BuildingId, UnitId = unitId, RoomNo = room, Area = request.Area, Usage = request.Usage, Status = request.Status };
                created.Id = _repo.InsertProperty(connection, transaction, created);
                WriteChangeLog(connection, transaction, BaseChangeObjectType.Property, created.Id, "基础信息", null, "房产 " + room + " 已录入", "后台维护");
                return created;
            });

        public void DeleteProperty(int id) =>
            WithTransaction((connection, transaction) =>
            {
                int relCount = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_owner_property_rel WHERE property_id = @id AND del_flag = 0", new { id });
                // BR-INF-02：存在绑定关系时禁止删除房产，需先解除关系（同业主模块业务逻辑，阻止级联解除造成的业务漏洞）
                if (relCount > 0) throw ApiException.Conflict("该房产存在业主绑定关系，请先解除关系");

                // v1.1.0 R1（跨模块引用闭环）：删除前校验其它模块的在用引用，
                // 保证「能删 ⇒ 必无在用子行」，使「一键清理残余数据」物理回收留痕父行后不产生孤儿引用。
                int billCount = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_bill WHERE property_id = @id AND del_flag = 0", new { id });
                if (billCount > 0)
                    throw ApiException.Conflict(
                        "该房产存在 " + billCount + " 条未删除账单（含已缴/未缴），财务记录需保留；" +
                        "请先在「账单工作台」处理相关账单后再删除房产");
                int parkingCount = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_parking_space WHERE property_id = @id AND del_flag = 0", new { id });
                if (parkingCount > 0)
                    throw ApiException.Conflict(
                        "该房产下绑定 " + parkingCount + " 个车位，请先在「车位维护」解除房产绑定后再删除房产");

                _repo.SoftDeleteProperty(connection, transaction, id);
                WriteChangeLog(connection, transaction, BaseChangeObjectType.Property, id,
                    "删除", null, "房产已删除", "后台维护");
            });

        // ===================== 业主（BR-INF-04 变更留痕） =====================
        public OwnerDto GetOwner(int id) =>
            WithConnection(c => _repo.GetOwner(c, id) ?? throw ApiException.NotFound("业主不存在"));

        public PageResult<OwnerDto> QueryOwners(BaseInfoQueryRequest query) =>
            WithConnection(c => _repo.QueryOwners(c, query, out int total));

        public OwnerDto SaveOwner(int id, OwnerRequest request) =>
            WithTransaction((connection, transaction) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("姓名不能为空");
                // v1.1.0 F-07：联系电话放开为选填（负责人 2026-09-16 确认，与导入模板必填矩阵统一）
                // 说明：同名业主请填写证件号或联系电话，用于档案区分与查重

                var owner = new OwnerDto
                {
                    Name = request.Name.Trim(),
                    IdCardType = request.IdCardType,
                    IdCard = (request.IdCard ?? string.Empty).Trim(),
                    Phone = (request.Phone ?? string.Empty).Trim(),
                    ResidentAddress = request.ResidentAddress,
                    EmergencyContactName = request.EmergencyContactName,
                    EmergencyContactPhone = request.EmergencyContactPhone,
                    CheckInDate = request.CheckInDate,
                    Status = request.Status
                };

                if (id > 0)
                {
                    var existing = _repo.GetOwner(connection, id) ?? throw ApiException.NotFound("业主不存在");
                    owner.Id = id;
                    _repo.UpdateOwner(connection, transaction, owner);
                    LogOwnerChanges(connection, transaction, existing, owner);
                    return owner;
                }
                owner.Id = _repo.InsertOwner(connection, transaction, owner);
                WriteChangeLog(connection, transaction, BaseChangeObjectType.Owner, owner.Id, "档案", null,
                    "业主 " + owner.Name + " 档案建立", "后台维护");
                return owner;
            });

        public void DeleteOwner(int id) =>
            WithTransaction((connection, transaction) =>
            {
                int relCount = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_owner_property_rel WHERE owner_id = @id AND del_flag = 0", new { id });
                if (relCount > 0) throw ApiException.Conflict("该业主存在房产绑定关系，请先解除关系");

                // v1.1.0 R1（跨模块引用闭环，与房产/车位同口径）：其余模块的在用引用同样必须先解除
                int parkingCount = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_parking_space WHERE owner_id = @id AND del_flag = 0", new { id });
                if (parkingCount > 0)
                    throw ApiException.Conflict("该业主名下绑定 " + parkingCount + " 个车位，请先在「车位维护」解除绑定后再删除业主");
                int depositCount = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_pre_deposit WHERE owner_id = @id", new { id });
                if (depositCount > 0)
                    throw ApiException.Conflict("该业主存在预存款记录，请先在「收款登记」处理余额后再删除业主");
                int disputeCount = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_dispute_party WHERE owner_id = @id", new { id });
                if (disputeCount > 0)
                    throw ApiException.Conflict("该业主是 " + disputeCount + " 个纠纷案件的当事人，请在「纠纷调解」核对后再删除业主");

                _repo.SoftDeleteOwner(connection, transaction, id);
            });

        // ===================== 业主-房产关系（BR-INF-02） =====================
        public OwnerPropertyRelationDto GetRelation(int id) =>
            WithConnection(c => NormalizeRelation(_repo.GetRelation(c, id) ?? throw ApiException.NotFound("关系不存在")));

        public PageResult<OwnerPropertyRelationDto> QueryRelations(BaseInfoQueryRequest query) =>
            WithConnection(c =>
            {
                var page = _repo.QueryRelations(c, query, out int total);
                foreach (var it in page.Items) NormalizeRelation(it);
                return page;
            });

        public List<OwnerPropertyRelationDto> ListRelationsByOwner(int ownerId) =>
            WithConnection(c => _repo.ListRelationsByOwner(c, ownerId).Select(NormalizeRelation).ToList());

        public OwnerPropertyRelationDto SaveRelation(int id, OwnerPropertyRelationRequest request) =>
            WithTransaction((connection, transaction) =>
            {
                if (request.PropertyId <= 0) throw ApiException.ValidationFailed("请选择房产");
                if (request.OwnerId <= 0) throw ApiException.ValidationFailed("请选择业主");
                if (request.EffectiveAt == default) throw ApiException.ValidationFailed("请选择关系起始日期");

                decimal share = request.Share;
                if (share <= 0)
                {
                    // 业务默认：业主 100%，共有人 50%；租户备案不适用份额(0)。用户可自定义 1~100。
                    if (request.RelType == OwnerRelType.Owner) share = 100m;
                    else if (request.RelType == OwnerRelType.CoOwner) share = 50m;
                    else share = 0m;
                }
                if (share < 0 || share > 100) throw ApiException.ValidationFailed("份额须在 0~100 之间");

                OwnerRelStatus relStatus = ResolveRelStatus(request.ExpireAt);
                if (request.RelType == OwnerRelType.Owner)
                {
                    int ownerCount = _repo.CountActiveOwnerRelationsByProperty(connection, transaction, request.PropertyId, id);
            if (ownerCount > 0) throw ApiException.Conflict("该房产已存在一名业主，一房仅可登记一名业主");
                }
                else if (request.RelType == OwnerRelType.CoOwner)
                {
                    int ownerCount = _repo.CountActiveOwnerRelationsByProperty(connection, transaction, request.PropertyId, -1);
                    if (ownerCount == 0) throw ApiException.ValidationFailed("请先绑定业主，再添加共有人");
                }

                var rel = new OwnerPropertyRelationDto
                {
                    PropertyId = request.PropertyId,
                    OwnerId = request.OwnerId,
                    RelType = request.RelType,
                    Share = share,
                    EffectiveAt = request.EffectiveAt,
                    ExpireAt = request.ExpireAt,
                    Status = relStatus
                };
                if (id > 0)
                {
                    rel.Id = id;
                    _repo.UpdateRelation(connection, transaction, rel);
                    WriteChangeLog(connection, transaction, BaseChangeObjectType.Relation, id, "关系变更",
                        null, "更新了该房产与业主关系", "后台维护");
                    return rel;
                }
                rel.Id = _repo.InsertRelation(connection, transaction, rel);
                WriteChangeLog(connection, transaction, BaseChangeObjectType.Relation, rel.Id, "绑定",
                    null, "建立业主-房产关系", "后台维护");
                return rel;
            });

        public void ReleaseRelation(int id, string reason) =>
            WithTransaction((connection, transaction) =>
            {
                var existing = _repo.GetRelation(connection, id) ?? throw ApiException.NotFound("关系不存在");
                _repo.SoftDeleteRelation(connection, transaction, id);
                WriteChangeLog(connection, transaction, BaseChangeObjectType.Relation, id, "解除",
                    existing.StatusText ?? string.Empty, "已解除",
                    string.IsNullOrWhiteSpace(reason) ? "后台维护" : ("解除原因：" + reason.Trim()));
            });

        // ===================== 车位（BR-INF-03） =====================
        public ParkingSpaceDto GetParking(int id) =>
            WithConnection(c => _repo.GetParking(c, id) ?? throw ApiException.NotFound("车位不存在"));

        public PageResult<ParkingSpaceDto> QueryParkings(BaseInfoQueryRequest query) =>
            WithConnection(c => _repo.QueryParkings(c, query, out int total));

        public ParkingSpaceDto SaveParking(int id, ParkingSpaceRequest request) =>
            WithTransaction((connection, transaction) =>
            {
                if (string.IsNullOrWhiteSpace(request.SpaceNo)) throw ApiException.ValidationFailed("车位编号不能为空");

                // 普通（原「人防」）类型禁售：该类型车位不可为已售（CHG-v1.1.2-48 文案由「人防」改为「普通」，约束不变）
                if (request.SpaceType == ParkingSpaceType.CivilDefense && request.Status == ParkingSpaceStatus.Owned)
                    throw ApiException.ValidationFailed("普通车位不可标为出售");

                string spaceNo = request.SpaceNo.Trim();
                var dup = _repo.GetParkingByNo(connection, spaceNo, id);
            if (dup != null) throw ApiException.Conflict("车位编号已存在");

                // CHG-v1.1.0-12：车位不再由表单手工指定房产；绑定房产由「绑定业主」自动引用，
                // 规则（负责人口径）：业主名下有效房产 ≥1 套 → 自动引用（编辑时若原绑定仍属该业主则保留原绑定，
                // 否则取楼栋/房号排序第一套）；0 套 → 不绑定房产（界面显示「未绑定」）。
                int? existingPropertyId = null;
                if (id > 0)
                {
                    ParkingSpaceDto existing = _repo.GetParking(connection, id);
                    existingPropertyId = existing == null ? (int?)null : existing.PropertyId;
                }
                int? derivedPropertyId = null;
                if (request.OwnerId.HasValue && request.OwnerId.Value > 0)
                {
                    List<int> ownerProperties = _repo.ListActivePropertyIdsByOwner(connection, request.OwnerId.Value);
                    if (ownerProperties.Count > 0 && existingPropertyId.HasValue && ownerProperties.Contains(existingPropertyId.Value))
                    {
                        derivedPropertyId = existingPropertyId;
                    }
                    else if (ownerProperties.Count > 0)
                    {
                        derivedPropertyId = ownerProperties[0];
                    }
                }

                if (request.SpaceType == ParkingSpaceType.PropertyRight && request.Status == ParkingSpaceStatus.Owned
                    && !derivedPropertyId.HasValue)
                {
                    throw ApiException.ValidationFailed(
                        "产权车位标记为已售需先绑定业主，且该业主名下需有有效房产（用于自动引用房产权属）");
                }

                // 一房最多绑定一个产权车位
                if (request.SpaceType == ParkingSpaceType.PropertyRight && derivedPropertyId.HasValue && derivedPropertyId.Value > 0)
                {
                    int bound = _repo.CountParkingsBoundToProperty(connection, transaction, derivedPropertyId.Value, id, (int)ParkingSpaceType.PropertyRight);
            if (bound > 0) throw ApiException.Conflict("该房产已绑定一个产权车位");
                }

                var parking = new ParkingSpaceDto
                {
                    SpaceNo = spaceNo,
                    Area = request.Area,
                    SpaceType = request.SpaceType,
                    Status = request.Status,
                    PropertyId = derivedPropertyId,
                    OwnerId = request.OwnerId,
                    MonthlyRent = request.MonthlyRent,
                    RentMode = request.RentMode,
                    RentTo = request.RentTo
                };
                if (id > 0)
                {
                    parking.Id = id;
                    _repo.UpdateParking(connection, transaction, parking);
                    WriteChangeLog(connection, transaction, BaseChangeObjectType.Parking, id, "维护", null,
                        "车位 " + spaceNo + " 更新", "后台维护");
                    return parking;
                }
                parking.Id = _repo.InsertParking(connection, transaction, parking);
                WriteChangeLog(connection, transaction, BaseChangeObjectType.Parking, parking.Id, "维护", null,
                    "车位 " + spaceNo + " 已录入", "后台维护");
                return parking;
            });

        public void DeleteParking(int id) =>
            WithTransaction((connection, transaction) =>
            {
                // v1.1.0 R1（跨模块引用闭环）：车位被账单引用时不可删除，避免清理留痕后账单失去缴费主体
                int billCount = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM t_bill WHERE parking_id = @id AND del_flag = 0", new { id });
                if (billCount > 0)
                    throw ApiException.Conflict(
                        "该车位存在 " + billCount + " 条未删除账单，财务记录需保留；" +
                        "请先在「账单工作台」处理相关账单后再删除车位");

                _repo.SoftDeleteParking(connection, transaction, id);
                WriteChangeLog(connection, transaction, BaseChangeObjectType.Parking, id, "删除", null, "车位已删除", "后台维护");
            });

        // ===================== 变更历史（BR-INF-04） =====================
        public List<BaseChangeLogDto> GetChangeLogs(int objectType, int objectId) =>
            WithConnection(c => _repo.ListChangeLogs(c, objectType, objectId));

        // ===================== 导出（UC-INF-007） =====================
        public ExportLogDto Export(BaseInfoExportRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ExportType)) throw ApiException.BadRequest("导出类型不能为空");
            string type = request.ExportType.Trim().ToLowerInvariant();
            string module = type == "parking" ? "parking" : (type == "owner" ? "owner" : (type == "relation" ? "relation" : "property"));
            string fileName = "基础信息_" + module + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xlsx";
            string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);

            using (var workbook = new XLWorkbook())
            {
                if (module == "property") ExportProperties(workbook, request.Filter);
                else if (module == "owner") ExportOwners(workbook, request.Filter);
                else if (module == "relation") ExportRelations(workbook, request.Filter);
                else ExportParkings(workbook, request.Filter);
                workbook.SaveAs(filePath);
            }

            int format = (int)ExportFormat.Excel;
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                int logId = _repo.InsertExportLog(connection, transaction, module, format, filePath);
                transaction.Commit();
                return new ExportLogDto { Id = logId, Module = module, Format = ExportFormat.Excel, FilePath = filePath, CreatedAt = DateTime.Now };
            }
        }

        // ===================== 导入（UC-INF-006，BR-INF-05） =====================
        public ImportResultDto Import(ImportRequest request)
        {
            if (request == null || request.FileContent == null || request.FileContent.Length == 0)
                throw ApiException.BadRequest("上传文件不能为空");

            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                var log = new ImportLogDto
                {
                    Module = request.Module,
                    FileName = string.IsNullOrWhiteSpace(request.FileName) ? "导入文件.xlsx" : request.FileName,
                    Total = 0,
                    Status = ImportStatus.Processing,
                    CreatedBy = "系统管理员"
                };
                log.Id = _repo.InsertImportLog(connection, transaction, log);

                List<ImportErrorItemDto> errors = new List<ImportErrorItemDto>();
                int success = 0;
                int updated = 0;
                int total = 0;
                ImportCount count = new ImportCount();
                try
                {
                    using (var stream = new MemoryStream(request.FileContent))
                    using (var workbook = new XLWorkbook(stream))
                    {
                        IXLWorksheet sheet = workbook.Worksheets.FirstOrDefault();
                        if (sheet == null) throw ApiException.BadRequest("Excel 未包含工作表");
                        // v1.1.0 F-08b：模板第 2 行为示例行（自动跳过），批次 total 只统计真实数据行
                        if (sheet.LastRowUsed() == null) { total = 0; }
                        else
                        {
                            var lastCol = sheet.LastColumnUsed();
                            total = CountDataRows(sheet, 1, lastCol == null ? 1 : lastCol.ColumnNumber());
                        }
                        log.Total = total;
                        if (total == 0)
                        {
                            errors.Add(new ImportErrorItemDto
                            {
                                RowNo = 0,
                                Field = "文件",
                                Content = string.Empty,
                                Reason = "文件未包含数据行（模板仅含表头），请先填写数据再导入",
                                Suggestion = "在模板表头下方填写数据行后重新上传"
                            });
                        }

                        switch (request.Module)
                        {
                            case ImportModule.Property: count = ImportProperties(connection, transaction, sheet, errors); break;
                            case ImportModule.Owner: count = ImportOwners(connection, transaction, sheet, errors); break;
                            case ImportModule.Parking: count = ImportParkings(connection, transaction, sheet, errors); break;
                            case ImportModule.OwnerRelation: count = ImportRelations(connection, transaction, sheet, errors); break;
                            default: throw ApiException.BadRequest("不支持的数据类型");
                        }
                        success = count.Inserted;
                        updated = count.Updated;
                    }
                }
                catch (Exception ex)
                {
                    // v1.1.0 第 3 轮：不再把 SQLite/.NET 英文异常原文暴露给用户；原文进服务端日志便于排查
                    Log.Error(ex, "导入失败：module={0} file={1}", request.Module, log.FileName);
                    errors.Add(new ImportErrorItemDto
                    {
                        RowNo = 0,
                        Field = "文件",
                        Content = string.Empty,
                        Reason = DescribeImportFailure(ex),
                        Suggestion = "请核对模板格式（数字列填数值、日期列填 yyyy-MM-dd），或重新下载最新模板后重试"
                    });
                }

                int fail = errors.Count;
                ImportStatus status = total == 0 ? ImportStatus.Failed
                    : (fail == 0 ? ImportStatus.Success
                        : (success > 0 ? ImportStatus.PartialSuccess : ImportStatus.Failed));
                string errorFile = fail > 0 ? BuildErrorFile(log.Id, errors) : null;

                // v1.2.0（CHG-v1.2.0-01）：逐行回执落库（新增/覆盖/失败）——「下载回执」据此导出
                if (count.Rows.Count > 0)
                    _repo.InsertImportRows(connection, transaction, count.Rows, log.Id);

                _repo.UpdateImportLog(connection, transaction, log.Id, total, status, success, fail, errorFile);
                _repo.UpdateImportLogUpdated(connection, transaction, log.Id, updated);
                if (fail > 0) _repo.InsertImportErrors(connection, transaction, errors, log.Id);
                transaction.Commit();

                log.Success = success;
                log.Updated = updated;
                log.Fail = fail;
                log.Status = status;
                log.StatusText = status == ImportStatus.Success ? "成功"
                    : (status == ImportStatus.PartialSuccess ? "部分成功" : "失败");
                log.ErrorFile = errorFile;
                return new ImportResultDto { Batch = log, Errors = errors };
            }
        }

        public ImportLogDto GetImportLog(int id) =>
            WithConnection(c => _repo.GetImportLog(c, id) ?? throw ApiException.NotFound("导入批次不存在"));

        public string GetParam(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw ApiException.BadRequest("参数键不能为空");
            return WithConnection(c => c.ExecuteScalar<string>(
                "SELECT param_value FROM t_param WHERE param_key = @key", new { key = key.Trim() }));
        }

        public void SetParam(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw ApiException.BadRequest("参数键不能为空");
            string k = key.Trim();
            string v = value == null ? string.Empty : value.Trim();
            WithTransaction((connection, transaction) =>
            {
                connection.Execute(
                    "INSERT INTO t_param (param_key, param_value) VALUES (@k, @v) " +
                    "ON CONFLICT(param_key) DO UPDATE SET param_value = @v, updated_at = datetime('now','localtime')",
                    new { k, v }, transaction);
            });
        }

        public List<ImportLogDto> ListImportLogs() =>
            WithConnection(c => _repo.ListImportLogs(c));

        /// <summary>
        /// 导入批次记录批量删除（v1.1.0-⑤，UC-INF-006 补充）：软删留痕（del_flag=1），批次不再出现在
        /// 「导入批次记录」列表；物理清理由「系统设置 / 备份与恢复 → 一键清理残余数据」统一执行
        /// （含该批次的错误行明细，避免留下孤儿残余数据）。
        /// 已导入成功的业务数据（房产/业主/车位/关系）不受影响——本操作只针对导入批次台账。
        /// </summary>
        public RecordBatchDeleteResultDto BatchDeleteImportLogs(RecordBatchDeleteRequest request,
            string operatorName = null, string ip = null)
        {
            var ids = (request == null || request.Ids == null ? new List<int>() : request.Ids)
                .Distinct().Where(x => x > 0).ToList();
            if (ids.Count == 0)
            {
                throw ApiException.ValidationFailed("请选择要删除的导入批次记录");
            }

            int affected = WithTransaction((connection, transaction) =>
                _repo.SoftDeleteImportLogs(connection, transaction, ids));

            if (affected == 0)
            {
                throw ApiException.NotFound("所选导入批次记录不存在或已删除");
            }

            _audit.Write("IMPORT_LOG_DELETE", "import_log", string.Join(",", ids),
                "导入批次记录批量删除（软删，" + affected + " 条；记录留痕，可在「备份与恢复」页一键清理）",
                null, operatorName, ip, "基础信息", "成功");
            return new RecordBatchDeleteResultDto { Deleted = affected };
        }

        public List<ImportErrorItemDto> GetImportErrors(int importId) =>
            WithConnection(c => _repo.ListImportErrors(c, importId));

        public ExportLogDto GetExportLog(int id) =>
            WithConnection(c =>
            {
                var row = c.QueryFirstOrDefault<ExportLogDto>(
                    "SELECT id, module, format, file_path AS FilePath, created_at AS CreatedAt FROM t_export_log WHERE id = @id",
                    new { id });
                return row ?? throw ApiException.NotFound("导出记录不存在");
            });

        /// <summary>按导入批次错误行生成 Excel（行号/字段/值/原因/建议），作为错误清单下载。</summary>
        public byte[] BuildImportErrorsExcel(int importId)
        {
            var errors = GetImportErrors(importId);
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("错误清单");
                sheet.Cell(1, 1).Value = "行号";
                sheet.Cell(1, 2).Value = "字段";
                sheet.Cell(1, 3).Value = "单元格值";
                sheet.Cell(1, 4).Value = "错误原因";
                sheet.Cell(1, 5).Value = "处理建议";
                int r = 2;
                foreach (var e in errors)
                {
                    sheet.Cell(r, 1).Value = e.RowNo;
                    sheet.Cell(r, 2).Value = e.Field ?? string.Empty;
                    sheet.Cell(r, 3).Value = e.Content ?? string.Empty;
                    sheet.Cell(r, 4).Value = e.Reason ?? string.Empty;
                    sheet.Cell(r, 5).Value = e.Suggestion ?? string.Empty;
                    r++;
                }
                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        /// <summary>
        /// 导入回执（v1.2.0 CHG-v1.2.0-01）：逐行处理结果导出。
        /// 背景（负责人 2026-09-21）：重复数据改走「覆盖处理」后，同名业主被覆盖没有任何凭据，
        /// 后续查找同名条件变得困难；本回执逐行写明 新增 / 覆盖（覆盖了谁、改了哪几个字段、旧值→新值）/ 失败。
        /// </summary>
        public byte[] BuildImportReceiptExcel(int importId)
        {
            ImportLogDto log = GetImportLog(importId);
            List<ImportRowResultDto> rows = WithConnection(c => _repo.ListImportRows(c, importId));
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("导入回执");

                var title = sheet.Cell(1, 1);
                title.Value = "导入回执（批次 #" + log.Id + "）";
                title.Style.Font.Bold = true;
                title.Style.Font.FontSize = 13;

                sheet.Cell(2, 1).Value =
                    "数据类型：" + ModuleLabel(log.Module)
                    + "　文件：" + (log.FileName ?? string.Empty)
                    + "　总行数：" + log.Total + "　新增：" + log.Success + "　覆盖：" + log.Updated
                    + "　失败：" + log.Fail
                    + "　导入时间：" + log.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

                string[] headers = { "行号", "处理结果", "对象", "覆盖 / 变更明细", "失败原因", "处理建议" };
                const int headerRow = 4;
                for (int i = 0; i < headers.Length; i++)
                {
                    var head = sheet.Cell(headerRow, i + 1);
                    head.Value = headers[i];
                    head.Style.Font.Bold = true;
                    head.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F3F7");
                    head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                int r = headerRow + 1;
                foreach (ImportRowResultDto row in rows)
                {
                    sheet.Cell(r, 1).Value = row.RowNo;
                    sheet.Cell(r, 2).Value = ImportRowResultText(row.Result);
                    sheet.Cell(r, 3).Value = row.ObjectKey ?? string.Empty;
                    sheet.Cell(r, 4).Value = row.ChangeSummary ?? string.Empty;
                    sheet.Cell(r, 5).Value = row.Reason ?? string.Empty;
                    sheet.Cell(r, 6).Value = row.Suggestion ?? string.Empty;
                    if (row.Result == ImportRowResult.Updated)
                    {
                        // 覆盖行高亮：用户一眼能看出「这一批覆盖了哪些记录」
                        sheet.Range(r, 1, r, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF7E6");
                    }
                    else if (row.Result == ImportRowResult.Failed)
                    {
                        sheet.Range(r, 1, r, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDECEC");
                    }
                    r++;
                }

                if (rows.Count == 0)
                {
                    sheet.Cell(r, 1).Value = "—";
                    sheet.Cell(r, 2).Value =
                        log.Total > 0
                            ? "该批次导入于 v1.2.0 之前，未留存逐行回执（请重新导入以取得完整回执）"
                            : "本批次没有数据行（文件只有表头/示例行）";
                    r++;
                }

                sheet.Cell(r + 1, 1).Value =
                    "说明：1)「新增」= 新入库的记录；2)「覆盖」= 命中既有记录并按文件里填了值的字段更新（明细写明 字段：旧值 → 新值）；"
                    + "3)「失败」= 该行未入库，按「失败原因/处理建议」修正后可重传；4) 每行的覆盖/新增都会写入变更留痕（渠道：批量导入）。";
                sheet.Cell(r + 1, 1).Style.Alignment.WrapText = true;
                sheet.Range(r + 1, 1, r + 1, headers.Length).Merge();

                sheet.Column(1).Width = 8;
                sheet.Column(2).Width = 10;
                sheet.Column(3).Width = 34;
                sheet.Column(4).Width = 52;
                sheet.Column(5).Width = 38;
                sheet.Column(6).Width = 38;
                sheet.Range(headerRow + 1, 3, Math.Max(headerRow + 1, r), 6).Style.Alignment.WrapText = true;
                sheet.SheetView.FreezeRows(headerRow);

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        private static string ImportRowResultText(ImportRowResult result)
        {
            switch (result)
            {
                case ImportRowResult.Updated: return "覆盖";
                case ImportRowResult.Failed: return "失败";
                default: return "新增";
            }
        }

        public byte[] BuildTemplate(ImportModule module)
        {
            List<ImportField> fields = TemplateFields(module);
            using (var workbook = new XLWorkbook())
            {
                // v1.2.0（CHG-v1.2.0-05）：模板字体统一为「微软雅黑 11」。
                // 旧模板是 Calibri：中文字符靠字体回退渲染，在不同机器/不同列上字重与字宽不一致，
                // 现场反馈「业主那列字体太大」即由此而来；改为中文字体后全表口径一致。
                workbook.Style.Font.FontName = TemplateFontName;
                workbook.Style.Font.FontSize = TemplateFontSize;

                var sheet = workbook.Worksheets.Add("模板");
                for (int i = 0; i < fields.Count; i++)
                {
                    var head = sheet.Cell(1, i + 1);
                    head.Value = fields[i].Required ? fields[i].Header + "*" : fields[i].Header;
                    head.Style.Font.FontName = TemplateFontName;
                    head.Style.Font.FontSize = TemplateFontSize;
                    head.Style.Font.Bold = true;
                    head.Style.Fill.BackgroundColor = fields[i].Required
                        ? XLColor.FromHtml("#FDF0DC") : XLColor.FromHtml("#F1F3F7");
                    head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    head.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    // 列宽按表头字数自适应（旧口径一律 18 宽，「单元号」等短列过宽、「业主证件号」等长列过挤）
                    sheet.Column(i + 1).Width =
                        Math.Min(24, Math.Max(12, (fields[i].Header.Length + 1) * 2.8));
                    // 编号/证件号/电话/房号列按「文本」格式：从原始档案粘贴时不会被 Excel 转成科学计数法或丢前导零
                    if (IsTextColumn(fields[i].Header))
                        sheet.Column(i + 1).Style.NumberFormat.Format = "@";
                }

                // 示例行（v1.1.0 F-08b）：首列以「示例：」开头，导入时自动跳过（IsExampleRow）
                for (int i = 0; i < fields.Count; i++)
                {
                    var cell = sheet.Cell(2, i + 1);
                    cell.Value = i == 0 ? ("示例：" + fields[i].Example) : fields[i].Example;
                    cell.Style.Font.FontName = TemplateFontName;
                    cell.Style.Font.FontSize = TemplateExampleFontSize;   // 比表头小一号：示例不抢表头/正文的视觉
                    cell.Style.Font.Italic = true;
                    cell.Style.Font.FontColor = XLColor.FromHtml("#8A8F98");
                }
                // v1.2.0（CHG-v1.2.0-05）：预留数据区（第 3~302 行）预置模板字体，
                // 让「录入」或「选择性粘贴 → 值」进来的内容一律是微软雅黑 11，不再被源文件的大字号带跑。
                // 空白行导入时由 IsBlankRow 跳过，不影响 total 计数。
                for (int r = 3; r <= 302; r++)
                {
                    for (int i = 0; i < fields.Count; i++)
                    {
                        var cell = sheet.Cell(r, i + 1);
                        cell.Style.Font.FontName = TemplateFontName;
                        cell.Style.Font.FontSize = TemplateFontSize;
                    }
                }
                sheet.SheetView.FreezeRows(2);

                // 第 2 个 Sheet：填写说明（必填/示例/口径）
                var doc = workbook.Worksheets.Add("填写说明");
                doc.Cell(1, 1).Value = "字段";
                doc.Cell(1, 2).Value = "是否必填";
                doc.Cell(1, 3).Value = "示例";
                doc.Cell(1, 4).Value = "填写说明";
                for (int c = 1; c <= 4; c++)
                {
                    doc.Cell(1, c).Style.Font.Bold = true;
                    doc.Cell(1, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F3F7");
                }
                for (int i = 0; i < fields.Count; i++)
                {
                    doc.Cell(i + 2, 1).Value = fields[i].Header;
                    doc.Cell(i + 2, 2).Value = fields[i].Required ? "必填" : "选填";
                    doc.Cell(i + 2, 3).Value = fields[i].Example;
                    doc.Cell(i + 2, 4).Value = fields[i].Note;
                }
                doc.Column(1).Width = 16;
                doc.Column(2).Width = 10;
                doc.Column(3).Width = 22;
                doc.Column(4).Width = 62;
                doc.Cell(fields.Count + 3, 1).Value =
                    "说明：1) 「是否必填」为「必填」的列必须填写，其余留空即可；" +
                    "2) 「模板」工作表第 2 行为示例，导入时自动跳过，请从第 3 行起录入真实数据；" +
                    "3) 表头顺序可自行调整，导入按表头名识别列（旧版模板仍可继续使用）；" +
                    "4) 原始档案资料可直接整段复制粘贴：日期、数字、全角字符、千分位、空白行都能正常校验，" +
                    "其中「证件号 / 电话 / 编号 / 房号」列已预置为文本格式；" +
                    "5) 同名业主必须补充「业主证件号」或「业主电话」才能自动判定：" +
                    "与档案一致 → 判为同一人（档案缺该项时按本行值补齐档案）；" +
                    "与档案不一致 → 判为不同业主（「业主-房产关系」模板按本行信息新建档案并关联房产，「业主」模板新增档案）；" +
                    "同名且本行未补充信息时不猜，该行会失败并提示（也可先在「业主-房产关系」页面手动绑定）；" +
                    "6) 导入后可在「导入批次记录」下载回执，逐行查看 新增 / 覆盖（覆盖了谁、改了哪些字段）/ 失败；" +
                    "7) 整段复制原始档案时建议用「选择性粘贴 → 值」，数据区已按微软雅黑 11 预置，" +
                    "粘贴为值即可保持模板字体（直接粘贴会带入源文件字号）。";
                doc.Cell(fields.Count + 3, 1).Style.Alignment.WrapText = true;
                doc.Range(fields.Count + 3, 1, fields.Count + 3, 4).Merge();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        /// <summary>模板字段定义（v1.1.0）：**生成模板与解析共用同一份必填口径**，避免两处各写一套。</summary>
        private sealed class ImportField
        {
            public string Header { get; set; }
            public bool Required { get; set; }
            public string Example { get; set; }
            public string Note { get; set; }
        }

        private static ImportField F(string header, bool required, string example, string note)
        {
            return new ImportField { Header = header, Required = required, Example = example, Note = note };
        }

        /// <summary>模板字体口径（v1.2.0 CHG-v1.2.0-05）：中文界面统一微软雅黑，表头 11、示例行 10。</summary>
        private const string TemplateFontName = "微软雅黑";
        private const double TemplateFontSize = 11;
        private const double TemplateExampleFontSize = 10;

        /// <summary>按「文本」格式预置的列（v1.2.0）：长数字（证件号/电话/编号/房号）粘贴后不被 Excel 改写。</summary>
        private static bool IsTextColumn(string header)
        {
            if (string.IsNullOrEmpty(header)) return false;
            return header.Contains("证件号") || header.Contains("电话")
                || header.Contains("编号") || header.Contains("房号");
        }

        /// <summary>
        /// 各导入类型的字段清单（v1.1.0 必填矩阵，负责人 2026-09-16 确认）：
        /// 房产＝楼栋号/房号/建筑面积（单元号、用途选填；「用途」列于 v1.2.0 CHG-v1.2.0-03 恢复）；
        /// 车位＝车位编号；业主＝姓名（联系电话放开为选填）；业主-房产关系＝楼栋号/房号/业主姓名。
        /// </summary>
        private static List<ImportField> TemplateFields(ImportModule module)
        {
            switch (module)
            {
                case ImportModule.Property:
                    return new List<ImportField>
                    {
                        F("楼栋号", true, "1号楼", "必填。楼栋不存在时请先在「房产列表」新增楼栋"),
                        F("单元号", false, "", "选填。老旧小区无单元请留空；填写时须与楼栋下已维护的单元号一致"),
                        F("房号", true, "101", "必填。有单元时同单元内不得重复；无单元时同楼栋内不得重复"),
                        F("建筑面积", true, "138.66", "必填。正数，最多 2 位小数（系统按原值计算，不做四舍五入）"),
                        // v1.2.0（CHG-v1.2.0-03）：恢复「用途」列 —— 收费规格的「适用条件 → 房产用途」按该列判定，
                        // 房产列表已展示用途，模板却不给列 → 现场无法通过导入维护用途。选填，留空按「住宅」。
                        F("用途", false, "住宅", "选填。住宅 / 商铺 / 空置，留空按「住宅」"),
                        F("状态", false, "空置", "选填。空置 / 已入住 / 装修中，留空按「空置」")
                    };
                case ImportModule.Owner:
                    return new List<ImportField>
                    {
                        F("姓名", true, "张伟", "必填"),
                        F("证件类型", false, "身份证", "选填。身份证 / 护照 / 户口簿 / 其他，留空按「身份证」"),
                        F("证件号", false, "110101198501011234", "选填。同名业主必填其一：与档案一致 → 覆盖同一人；不一致 → 判为不同业主并新增档案"),
                        F("联系电话", false, "13800000001", "选填。同名业主必填其一：与档案一致 → 覆盖同一人；不一致 → 判为不同业主并新增档案"),
                        F("常住地址", false, "1号楼1单元101", "选填"),
                        F("紧急联系人", false, "李娜", "选填"),
                        F("紧急联系人电话", false, "13800000002", "选填"),
                        F("入住日期", false, "2026-01-01", "选填。格式 yyyy-MM-dd"),
                        F("状态", false, "在住", "选填。在住 / 搬离，留空按「在住」")
                    };
                case ImportModule.Parking:
                    return new List<ImportField>
                    {
                        F("车位编号", true, "B1-001", "必填。车位编号全局唯一"),
                        F("区域", false, "B1 层", "选填。车位所在区域/楼层"),
                        F("类型", false, "产权", "选填。产权 / 普通 / 临时，留空按「产权」"),
                        F("状态", false, "空置", "选填。已售 / 已租 / 空置 / 维修中，留空按「空置」"),
                        F("绑定楼栋号", false, "1号楼", "选填。需要绑定房产时，必须同时填写「绑定楼栋号 + 绑定房号」（单元号可留空）"),
                        F("绑定单元号", false, "", "选填。与绑定楼栋号/绑定房号配套使用"),
                        F("绑定房号", false, "101", "选填。需要绑定房产时，必须同时填写「绑定楼栋号 + 绑定房号」"),
                    };
                case ImportModule.OwnerRelation:
                    return new List<ImportField>
                    {
                        F("楼栋号", true, "1号楼", "必填"),
                        F("单元号", false, "", "选填。无单元请留空"),
                        F("房号", true, "101", "必填。与楼栋号（可选单元号）共同定位一条房产"),
                        F("业主姓名", true, "张伟", "必填。同名业主请补充「业主证件号」或「业主电话」"),
                        F("业主证件号", false, "110101198501011234", "选填。同名业主必填其一：与档案一致 → 关联同一人（档案缺该项时按本行值补齐）；不一致 → 按本行信息新建档案并关联"),
                        F("业主电话", false, "13800000001", "选填。同名业主必填其一：与档案一致 → 关联同一人（档案缺该项时按本行值补齐）；不一致 → 按本行信息新建档案并关联"),
                        F("关系类型", false, "业主", "选填。业主 / 共有人 / 租户备案，留空按「业主」"),
                        F("份额", false, "100", "选填。0~100，留空按类型默认（业主 100 / 共有人 50 / 租户备案 0）"),
                        F("起始日期", false, "2026-01-01", "选填。格式 yyyy-MM-dd，留空按当天"),
                        F("终止日期", false, "", "选填。格式 yyyy-MM-dd，留空为长期有效"),
                        F("状态", false, "有效", "选填。有效 / 即将到期 / 已解除，留空按终止日期自动判定")
                    };
                default:
                    throw ApiException.BadRequest("不支持的数据类型");
            }
        }

        // ===================== 表头驱动解析（v1.1.0 F-05~F-08） =====================

        /// <summary>读取第 1 行表头，建立「表头名 → 列号」映射（列序可变、旧模板兼容）。</summary>
        private static Dictionary<string, int> BuildColumnMap(IXLWorksheet sheet)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            int lastCol = 1;
            var last = sheet.LastColumnUsed();
            if (last != null) lastCol = Math.Max(1, last.ColumnNumber());
            for (int c = 1; c <= lastCol; c++)
            {
                string head = Cell(sheet, 1, c);
                if (head.Length == 0) continue;
                string key = NormalizeHeader(head);
                if (key.Length > 0 && !map.ContainsKey(key)) map[key] = c;
            }
            return map;
        }

        /// <summary>表头归一化：去空白/星号/√，去掉括号后缀（如「建筑面积（㎡，最多 2 位小数）」→「建筑面积」）。</summary>
        private static string NormalizeHeader(string header)
        {
            if (string.IsNullOrEmpty(header)) return string.Empty;
            var sb = new StringBuilder();
            foreach (char ch in header.Trim())
            {
                if (char.IsWhiteSpace(ch) || ch == '*' || ch == '＊' || ch == '√') continue;
                if (ch == '（' || ch == '(' || ch == '【') break;
                if (ch == '】' || ch == '）' || ch == ')') continue;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>按别名取列号（-1 表示模板中不存在该列）。</summary>
        private static int ColIndex(Dictionary<string, int> map, params string[] names)
        {
            foreach (string n in names)
            {
                int idx;
                if (map.TryGetValue(NormalizeHeader(n), out idx)) return idx;
            }
            return -1;
        }

        private static string CellAt(IXLWorksheet sheet, int row, int col)
        {
            return col < 1 ? string.Empty : Cell(sheet, row, col);
        }

        /// <summary>
        /// 整行是否为空（v1.2.0 CHG-v1.2.0-04）。
        /// 用户常把原始档案整段复制进模板，末尾会带上「只有格式没有内容」的行；
        /// 这类行不应被当成数据行报「必填项不能为空」。
        /// </summary>
        private static bool IsBlankRow(params string[] cells)
        {
            foreach (string text in cells)
            {
                if (!string.IsNullOrWhiteSpace(text)) return false;
            }
            return true;
        }

        /// <summary>回执「对象」列文案：房产/关系用 楼栋+单元+房号，业主/车位直接用业务主键。</summary>
        private static string KeyLabel(string buildingNo, string unitNo, string roomNo)
        {
            string building = (buildingNo ?? string.Empty).Trim();
            string unit = (unitNo ?? string.Empty).Trim();
            string room = (roomNo ?? string.Empty).Trim();
            string label = building + unit + room;
            return label.Length == 0 ? "（未填写楼栋/房号）" : label;
        }

        /// <summary>
        /// 业主「常住地址」是否与本行「楼栋[+单元]+房号」指向同一处房产（v1.2.0 第 2 轮）。
        /// 用于业主-房产关系导入时区分同名业主：地址如「1号楼1单元101」与楼栋「1号楼」房号「101」视为同一处。
        /// </summary>
        private static bool AddressMatchesProperty(string address, string buildingNo, string unitNo, string roomNo)
        {
            string a = Compact(address);
            string building = Compact(buildingNo);
            string unit = Compact(unitNo);
            string room = Compact(roomNo);
            if (a.Length == 0 || building.Length == 0 || room.Length == 0) return false;
            if (IndexOfToken(a, building) < 0) return false;
            if (IndexOfToken(a, room) < 0) return false;
            return unit.Length == 0 || IndexOfToken(a, unit) >= 0;
        }

        /// <summary>去空白、全角空格（地址比对用）。</summary>
        private static string Compact(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch) || ch == '\u3000' || ch == '\u00A0') continue;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 按「数字边界」查找短串：避免「11号楼」被「1号楼」命中、「1101」被「101」命中。
        /// 左侧为数字一律不算命中；右侧仅当**查找串以数字结尾**时才校验（如「1号楼908」中的「1号楼」后面紧跟房号数字，属正常）。
        /// </summary>
        private static int IndexOfToken(string text, string token)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token) || token.Length > text.Length) return -1;
            bool checkRight = char.IsDigit(token[token.Length - 1]);
            int from = 0;
            while (from <= text.Length - token.Length)
            {
                int idx = text.IndexOf(token, from, StringComparison.Ordinal);
                if (idx < 0) return -1;
                int end = idx + token.Length;
                bool leftOk = idx == 0 || !char.IsDigit(text[idx - 1]);
                bool rightOk = !checkRight || end >= text.Length || !char.IsDigit(text[end]);
                if (leftOk && rightOk) return idx;
                from = idx + 1;
            }
            return -1;
        }

        /// <summary>模板示例行判定：首列以「示例」开头（v1.1.0 F-08b 约定，导入时自动跳过）。</summary>
        private static bool IsExampleRow(IXLWorksheet sheet, int row, int firstCol)
        {
            string first = CellAt(sheet, row, firstCol);
            return first.StartsWith("示例", StringComparison.Ordinal);
        }

        /// <summary>统计有效数据行（不含示例行与空行），用于导入批次 total。</summary>
        private static int CountDataRows(IXLWorksheet sheet, int firstCol, int lastCol)
        {
            int count = 0;
            var lastRow = sheet.LastRowUsed();
            if (lastRow == null) return 0;
            for (int row = 2; row <= lastRow.RowNumber(); row++)
            {
                if (IsExampleRow(sheet, row, firstCol)) continue;
                bool any = false;
                for (int c = Math.Max(1, firstCol); c <= lastCol; c++)
                {
                    if (CellAt(sheet, row, c).Length > 0) { any = true; break; }
                }
                if (any) count++;
            }
            return count;
        }

        /// <summary>校验关键表头是否齐备（缺失即提示重新下载模板，避免把「用途」等错列读成业务字段）。</summary>
        private static void RequireHeaders(Dictionary<string, int> map, ImportModule module, params string[] requiredKeys)
        {
            var missing = requiredKeys.Where(k => ColIndex(map, k) < 0).ToList();
            if (missing.Count == 0) return;
            throw ApiException.BadRequest("模板表头与所选数据类型不匹配，缺少列：" + string.Join("、", missing)
                + "。请重新下载「" + ModuleLabel(module) + "」模板后填写。");
        }

        private static string ModuleLabel(ImportModule module)
        {
            switch (module)
            {
                case ImportModule.Owner: return "业主";
                case ImportModule.Parking: return "车位";
                case ImportModule.OwnerRelation: return "业主-房产关系";
                default: return "房产";
            }
        }

        /// <summary>
        /// 把导入期异常翻译为**中文业务提示**（不把 SQLite/.NET 英文原文暴露给用户；原始异常已写 NLog）。
        /// v1.1.0 第 3 轮：现场反馈「业主-房产关系导入失败显示英文提示」即原始 SQLite 唯一约束消息外泄。
        /// </summary>
        private static string DescribeImportFailure(Exception ex)
        {
            string msg = ex == null ? string.Empty : (ex.Message ?? string.Empty);
            string typeName = ex == null ? string.Empty : (ex.GetType().FullName ?? string.Empty);
            // 业务校验异常（如「模板表头与所选数据类型不匹配，缺少列：…」）本身就是中文可读提示，原样保留
            if (ex is ApiException) return msg;
            if (typeName.IndexOf("ClosedXML", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Packaging", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ex is InvalidDataException)
                return "文件无法解析：该文件不是有效的 Excel（.xlsx）文件，请使用系统下载的模板填写后重试";
            if (msg.IndexOf("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) >= 0)
                return "数据重复：文件中的记录与库内已有数据冲突（同一房产/业主/日期等唯一键重复），请检查是否重复导入或数据已存在";
            if (ex is System.Data.SQLite.SQLiteException)
                return "数据写入失败：文件数据与数据库约束不符，请检查数字列与日期列格式是否正确";
            if (ex is IOException || ex is UnauthorizedAccessException)
                return "文件读写失败：请确认文件未被 Excel/WPS 占用，或改存到有写入权限的目录后重试";
            if (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
                return "文件无法解析或单元格格式不正确：请使用系统下载的模板（.xlsx）填写，数字列填数值、日期列填 yyyy-MM-dd";
            return "导入失败：文件无法解析或数据处理异常，请核对模板格式后重试";
        }

        // ============================ 私有辅助 ============================
        /// <summary>
        /// 导入计数（CHG-v1.1.2-01）：Inserted = 新增行数，Updated = 覆盖行数。
        /// 命中既有记录时不再报错，改为按「只覆盖文件里填了值的字段」的口径更新。
        /// </summary>
        private class ImportCount
        {
            public int Inserted;
            public int Updated;

            /// <summary>
            /// 逐行回执（v1.2.0 CHG-v1.2.0-01）：每一行数据行都留一条「新增/覆盖/失败」记录，
            /// 导入完成后落库 t_import_row，批次列表「下载回执」导出 Excel。
            /// </summary>
            public readonly List<ImportRowResultDto> Rows = new List<ImportRowResultDto>();

            /// <param name="note">可选提示（回执「处理建议」列）。</param>
            public void AddInserted(int rowNo, string objectKey, string note = null)
            {
                Inserted++;
                Rows.Add(new ImportRowResultDto
                {
                    RowNo = rowNo, Result = ImportRowResult.Inserted, ObjectKey = objectKey, Suggestion = note
                });
            }

            public void AddUpdated(int rowNo, string objectKey, IList<string> changes, string note = null)
            {
                Updated++;
                Rows.Add(new ImportRowResultDto
                {
                    RowNo = rowNo,
                    Result = ImportRowResult.Updated,
                    ObjectKey = objectKey,
                    Suggestion = note,
                    ChangeSummary = changes == null || changes.Count == 0
                        ? "覆盖：文件内容与库内一致，未产生字段变化"
                        : "覆盖：" + string.Join("；", changes)
                });
            }

            public void AddFailed(int rowNo, string objectKey, IEnumerable<ImportErrorItemDto> rowErrors)
            {
                var list = (rowErrors ?? Enumerable.Empty<ImportErrorItemDto>()).ToList();
                Rows.Add(new ImportRowResultDto
                {
                    RowNo = rowNo,
                    Result = ImportRowResult.Failed,
                    ObjectKey = objectKey,
                    Reason = string.Join("；", list.Select(e => e.Reason)
                        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
                    Suggestion = string.Join("；", list.Select(e => e.Suggestion)
                        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
                });
            }
        }

        /// <summary>回执「对象」列：业主用 姓名 +（档案号／证件号／电话），解决同名业主无法分辨的问题。</summary>
        private static string OwnerKeyLabel(string name, int? ownerId, string idCard, string phone)
        {
            var parts = new List<string>();
            if (ownerId.HasValue && ownerId.Value > 0) parts.Add("档案 #" + ownerId.Value);
            if (!string.IsNullOrWhiteSpace(idCard)) parts.Add("证件号 " + idCard.Trim());
            if (!string.IsNullOrWhiteSpace(phone)) parts.Add("电话 " + phone.Trim());
            return (name ?? string.Empty).Trim() + (parts.Count == 0 ? string.Empty : "（" + string.Join("，", parts) + "）");
        }

        private ImportCount ImportProperties(IDbConnection c, IDbTransaction tx, IXLWorksheet sheet, List<ImportErrorItemDto> errors)
        {
            Dictionary<string, int> map = BuildColumnMap(sheet);
            RequireHeaders(map, ImportModule.Property, "楼栋号", "房号", "建筑面积");
            int colBuilding = ColIndex(map, "楼栋号", "楼栋");
            int colUnit = ColIndex(map, "单元号", "单元");
            int colRoom = ColIndex(map, "房号");
            int colArea = ColIndex(map, "建筑面积", "面积");
            int colUsage = ColIndex(map, "用途");           // v1.2.0（CHG-v1.2.0-03）：模板恢复「用途」列（选填）
            int colStatus = ColIndex(map, "状态");

            var count = new ImportCount();
            int row = 2;
            int lastRow = sheet.LastRowUsed().RowNumber();
            while (row <= lastRow)
            {
                if (IsExampleRow(sheet, row, colBuilding)) { row++; continue; }
                string buildingNo = CellAt(sheet, row, colBuilding);
                string unitNo = CellAt(sheet, row, colUnit);       // v1.1.0 F-05：单元号选填（老旧小区无单元）
                string roomNo = CellAt(sheet, row, colRoom);
                string areaText = CellAt(sheet, row, colArea);
                string usageText = CellAt(sheet, row, colUsage);
                string statusText = CellAt(sheet, row, colStatus);
                // v1.2.0（CHG-v1.2.0-04）：模板预留的空白行（只有样式没有内容）直接跳过，不报「楼栋号不能为空」
                if (IsBlankRow(buildingNo, unitNo, roomNo, areaText, usageText, statusText)) { row++; continue; }
                string keyLabel = KeyLabel(buildingNo, unitNo, roomNo);

                var rowErrors = new List<ImportErrorItemDto>();
                if (string.IsNullOrWhiteSpace(buildingNo)) rowErrors.Add(Err(row, "楼栋号", buildingNo, "楼栋号不能为空", "填写正确的楼栋号"));
                if (string.IsNullOrWhiteSpace(roomNo)) rowErrors.Add(Err(row, "房号", roomNo, "房号不能为空", "填写正确的房号"));
                decimal area;
                if (!AreaValue.TryParseLoose(areaText, out area) || area <= 0)
                    rowErrors.Add(Err(row, "建筑面积", areaText, "建筑面积必须为正数", "填写数字，如 88.5"));
                PropertyUsage propUsage = PropertyUsage.Residential;
                if (!string.IsNullOrWhiteSpace(usageText) && !TryParseLabeled(usageText, PropertyUsageMap, out propUsage))
                    rowErrors.Add(Err(row, "用途", usageText, "用途不合法（住宅/商铺/空置）", "填写 住宅/商铺/空置，或留空按住宅"));
                PropertyStatus propStatus = PropertyStatus.Vacant;
                if (!string.IsNullOrWhiteSpace(statusText) && !TryParseLabeled(statusText, PropertyStatusMap, out propStatus))
                    rowErrors.Add(Err(row, "状态", statusText, "状态不合法（空置/已入住/装修中）", "填写 空置/已入住/装修中"));

                if (rowErrors.Count == 0)
                {
                    buildingNo = buildingNo.Trim(); roomNo = roomNo.Trim();
                    bool hasUnit = !string.IsNullOrWhiteSpace(unitNo);
                    unitNo = hasUnit ? unitNo.Trim() : string.Empty;
                    int? buildingId = ResolveBuildingId(c, tx, buildingNo);
                    int? unitId = null;
                    if (!buildingId.HasValue)
                        rowErrors.Add(Err(row, "楼栋号", buildingNo, "找不到该楼栋", "请先在「房产列表」维护楼栋"));
                    else if (hasUnit)
                    {
                        unitId = ResolveUnit(c, tx, buildingNo, unitNo);
                        if (!unitId.HasValue)
                            rowErrors.Add(Err(row, "单元号", unitNo, "该楼栋下找不到此单元", "留空表示无单元，或核对该楼栋已维护的单元号"));
                    }
                    if (rowErrors.Count == 0)
                    {
                        // BR-INF-01：有单元按「单元+房号」判重；无单元按「楼栋+房号」判重（与表单口径一致）
                        // v1.1.2 I-01（负责人 2026-09-19 裁定 A）：命中既有房产不再报错 → 覆盖处理。
                        // 覆盖口径：只覆盖文件里填了值的字段（建筑面积必填 → 必覆盖；状态留空 → 保留库内原值），
                        // 并写 t_base_change_log 留痕，便于追溯「哪一次导入改了什么」。
                        PropertyDto dup = unitId.HasValue
                            ? _repo.GetPropertyByUnitRoom(c, tx, unitId.Value, roomNo, 0)
                            : _repo.GetPropertyByBuildingRoom(c, tx, buildingId.Value, roomNo, 0);
                        if (dup != null)
                        {
                            PropertyDto before = _repo.GetProperty(c, dup.Id);
                            if (before == null)
                            {
                                // 并发下既有房产已被删除：按新增处理，避免空引用
                                _repo.InsertProperty(c, tx, new PropertyDto
                                {
                                    BuildingId = buildingId.Value, UnitId = unitId, RoomNo = roomNo,
                                    Area = area,
                                    Usage = string.IsNullOrWhiteSpace(usageText) ? PropertyUsage.Residential : propUsage,
                                    Status = propStatus
                                });
                                count.AddInserted(row, keyLabel);
                                errors.AddRange(rowErrors);
                                row++;
                                continue;
                            }
                            PropertyStatus newStatus = string.IsNullOrWhiteSpace(statusText) ? before.Status : propStatus;
                            PropertyUsage newUsage = string.IsNullOrWhiteSpace(usageText) ? before.Usage : propUsage;
                            var changes = new List<string>();
                            _repo.UpdateProperty(c, tx, new PropertyDto
                            {
                                Id = before.Id,
                                BuildingId = buildingId.Value,
                                UnitId = unitId,
                                RoomNo = roomNo,
                                Area = area,
                                Usage = newUsage,       // 模板「用途」列留空 → 保留库内用途，不因导入被改写
                                Status = newStatus
                            });
                            if (before.Area != area)
                            {
                                WriteChangeLog(c, tx, BaseChangeObjectType.Property, before.Id, "建筑面积",
                                    before.Area.ToString("0.####"), area.ToString("0.####"), "批量导入");
                                changes.Add("建筑面积：" + AreaValue.Format(before.Area) + " → " + AreaValue.Format(area));
                            }
                            if (before.Usage != newUsage)
                            {
                                WriteChangeLog(c, tx, BaseChangeObjectType.Property, before.Id, "用途",
                                    UsageText(before.Usage), UsageText(newUsage), "批量导入");
                                changes.Add("用途：" + UsageText(before.Usage) + " → " + UsageText(newUsage));
                            }
                            if (before.Status != newStatus)
                            {
                                WriteChangeLog(c, tx, BaseChangeObjectType.Property, before.Id, "状态",
                                    StatusText(before.Status), StatusText(newStatus), "批量导入");
                                changes.Add("状态：" + StatusText(before.Status) + " → " + StatusText(newStatus));
                            }
                            count.AddUpdated(row, keyLabel + "（房产 #" + before.Id + "）", changes);
                        }
                        else
                        {
                            _repo.InsertProperty(c, tx, new PropertyDto
                            {
                                BuildingId = buildingId.Value,
                                UnitId = unitId,
                                RoomNo = roomNo,
                                Area = area,
                                Usage = string.IsNullOrWhiteSpace(usageText) ? PropertyUsage.Residential : propUsage,
                                Status = propStatus
                            });
                            count.AddInserted(row, keyLabel);
                        }
                    }
                }
                if (rowErrors.Count > 0) count.AddFailed(row, keyLabel, rowErrors);
                errors.AddRange(rowErrors);
                row++;
            }
            return count;
        }

        private ImportCount ImportOwners(IDbConnection c, IDbTransaction tx, IXLWorksheet sheet, List<ImportErrorItemDto> errors)
        {
            Dictionary<string, int> map = BuildColumnMap(sheet);
            RequireHeaders(map, ImportModule.Owner, "姓名");
            int colName = ColIndex(map, "姓名");
            int colType = ColIndex(map, "证件类型");
            int colIdCard = ColIndex(map, "证件号");
            int colPhone = ColIndex(map, "联系电话", "电话", "手机", "手机号");
            int colAddress = ColIndex(map, "常住地址", "地址");
            int colEmergency = ColIndex(map, "紧急联系人");
            int colEmergencyPhone = ColIndex(map, "紧急联系人电话");
            int colCheckIn = ColIndex(map, "入住日期");
            int colStatus = ColIndex(map, "状态");

            var count = new ImportCount();
            int row = 2;
            int lastRow = sheet.LastRowUsed().RowNumber();
            while (row <= lastRow)
            {
                if (IsExampleRow(sheet, row, colName)) { row++; continue; }
                string name = CellAt(sheet, row, colName);
                string idType = CellAt(sheet, row, colType);
                string idCard = CellAt(sheet, row, colIdCard);
                string phone = CellAt(sheet, row, colPhone);
                string address = CellAt(sheet, row, colAddress);
                string emergency = CellAt(sheet, row, colEmergency);
                string emergencyPhone = CellAt(sheet, row, colEmergencyPhone);
                string checkIn = CellAt(sheet, row, colCheckIn);
                string statusText = CellAt(sheet, row, colStatus);
                // v1.2.0（CHG-v1.2.0-04）：模板预留空白行跳过
                if (IsBlankRow(name, idType, idCard, phone, address, emergency, emergencyPhone, checkIn, statusText))
                { row++; continue; }
                string ownerKey = OwnerKeyLabel(name, null, idCard, phone);

                var rowErrors = new List<ImportErrorItemDto>();
                if (string.IsNullOrWhiteSpace(name)) rowErrors.Add(Err(row, "姓名", name, "姓名不能为空", "填写姓名"));
                // v1.1.0 F-07：联系电话放开为选填（负责人 2026-09-16 确认，与业主档案口径统一）
                OwnerIdCardType idCardType = OwnerIdCardType.IdCard;
                if (!string.IsNullOrWhiteSpace(idType) && !TryParseLabeled(idType, IdCardTypeMap, out idCardType))
                    rowErrors.Add(Err(row, "证件类型", idType, "证件类型不合法（身份证/护照/户口簿/其他）", "填写 身份证/护照/户口簿/其他"));
                OwnerStatus ownerStatus = OwnerStatus.Living;
                if (!string.IsNullOrWhiteSpace(statusText) && !TryParseLabeled(statusText, OwnerStatusMap, out ownerStatus))
                    rowErrors.Add(Err(row, "状态", statusText, "状态不合法（在住/搬离）", "填写 在住/搬离"));

                if (rowErrors.Count == 0)
                {
                    string n = name.Trim();
                    string ph = string.IsNullOrWhiteSpace(phone) ? string.Empty : phone.Trim();
                    string ic = string.IsNullOrWhiteSpace(idCard) ? string.Empty : idCard.Trim();
                    // 查重口径（v1.1.0 起，v1.2.0 CHG-v1.2.0-02 放宽）：证件号 → 电话 → 姓名 +「补充信息是否为空」。
                    // v1.1.2 I-01（负责人 2026-09-19 裁定 A）：命中唯一既有业主 → 覆盖处理，不再报「该业主已存在」。
                    // v1.2.0（负责人 2026-09-21 反馈「同名判定过严」）：同名多条时，
                    //   若只有一名同名业主既没证件号也没电话，而本行也没填 → 判定为该名（两者只是名字重复，其它信息不重复）。
                    OwnerMatch match = MatchOwnerForImport(c, tx, n, ic, ph);
                    if (match.Ambiguous)
                    {
                        rowErrors.Add(Err(row, "姓名", n, match.Reason, match.Suggestion));
                    }
                    else if (match.OwnerId.HasValue)
                    {
                        OwnerDto before = LoadOwner(c, tx, match.OwnerId.Value);
                        if (before == null)
                        {
                            rowErrors.Add(Err(row, "姓名", n, "该业主已被删除，请重新核对业主档案", "先在「业主档案」确认后再导入"));
                        }
                        else
                        {
                            var after = new OwnerDto
                            {
                                Id = before.Id,
                                Name = n,
                                // 只覆盖文件里填了值的字段：空单元格保留库内原值
                                IdCardType = string.IsNullOrWhiteSpace(idType) ? before.IdCardType : idCardType,
                                IdCard = ic.Length > 0 ? ic : before.IdCard,
                                Phone = ph.Length > 0 ? ph : before.Phone,
                                ResidentAddress = string.IsNullOrWhiteSpace(address) ? before.ResidentAddress : address,
                                EmergencyContactName = string.IsNullOrWhiteSpace(emergency) ? before.EmergencyContactName : emergency,
                                EmergencyContactPhone = string.IsNullOrWhiteSpace(emergencyPhone) ? before.EmergencyContactPhone : emergencyPhone,
                                CheckInDate = string.IsNullOrWhiteSpace(checkIn) ? before.CheckInDate : ParseDate(checkIn),
                                Status = string.IsNullOrWhiteSpace(statusText) ? before.Status : ownerStatus
                            };
                            _repo.UpdateOwner(c, tx, after);
                            List<string> changes = LogOwnerChanges(c, tx, before, after, "批量导入");
                            // 证件号/证件类型是「同名业主区分」的关键字段：导入覆盖时单独留痕并写进回执
                            // （档案页维护沿用原有留痕口径，不在此改动）
                            if (!Equals(before.IdCard, after.IdCard))
                            {
                                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, after.Id, "证件号",
                                    before.IdCard, after.IdCard, "批量导入");
                                changes.Add("证件号：" + Show(before.IdCard) + " → " + Show(after.IdCard));
                            }
                            if (before.IdCardType != after.IdCardType)
                            {
                                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, after.Id, "证件类型",
                                    IdCardTypeName(before.IdCardType), IdCardTypeName(after.IdCardType), "批量导入");
                                changes.Add("证件类型：" + IdCardTypeName(before.IdCardType) + " → " + IdCardTypeName(after.IdCardType));
                            }
                            if (!Equals(before.CheckInDate, after.CheckInDate))
                            {
                                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, after.Id, "入住日期",
                                    before.CheckInDate.HasValue ? before.CheckInDate.Value.ToString("yyyy-MM-dd") : string.Empty,
                                    after.CheckInDate.HasValue ? after.CheckInDate.Value.ToString("yyyy-MM-dd") : string.Empty,
                                    "批量导入");
                                changes.Add("入住日期：" + (before.CheckInDate.HasValue ? before.CheckInDate.Value.ToString("yyyy-MM-dd") : "（空）")
                                    + " → " + (after.CheckInDate.HasValue ? after.CheckInDate.Value.ToString("yyyy-MM-dd") : "（空）"));
                            }
                            if (before.Status != after.Status)
                            {
                                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, after.Id, "状态",
                                    before.Status == OwnerStatus.Living ? "在住" : "搬离",
                                    after.Status == OwnerStatus.Living ? "在住" : "搬离", "批量导入");
                                changes.Add("状态：" + (before.Status == OwnerStatus.Living ? "在住" : "搬离")
                                    + " → " + (after.Status == OwnerStatus.Living ? "在住" : "搬离"));
                            }
                            count.AddUpdated(row, OwnerKeyLabel(before.Name, before.Id, before.IdCard, before.Phone), changes);
                        }
                    }
                    else
                    {
                        _repo.InsertOwner(c, tx, new OwnerDto
                        {
                            Name = n,
                            IdCardType = idCardType,
                            IdCard = string.IsNullOrWhiteSpace(ic) ? null : ic,
                            Phone = string.IsNullOrEmpty(ph) ? null : ph,
                            ResidentAddress = address,
                            EmergencyContactName = emergency,
                            EmergencyContactPhone = emergencyPhone,
                            CheckInDate = ParseDate(checkIn),
                            Status = ownerStatus
                        });
                        count.AddInserted(row, ownerKey);
                    }
                }
                if (rowErrors.Count > 0) count.AddFailed(row, ownerKey, rowErrors);
                errors.AddRange(rowErrors);
                row++;
            }
            return count;
        }

        private ImportCount ImportParkings(IDbConnection c, IDbTransaction tx, IXLWorksheet sheet, List<ImportErrorItemDto> errors)
        {
            Dictionary<string, int> map = BuildColumnMap(sheet);
            RequireHeaders(map, ImportModule.Parking, "车位编号");
            int colSpaceNo = ColIndex(map, "车位编号", "编号");
            int colArea = ColIndex(map, "区域");
            int colType = ColIndex(map, "类型");
            int colStatus = ColIndex(map, "状态");
            int colBindBuilding = ColIndex(map, "绑定楼栋号", "楼栋号");
            int colBindUnit = ColIndex(map, "绑定单元号", "单元号");
            int colBindRoom = ColIndex(map, "绑定房号", "房号");

            var count = new ImportCount();
            int row = 2;
            int lastRow = sheet.LastRowUsed().RowNumber();
            while (row <= lastRow)
            {
                if (IsExampleRow(sheet, row, colSpaceNo)) { row++; continue; }
                string spaceNo = CellAt(sheet, row, colSpaceNo);
                string area = CellAt(sheet, row, colArea);
                string typeText = CellAt(sheet, row, colType);
                string statusText = CellAt(sheet, row, colStatus);
                string bindBuildingNo = CellAt(sheet, row, colBindBuilding);
                string bindUnitNo = CellAt(sheet, row, colBindUnit);
                string bindRoomNo = CellAt(sheet, row, colBindRoom);
                // v1.2.0（CHG-v1.2.0-04）：模板预留空白行跳过
                if (IsBlankRow(spaceNo, area, typeText, statusText, bindBuildingNo, bindUnitNo, bindRoomNo))
                { row++; continue; }
                string spaceKey = string.IsNullOrWhiteSpace(spaceNo) ? "（未填写车位编号）" : spaceNo.Trim();

                var rowErrors = new List<ImportErrorItemDto>();
                if (string.IsNullOrWhiteSpace(spaceNo)) rowErrors.Add(Err(row, "车位编号", spaceNo, "车位编号不能为空", "填写车位编号"));

                bool hasTypeText = !string.IsNullOrWhiteSpace(typeText);
                ParkingSpaceType spaceType = ParkingSpaceType.PropertyRight;
                if (hasTypeText && !TryParseLabeled(typeText, ParkingTypeMap, out spaceType))
                    rowErrors.Add(Err(row, "类型", typeText, "类型不合法（产权/普通/临时）", "填写 产权/普通/临时"));
                bool hasStatusText = !string.IsNullOrWhiteSpace(statusText);
                ParkingSpaceStatus status = ParkingSpaceStatus.Vacant;
                if (hasStatusText && !TryParseLabeled(statusText, ParkingStatusMap, out status))
                    rowErrors.Add(Err(row, "状态", statusText, "状态不合法（已售/已租/空置/维修中）", "填写 已售/已租/空置/维修中"));
                if (spaceType == ParkingSpaceType.CivilDefense && status == ParkingSpaceStatus.Owned)
                        rowErrors.Add(Err(row, "类型/状态", typeText + "-" + statusText, "普通车位不可标为出售", "改为已租/空置"));

                if (rowErrors.Count == 0)
                {
                    string key = spaceNo.Trim();
                    // v1.1.2 I-01（负责人 2026-09-19 裁定 A）：车位编号命中既有记录 → 覆盖处理，不再报错。
                    // 覆盖口径：只覆盖文件里填了值的字段；「绑定房产」三列整体留空时保留库内既有绑定。
                    // 另：v1.1.0 起车位表单已下线「租金/租期至」，模板同步移除该两列，导入不再改写库内租金与租期。
                    ParkingSpaceDto duplicate = _repo.GetParkingByNo(c, key, 0);
                    ParkingSpaceDto before = duplicate == null ? null : LoadParking(c, tx, duplicate.Id);
                    if (before != null)
                    {
                        ParkingSpaceType newType = hasTypeText ? spaceType : before.SpaceType;
                        ParkingSpaceStatus newStatus = hasStatusText ? status : before.Status;
                        int? propertyId = null;
                        bool hasBind = !string.IsNullOrWhiteSpace(bindBuildingNo) || !string.IsNullOrWhiteSpace(bindUnitNo) || !string.IsNullOrWhiteSpace(bindRoomNo);
                        if (hasBind)
                        {
                            // v1.1.0 F-06：绑定房产只需「楼栋号 + 房号」，单元号可留空（老旧小区无单元）
                            if (string.IsNullOrWhiteSpace(bindBuildingNo) || string.IsNullOrWhiteSpace(bindRoomNo))
                                rowErrors.Add(Err(row, "绑定房产", bindBuildingNo + "-" + bindRoomNo, "绑定房产需同时填写「绑定楼栋号 + 绑定房号」", "补填楼栋号与房号（单元号可留空）"));
                            else
                            {
                                string bindLabel = bindBuildingNo.Trim() + (string.IsNullOrWhiteSpace(bindUnitNo) ? string.Empty : bindUnitNo.Trim()) + bindRoomNo.Trim();
                                propertyId = ResolvePropertyByKeyLoose(c, tx, bindBuildingNo.Trim(), bindUnitNo, bindRoomNo.Trim());
                                if (!propertyId.HasValue)
                                    rowErrors.Add(Err(row, "绑定房产", bindLabel, "找不到唯一对应的房产", "核对该房产是否存在；若同楼栋存在多个同名房号，请补填单元号"));
                            }
                        }
                        if (rowErrors.Count == 0)
                        {
                            int? effPropertyId = hasBind ? propertyId : before.PropertyId;
                            int? effOwnerId = hasBind
                                ? (effPropertyId.HasValue ? ResolveOwnerIdByProperty(c, tx, effPropertyId.Value) : null)
                                : before.OwnerId;
                            if (newType == ParkingSpaceType.PropertyRight && effPropertyId.HasValue &&
                                _repo.CountParkingsBoundToProperty(c, tx, effPropertyId.Value, before.Id, (int)ParkingSpaceType.PropertyRight) > 0)
                            rowErrors.Add(Err(row, "绑定房产", bindBuildingNo + "-" + bindUnitNo + "-" + bindRoomNo, "该房产已绑定一个产权车位", "更换房产"));
                            if (rowErrors.Count == 0)
                            {
                                var changes = new List<string>();
                                _repo.UpdateParking(c, tx, new ParkingSpaceDto
                                {
                                    Id = before.Id,
                                    SpaceNo = key,
                                    Area = string.IsNullOrWhiteSpace(area) ? before.Area : area,
                                    SpaceType = newType,
                                    Status = newStatus,
                                    PropertyId = effPropertyId,
                                    OwnerId = effOwnerId,
                                    MonthlyRent = before.MonthlyRent,   // 模板已下线「租金」列 → 保留库内原值
                                    RentMode = before.RentMode,
                                    RentTo = before.RentTo               // 模板已下线「租期至」列 → 保留库内原值
                                });
                                if (before.Status != newStatus)
                                {
                                    WriteChangeLog(c, tx, BaseChangeObjectType.Parking, before.Id, "状态",
                                        ParkingStatusLabel(before.Status), ParkingStatusLabel(newStatus), "批量导入");
                                    changes.Add("状态：" + ParkingStatusLabel(before.Status) + " → " + ParkingStatusLabel(newStatus));
                                }
                                if (before.SpaceType != newType)
                                {
                                    WriteChangeLog(c, tx, BaseChangeObjectType.Parking, before.Id, "类型",
                                        ParkingTypeLabel(before.SpaceType), ParkingTypeLabel(newType), "批量导入");
                                    changes.Add("类型：" + ParkingTypeLabel(before.SpaceType) + " → " + ParkingTypeLabel(newType));
                                }
                                if (!Equals(before.Area, area) && !string.IsNullOrWhiteSpace(area))
                                {
                                    WriteChangeLog(c, tx, BaseChangeObjectType.Parking, before.Id, "区域", before.Area, area, "批量导入");
                                    changes.Add("区域：" + Show(before.Area) + " → " + Show(area));
                                }
                                if (before.PropertyId != effPropertyId)
                                {
                                    WriteChangeLog(c, tx, BaseChangeObjectType.Parking, before.Id, "绑定房产",
                                        before.PropertyId.HasValue ? before.PropertyId.Value.ToString() : string.Empty,
                                        effPropertyId.HasValue ? effPropertyId.Value.ToString() : string.Empty, "批量导入");
                                    changes.Add("绑定房产：" + (before.PropertyId.HasValue ? "#" + before.PropertyId.Value : "（未绑定）")
                                        + " → " + (effPropertyId.HasValue ? "#" + effPropertyId.Value : "（未绑定）"));
                                }
                                count.AddUpdated(row, spaceKey + "（车位 #" + before.Id + "）", changes);
                            }
                        }
                    }
                    else
                    {
                        int? propertyId = null;
                        int? ownerId = null;
                        bool hasBind = !string.IsNullOrWhiteSpace(bindBuildingNo) || !string.IsNullOrWhiteSpace(bindUnitNo) || !string.IsNullOrWhiteSpace(bindRoomNo);
                        if (hasBind)
                        {
                            // v1.1.0 F-06：绑定房产只需「楼栋号 + 房号」，单元号可留空（老旧小区无单元）
                            if (string.IsNullOrWhiteSpace(bindBuildingNo) || string.IsNullOrWhiteSpace(bindRoomNo))
                                rowErrors.Add(Err(row, "绑定房产", bindBuildingNo + "-" + bindRoomNo, "绑定房产需同时填写「绑定楼栋号 + 绑定房号」", "补填楼栋号与房号（单元号可留空）"));
                            else
                            {
                                string bindLabel = bindBuildingNo.Trim() + (string.IsNullOrWhiteSpace(bindUnitNo) ? string.Empty : bindUnitNo.Trim()) + bindRoomNo.Trim();
                                propertyId = ResolvePropertyByKeyLoose(c, tx, bindBuildingNo.Trim(), bindUnitNo, bindRoomNo.Trim());
                                if (!propertyId.HasValue)
                                    rowErrors.Add(Err(row, "绑定房产", bindLabel, "找不到唯一对应的房产", "核对该房产是否存在；若同楼栋存在多个同名房号，请补填单元号"));
                            }
                        }
                        if (rowErrors.Count == 0)
                        {
                            if (spaceType == ParkingSpaceType.PropertyRight && propertyId.HasValue &&
                                _repo.CountParkingsBoundToProperty(c, tx, propertyId.Value, 0, (int)ParkingSpaceType.PropertyRight) > 0)
                            rowErrors.Add(Err(row, "绑定房产", bindBuildingNo + "-" + bindUnitNo + "-" + bindRoomNo, "该房产已绑定一个产权车位", "更换房产"));
                        }
                        if (rowErrors.Count == 0 && propertyId.HasValue)
                        {
                            // 回填业主：取绑定房产的活跃业主（业主类型关系），避免车位业主为空
                            ownerId = ResolveOwnerIdByProperty(c, tx, propertyId.Value);
                        }
                        if (rowErrors.Count == 0)
                        {
                            _repo.InsertParking(c, tx, new ParkingSpaceDto
                            {
                                SpaceNo = key,
                                Area = area,
                                SpaceType = spaceType,
                                Status = status,
                                PropertyId = propertyId,
                                OwnerId = ownerId,
                                MonthlyRent = null,
                                RentTo = null
                            });
                            count.AddInserted(row, spaceKey);
                        }
                    }
                }
                if (rowErrors.Count > 0) count.AddFailed(row, spaceKey, rowErrors);
                errors.AddRange(rowErrors);
                row++;
            }
            return count;
        }

        private ImportCount ImportRelations(IDbConnection c, IDbTransaction tx, IXLWorksheet sheet, List<ImportErrorItemDto> errors)
        {
            Dictionary<string, int> map = BuildColumnMap(sheet);
            RequireHeaders(map, ImportModule.OwnerRelation, "房号", "业主姓名");
            int colBuilding = ColIndex(map, "楼栋号", "楼栋");
            int colUnit = ColIndex(map, "单元号", "单元");
            int colRoom = ColIndex(map, "房号");
            int colOwnerName = ColIndex(map, "业主姓名", "姓名");
            int colOwnerIdCard = ColIndex(map, "业主证件号", "证件号");
            int colOwnerPhone = ColIndex(map, "业主电话", "电话");
            int colRelType = ColIndex(map, "关系类型");
            int colShare = ColIndex(map, "份额");
            int colStart = ColIndex(map, "起始日期", "生效日期");
            int colEnd = ColIndex(map, "终止日期", "结束日期");
            int colStatus = ColIndex(map, "状态");

            var count = new ImportCount();
            int row = 2;
            int lastRow = sheet.LastRowUsed().RowNumber();
            while (row <= lastRow)
            {
                if (IsExampleRow(sheet, row, colBuilding)) { row++; continue; }
                string buildingNo = CellAt(sheet, row, colBuilding);
                string unitNo = CellAt(sheet, row, colUnit);      // v1.1.0 F-08：单元号选填
                string roomNo = CellAt(sheet, row, colRoom);
                string ownerName = CellAt(sheet, row, colOwnerName);
                string ownerIdCard = CellAt(sheet, row, colOwnerIdCard);
                string ownerPhone = CellAt(sheet, row, colOwnerPhone);
                string relTypeText = CellAt(sheet, row, colRelType);
                string shareText = CellAt(sheet, row, colShare);
                string startText = CellAt(sheet, row, colStart);
                string endText = CellAt(sheet, row, colEnd);
                string statusText = CellAt(sheet, row, colStatus);
                // v1.2.0（CHG-v1.2.0-04）：模板预留空白行跳过
                if (IsBlankRow(buildingNo, unitNo, roomNo, ownerName, ownerIdCard, ownerPhone,
                        relTypeText, shareText, startText, endText, statusText))
                { row++; continue; }
                string relKey = KeyLabel(buildingNo, unitNo, roomNo) + " / " + OwnerKeyLabel(ownerName, null, ownerIdCard, ownerPhone);

                var rowErrors = new List<ImportErrorItemDto>();
                if (string.IsNullOrWhiteSpace(buildingNo)) rowErrors.Add(Err(row, "楼栋号", buildingNo, "楼栋号不能为空", "填写楼栋号"));
                if (string.IsNullOrWhiteSpace(roomNo)) rowErrors.Add(Err(row, "房号", roomNo, "房号不能为空", "填写房号"));
                if (string.IsNullOrWhiteSpace(ownerName)) rowErrors.Add(Err(row, "业主姓名", ownerName, "业主姓名不能为空", "填写业主姓名"));
                OwnerRelType relType = OwnerRelType.Owner;
                if (!string.IsNullOrWhiteSpace(relTypeText) && !TryParseLabeled(relTypeText, OwnerRelTypeMap, out relType))
                    rowErrors.Add(Err(row, "关系类型", relTypeText, "关系类型不合法（业主/共有人/租户备案）", "填写 业主/共有人/租户备案"));
                decimal share = 0;
                if (!string.IsNullOrWhiteSpace(shareText) &&
                    !decimal.TryParse(AreaValue.Normalize(shareText), NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out share))
                    rowErrors.Add(Err(row, "份额", shareText, "数字格式不正确", "填写 0~100"));
                OwnerRelStatus relStatus = OwnerRelStatus.Active;
                bool hasStatusText = !string.IsNullOrWhiteSpace(statusText);
                if (hasStatusText && !TryParseLabeled(statusText, OwnerRelStatusMap, out relStatus))
                    rowErrors.Add(Err(row, "状态", statusText, "状态不合法（有效/即将到期/已解除）", "填写 有效/即将到期/已解除，或留空自动判定"));

                if (rowErrors.Count == 0)
                {
                    string propLabel = buildingNo.Trim() + (string.IsNullOrWhiteSpace(unitNo) ? string.Empty : unitNo.Trim()) + roomNo.Trim();
                    int? propertyId = ResolvePropertyByKeyLoose(c, tx, buildingNo.Trim(), unitNo, roomNo.Trim());
                    // v1.2.0（CHG-v1.2.0-02 / 第 2 轮）：与「业主」模板共用同一套同名判定，并额外把**本行楼栋/房号**作为区分依据
                    // —— 不同楼栋/房号的同名业主属不同业主（先按本房产已绑定的同名业主判定，再按常住地址比对）。
                    // v1.3.0（负责人 2026-09-23 裁定，两轮）：
                    //   第 1 轮删除「按文件出现顺序配对同名档案」的兜底；第 2 轮删除「无补充信息就判给唯一一位」的兜底，并补上
                    //   「与档案一致 → 同一人；与档案不一致 → 不同业主（按本行信息建档并关联）；档案缺该信息 → 判为同一人并回填档案」。
                    OwnerMatch ownerMatch = MatchOwnerForImport(c, tx, ownerName, ownerIdCard, ownerPhone,
                        propertyId, buildingNo, unitNo, roomNo, relType);
                    int? ownerId = ownerMatch.OwnerId;
                    string ownerNote = null;
                    if (ownerId.HasValue && (ownerMatch.BackfillIdCard || ownerMatch.BackfillPhone))
                    {
                        // 档案缺「证件号 / 电话」→ 判为同一人，并用本行值补齐档案（回执写明补了什么）
                        ownerNote = BackfillOwnerFromRelationRow(c, tx, ownerId.Value, ownerIdCard, ownerPhone,
                            ownerMatch.BackfillIdCard, ownerMatch.BackfillPhone);
                    }
                    else if (!ownerId.HasValue && ownerMatch.NewOwnerFromRow)
                    {
                        // 同名但补充信息与档案不一致 → 判定为不同业主：按本行信息建档后关联房产（负责人 2026-09-23 裁定）
                        int newOwnerId = _repo.InsertOwner(c, tx, new OwnerDto
                        {
                            Name = ownerName.Trim(),
                            IdCardType = OwnerIdCardType.IdCard,
                            IdCard = string.IsNullOrWhiteSpace(ownerIdCard) ? null : ownerIdCard.Trim(),
                            Phone = string.IsNullOrWhiteSpace(ownerPhone) ? null : ownerPhone.Trim(),
                            Status = OwnerStatus.Living
                        });
                        WriteChangeLog(c, tx, BaseChangeObjectType.Owner, newOwnerId, "档案来源",
                            string.Empty, "关系模板导入（同名信息与档案不一致）", "批量导入");
                        ownerId = newOwnerId;
                        ownerNote = "同名业主信息与档案不一致：已按本行信息新建档案 #" + newOwnerId;
                    }
                    if (!propertyId.HasValue)
                        rowErrors.Add(Err(row, "房号", propLabel,
                            string.IsNullOrWhiteSpace(unitNo) ? "找不到唯一对应的房产" : "找不到该房产",
                            string.IsNullOrWhiteSpace(unitNo) ? "请先维护房产；同楼栋存在多个同名房号时请补填单元号" : "请先维护房产或核对单元号"));
                    else if (!ownerId.HasValue)
                        rowErrors.Add(Err(row, "业主姓名", ownerName,
                            ownerMatch.Reason ?? "找不到该业主",
                            ownerMatch.Suggestion ?? "请先维护业主或填写证件号/电话"));
                    else
                    {
                        // 回执「对象」带上业主档案号：同名业主靠这一列才能分辨到底关联了谁
                        relKey = propLabel + " / " + OwnerKeyLabel(ownerName, ownerId, ownerIdCard, ownerPhone);
                        DateTime effectiveAt = ParseDate(startText) ?? DateTime.Today;
                        DateTime? expireAt = ParseDate(endText);
                        // v1.1.2 I-01（负责人 2026-09-19 裁定 A）：同「房产 + 业主 + 生效日期」命中既有关系 → 覆盖处理。
                        // 同时 BR-INF-02 只在「该房产已有另一位业主」时报错 —— 同一业主重复行不再被拦。
                        int existingRelId = c.ExecuteScalar<int>(
                            "SELECT COALESCE((SELECT id FROM t_owner_property_rel WHERE property_id = @p AND owner_id = @o " +
                            "AND date(effective_at) = @d AND del_flag = 0 ORDER BY id DESC LIMIT 1), 0)",
                            new { p = propertyId.Value, o = ownerId.Value, d = effectiveAt.ToString("yyyy-MM-dd") }, tx);
                        if (existingRelId == 0 && relType == OwnerRelType.Owner &&
                            _repo.CountActiveOwnerRelationsByProperty(c, tx, propertyId.Value, 0) > 0)
                        rowErrors.Add(Err(row, "关系类型", relTypeText, "该房产已有一名业主，一房仅可登记一名业主", "改为共有人/租户备案"));
                        if (rowErrors.Count == 0)
                        {
                            decimal defaultShare = relType == OwnerRelType.Owner ? 100m : (relType == OwnerRelType.CoOwner ? 50m : 0m);
                            decimal effShare = share <= 0 ? defaultShare : share;
                            if (effShare < 0 || effShare > 100) rowErrors.Add(Err(row, "份额", shareText, "份额须在 0~100 之间", "填写 0~100"));
                        }
                        if (rowErrors.Count == 0)
                        {
                            if (existingRelId > 0)
                            {
                                OwnerPropertyRelationDto beforeRel = _repo.GetRelation(c, existingRelId);
                                if (beforeRel == null)
                                {
                                    rowErrors.Add(Err(row, "关系", propLabel + " / " + ownerName, "该关系已被删除，请核对后重试", "在「业主-房产关系」页面确认后再导入"));
                                }
                                else
                                {
                                    var changes = new List<string>();
                                    // 只覆盖文件里填了值的字段（关系类型/份额/终止日期/状态留空 → 保留库内原值）
                                    OwnerRelType newRelType = string.IsNullOrWhiteSpace(relTypeText) ? beforeRel.RelType : relType;
                                    decimal newShare = share > 0 ? share : beforeRel.Share;
                                    DateTime? newExpire = string.IsNullOrWhiteSpace(endText) ? beforeRel.ExpireAt : expireAt;
                                    OwnerRelStatus newRelStatus = hasStatusText ? relStatus : beforeRel.Status;
                                    _repo.UpdateRelation(c, tx, new OwnerPropertyRelationDto
                                    {
                                        Id = beforeRel.Id,
                                        PropertyId = propertyId.Value,
                                        OwnerId = ownerId.Value,
                                        RelType = newRelType,
                                        Share = newShare,
                                        EffectiveAt = effectiveAt,
                                        ExpireAt = newExpire,
                                        Status = newRelStatus
                                    });
                                    if (beforeRel.RelType != newRelType)
                                    {
                                        WriteChangeLog(c, tx, BaseChangeObjectType.Relation, beforeRel.Id, "关系类型",
                                            RelTypeName(beforeRel.RelType), RelTypeName(newRelType), "批量导入");
                                        changes.Add("关系类型：" + RelTypeName(beforeRel.RelType) + " → " + RelTypeName(newRelType));
                                    }
                                    if (beforeRel.Share != newShare)
                                    {
                                        WriteChangeLog(c, tx, BaseChangeObjectType.Relation, beforeRel.Id, "份额",
                                            beforeRel.Share.ToString("0.##"), newShare.ToString("0.##"), "批量导入");
                                        changes.Add("份额：" + beforeRel.Share.ToString("0.##") + " → " + newShare.ToString("0.##"));
                                    }
                                    if (!Equals(beforeRel.ExpireAt, newExpire))
                                    {
                                        WriteChangeLog(c, tx, BaseChangeObjectType.Relation, beforeRel.Id, "终止日期",
                                            beforeRel.ExpireAt.HasValue ? beforeRel.ExpireAt.Value.ToString("yyyy-MM-dd") : string.Empty,
                                            newExpire.HasValue ? newExpire.Value.ToString("yyyy-MM-dd") : string.Empty, "批量导入");
                                        changes.Add("终止日期：" + (beforeRel.ExpireAt.HasValue ? beforeRel.ExpireAt.Value.ToString("yyyy-MM-dd") : "（长期）")
                                            + " → " + (newExpire.HasValue ? newExpire.Value.ToString("yyyy-MM-dd") : "（长期）"));
                                    }
                                    if (beforeRel.Status != newRelStatus)
                                    {
                                        WriteChangeLog(c, tx, BaseChangeObjectType.Relation, beforeRel.Id, "状态",
                                            RelStatusText(beforeRel.Status), RelStatusText(newRelStatus), "批量导入");
                                        changes.Add("状态：" + RelStatusText(beforeRel.Status) + " → " + RelStatusText(newRelStatus));
                                    }
                                    count.AddUpdated(row, relKey + "（关系 #" + beforeRel.Id + "）", changes, ownerNote);
                                }
                            }
                            else
                            {
                                _repo.InsertRelation(c, tx, new OwnerPropertyRelationDto
                                {
                                    PropertyId = propertyId.Value,
                                    OwnerId = ownerId.Value,
                                    RelType = relType,
                                    Share = share <= 0 ? (relType == OwnerRelType.Owner ? 100m : (relType == OwnerRelType.CoOwner ? 50m : 0m)) : share,
                                    EffectiveAt = effectiveAt,
                                    ExpireAt = expireAt,
                                    // 模板「状态」列给了就按填写值落库（历史档案导入），否则按终止日期自动判定
                                    Status = hasStatusText ? relStatus : ResolveRelStatus(expireAt)
                                });
                                count.AddInserted(row, relKey, ownerNote);
                            }
                        }
                    }
                }
                if (rowErrors.Count > 0) count.AddFailed(row, relKey, rowErrors);
                errors.AddRange(rowErrors);
                row++;
            }
            return count;
        }

        /// <summary>
        /// 业主关键字段变更留痕（t_base_change_log）。
        /// CHG-v1.1.2-01：新增 channel 参数 —— 批量导入覆盖走「批量导入」，档案页维护仍为「后台维护」，
        /// 便于在业主档案的「联系方式变更历史」里区分数据来源。
        /// v1.2.0（CHG-v1.2.0-01）：返回逐字段变化描述，供导入回执写明「覆盖了什么」。
        /// </summary>
        private List<string> LogOwnerChanges(IDbConnection c, IDbTransaction tx, OwnerDto before, OwnerDto after, string channel = "后台维护")
        {
            var changes = new List<string>();
            if (!Equals(before.Phone, after.Phone))
            {
                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, after.Id, "联系电话", before.Phone, after.Phone, channel);
                changes.Add("联系电话：" + Show(before.Phone) + " → " + Show(after.Phone));
            }
            if (!Equals(before.EmergencyContactName, after.EmergencyContactName))
            {
                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, after.Id, "紧急联系人", before.EmergencyContactName, after.EmergencyContactName, channel);
                changes.Add("紧急联系人：" + Show(before.EmergencyContactName) + " → " + Show(after.EmergencyContactName));
            }
            if (!Equals(before.EmergencyContactPhone, after.EmergencyContactPhone))
            {
                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, after.Id, "紧急联系人电话", before.EmergencyContactPhone, after.EmergencyContactPhone, channel);
                changes.Add("紧急联系人电话：" + Show(before.EmergencyContactPhone) + " → " + Show(after.EmergencyContactPhone));
            }
            if (!Equals(before.ResidentAddress, after.ResidentAddress))
            {
                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, after.Id, "常住地址", before.ResidentAddress, after.ResidentAddress, channel);
                changes.Add("常住地址：" + Show(before.ResidentAddress) + " → " + Show(after.ResidentAddress));
            }
            return changes;
        }

        /// <summary>回执展示用：空值统一显示「（空）」，避免「旧 → 新」看不出是哪边为空。</summary>
        private static string Show(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "（空）" : value.Trim();
        }

        /// <summary>读取业主原始行（不含统计聚合），供导入覆盖前的「变更前后」比对使用。</summary>
        private static OwnerDto LoadOwner(IDbConnection c, IDbTransaction tx, int id)
        {
            return c.QueryFirstOrDefault<OwnerDto>(
                "SELECT id, name, id_card_type AS IdCardType, id_card AS IdCard, phone, resident_address AS ResidentAddress, " +
                "emergency_contact_name AS EmergencyContactName, emergency_contact_phone AS EmergencyContactPhone, " +
                "check_in_date AS CheckInDate, status FROM t_owner WHERE id = @id AND del_flag = 0",
                new { id }, tx);
        }

        /// <summary>读取车位原始行（含绑定与租金字段），供导入覆盖前的「变更前后」比对与保留非覆盖字段使用。</summary>
        private static ParkingSpaceDto LoadParking(IDbConnection c, IDbTransaction tx, int id)
        {
            return c.QueryFirstOrDefault<ParkingSpaceDto>(
                "SELECT id, space_no AS SpaceNo, area AS Area, space_type AS SpaceType, status AS Status, " +
                "property_id AS PropertyId, owner_id AS OwnerId, monthly_rent AS MonthlyRent, " +
                "rent_mode AS RentMode, rent_to AS RentTo, del_flag AS DelFlag " +
                "FROM t_parking_space WHERE id = @id AND del_flag = 0",
                new { id }, tx);
        }

        private static string ParkingStatusLabel(ParkingSpaceStatus status)
        {
            switch (status)
            {
                case ParkingSpaceStatus.Owned: return "已售";
                case ParkingSpaceStatus.Rented: return "已租";
                case ParkingSpaceStatus.Repairing: return "维修中";
                default: return "空置";
            }
        }

        private static string ParkingTypeLabel(ParkingSpaceType type)
        {
            switch (type)
            {
                case ParkingSpaceType.CivilDefense: return "普通";
                case ParkingSpaceType.Temporary: return "临时";
                default: return "产权";
            }
        }

        private static void WriteChangeLog(IDbConnection c, IDbTransaction tx, BaseChangeObjectType type, int objectId,
            string field, string oldValue, string newValue, string channel)
        {
            c.Execute(
                "INSERT INTO t_base_change_log (object_type, object_id, change_content, field_name, old_value, new_value, operator, channel, changed_at) " +
                "VALUES (@ObjectType, @ObjectId, @ChangeContent, @FieldName, @OldValue, @NewValue, @Operator, @Channel, @ChangedAt)",
                new
                {
                    ObjectType = (int)type,
                    ObjectId = objectId,
                    ChangeContent = field + "：" + (oldValue ?? string.Empty) + " → " + (newValue ?? string.Empty),
                    FieldName = field,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Operator = "系统管理员",
                    Channel = channel,
                    ChangedAt = DateTime.Now
                }, tx);
        }

        private static OwnerRelStatus ResolveRelStatus(DateTime? expireAt)
        {
            if (!expireAt.HasValue) return OwnerRelStatus.Active;
            if (expireAt.Value < DateTime.Today) return OwnerRelStatus.Released;
            if (expireAt.Value <= DateTime.Today.AddDays(30)) return OwnerRelStatus.Expiring;
            return OwnerRelStatus.Active;
        }

        private static OwnerPropertyRelationDto NormalizeRelation(OwnerPropertyRelationDto dto)
        {
            if (dto == null) return null;
            if (dto.ExpireAt.HasValue && dto.ExpireAt.Value < DateTime.Today && dto.Status != OwnerRelStatus.Released)
            {
                dto.Status = OwnerRelStatus.Released;
                dto.StatusText = "已解除";
            }
            return dto;
        }

        private int? ResolveUnit(IDbConnection c, IDbTransaction tx, string buildingNo, string unitNo)
        {
            return c.ExecuteScalar<int?>(
                "SELECT u.id FROM t_unit u JOIN t_building b ON b.id = u.building_id " +
                "WHERE b.building_no = @buildingNo AND u.unit_no = @unitNo AND b.del_flag = 0 AND u.del_flag = 0 LIMIT 1",
                new { buildingNo, unitNo }, tx);
        }

        /// <summary>按楼栋号取楼栋 id（v1.1.0：无单元房产入库需要直接定位楼栋）。</summary>
        private int? ResolveBuildingId(IDbConnection c, IDbTransaction tx, string buildingNo)
        {
            return c.ExecuteScalar<int?>(
                "SELECT b.id FROM t_building b WHERE b.building_no = @buildingNo AND b.del_flag = 0 LIMIT 1",
                new { buildingNo }, tx);
        }

        /// <summary>
        /// 按 楼栋号 + 单元号（可空）+ 房号 定位房产（v1.1.0 F-05/F-08）：
        /// 给了单元号走精确匹配；没给单元号时优先匹配「无单元房产」，找不到再按楼栋+房号兜底；
        /// 命中多条时返回 null（由调用方给出「补填单元号」的提示，避免猜错房产）。
        /// </summary>
        private int? ResolvePropertyByKeyLoose(IDbConnection c, IDbTransaction tx, string buildingNo, string unitNo, string roomNo)
        {
            string exactSql =
                "SELECT p.id FROM t_property p " +
                "JOIN t_unit u ON u.id = p.unit_id " +
                "JOIN t_building b ON b.id = u.building_id " +
                "WHERE b.building_no = @buildingNo AND u.unit_no = @unitNo AND p.room_no = @roomNo " +
                "AND b.del_flag = 0 AND u.del_flag = 0 AND p.del_flag = 0 LIMIT 1";
            if (!string.IsNullOrWhiteSpace(unitNo))
            {
                return c.ExecuteScalar<int?>(exactSql, new { buildingNo, unitNo = unitNo.Trim(), roomNo }, tx);
            }
            string looseSql =
                "SELECT p.id FROM t_property p " +
                "LEFT JOIN t_unit u ON u.id = p.unit_id " +
                "LEFT JOIN t_building b ON b.id = COALESCE(p.building_id, u.building_id) " +
                "WHERE b.building_no = @buildingNo AND p.room_no = @roomNo " +
                "AND b.del_flag = 0 AND p.del_flag = 0";
            var ids = c.Query<int>(looseSql, new { buildingNo, roomNo }, tx).ToList();
            return ids.Count == 1 ? (int?)ids[0] : null;
        }

        /// <summary>按 楼栋号+单元号+房号 精确定位房产（消除跨楼栋同名房号歧义）。</summary>
        private int? ResolvePropertyByKey(IDbConnection c, IDbTransaction tx, string buildingNo, string unitNo, string roomNo)
        {
            return c.ExecuteScalar<int?>(
                "SELECT p.id FROM t_property p " +
                "JOIN t_unit u ON u.id = p.unit_id " +
                "JOIN t_building b ON b.id = u.building_id " +
                "WHERE b.building_no = @buildingNo AND u.unit_no = @unitNo AND p.room_no = @roomNo " +
                "AND b.del_flag = 0 AND u.del_flag = 0 AND p.del_flag = 0 LIMIT 1",
                new { buildingNo, unitNo, roomNo }, tx);
        }

        /// <summary>导入期业主匹配结果（v1.2.0 CHG-v1.2.0-02）。</summary>
        private sealed class OwnerMatch
        {
            public int? OwnerId;
            /// <summary>true＝口径上属于「另一位业主」（同名但证件号/电话不同）→ 业主模板可新增。</summary>
            public bool IsNewOwner;
            /// <summary>
            /// true＝本行带了「业主证件号」或「业主电话」，但档案里没有任何一份能对上，且同名档案都没有登记该信息 →
            /// 判定为不同业主，可由调用方按本行信息建档后再关联房产（v1.3.0 第 2 轮：关系模板自动建档）。
            /// </summary>
            public bool NewOwnerFromRow;
            /// <summary>命中档案、但该档案的证件号为空 → 用本行「业主证件号」补齐档案。</summary>
            public bool BackfillIdCard;
            /// <summary>命中档案、但该档案的电话为空 → 用本行「业主电话」补齐档案。</summary>
            public bool BackfillPhone;
            public string Reason;
            public string Suggestion;
            /// <summary>无法唯一判定（同名多条且补充信息无法区分）→ 必须报错，不做自造合并。</summary>
            public bool Ambiguous { get { return !OwnerId.HasValue && !IsNewOwner; } }
        }

        private sealed class OwnerPick
        {
            public int Id { get; set; }
            public string IdCard { get; set; }
            public string Phone { get; set; }
            /// <summary>常住地址（v1.2.0 第 2 轮）：业主-房产关系模板用它与本行「楼栋/房号」比对。</summary>
            public string ResidentAddress { get; set; }
        }

        /// <summary>
        /// 导入期业主匹配（v1.2.0 CHG-v1.2.0-02，v1.3.0 第 2 轮定稿）——业主模板与业主-房产关系模板共用一套判定。
        /// **判定三分法**：与档案一致 → 同一人；与档案不一致 → 不同业主；档案缺该信息 → 用本行值补齐档案。
        ///   1) 填了证件号 → 按「姓名 + 证件号」定位：唯一命中 → 同一人；
        ///      未命中时看该姓名下「证件号为空」的档案件数 —— 恰好 1 份 → 判为同一人并**回填证件号**（<see cref="OwnerMatch.BackfillIdCard"/>）；
        ///      0 份 → 同名档案证件号都与本行不同 → **不同业主**（<see cref="OwnerMatch.NewOwnerFromRow"/>，可执行建档）；
        ///      ≥2 份 → 无法唯一判定 → 报错（档案本身重复，需先整理）。
        ///   2) 否则填了电话 → 按「姓名 + 电话」定位，口径与证件号完全对称（回填见 <see cref="OwnerMatch.BackfillPhone"/>）。
        ///   3) 都没填 → 按姓名找：姓名唯一 → 同一人；同名多条时按
        ///      ① 本行「楼栋/房号」对应房产**已绑定的同名业主**（同关系类型）→
        ///      ② 同名业主「常住地址」与本行「楼栋/房号」一致（唯一）
        ///      两条房产侧证据判定；都不成立 → <see cref="OwnerMatch.Ambiguous"/>，由调用方按行报错并要求补「业主证件号」或「业主电话」。
        /// v1.3.0（负责人 2026-09-23 裁定，两轮）：
        ///   第 1 轮删除「按文件出现顺序配对同名档案」的兜底；第 2 轮**删除「只有一位同名业主无补充信息就判给它」的兜底** ——
        ///   该兜底会把同名的多套房产全部堆到同一个人名下（其余同名业主永远为空）；改为「同名必须靠证件号/电话或房产侧证据判定」。
        /// </summary>
        /// <param name="propertyId">本行「楼栋/单元/房号」定位到的房产；业主模板传 null（没有房产上下文，只走姓名口径）。</param>
        /// <param name="buildingNo">本行楼栋号（用于比对同名业主的常住地址）。</param>
        /// <param name="unitNo">本行单元号（可空）。</param>
        /// <param name="roomNo">本行房号。</param>
        /// <param name="relType">本行的关系类型：只认同类型的既有关系（业主行不会被「共有人/租户备案」的同名业主抢走）。</param>
        private OwnerMatch MatchOwnerForImport(IDbConnection c, IDbTransaction tx, string name, string idCard, string phone,
            int? propertyId = null, string buildingNo = null, string unitNo = null, string roomNo = null,
            OwnerRelType? relType = null)
        {
            string n = (name ?? string.Empty).Trim();
            string ic = (idCard ?? string.Empty).Trim();
            string ph = (phone ?? string.Empty).Trim();

            if (ic.Length > 0)
            {
                var byCard = c.Query<int>(
                    "SELECT id FROM t_owner WHERE name = @n AND id_card = @ic AND del_flag = 0",
                    new { n, ic }, tx).ToList();
                if (byCard.Count == 1) return new OwnerMatch { OwnerId = byCard[0] };
                if (byCard.Count > 1)
                    return new OwnerMatch
                    {
                        Reason = "同名且同证件号的业主有 " + byCard.Count + " 名，无法确定对象",
                        Suggestion = "请先在「业主档案」核对重复档案（同证件号不应建档多次）后再导入"
                    };
                // 档案里没有这个证件号：区分「档案缺证件号（视为同一人并补齐）」与「信息不一致（不同业主）」
                List<int> blankCard = QueryOwnersWithoutIdCard(c, tx, n);
                if (blankCard.Count == 1)
                    return new OwnerMatch { OwnerId = blankCard[0], BackfillIdCard = true };
                if (blankCard.Count == 0)
                    return new OwnerMatch
                    {
                        IsNewOwner = true,
                        NewOwnerFromRow = true,
                        Reason = "同名业主的证件号都与本行不一致，判定为不同业主",
                        Suggestion = "如确为另一位业主，可直接导入（关系模板会按本行信息建档并关联；业主模板会新增档案）"
                    };
                return new OwnerMatch
                {
                    Reason = "同名且「证件号为空」的档案有 " + blankCard.Count + " 份，无法确定是哪一位",
                    Suggestion = "请先补齐这些档案的证件号（或在本行改填「业主电话」），再重新导入"
                };
            }

            if (ph.Length > 0)
            {
                var byPhone = c.Query<int>(
                    "SELECT id FROM t_owner WHERE name = @n AND phone = @ph AND del_flag = 0",
                    new { n, ph }, tx).ToList();
                if (byPhone.Count == 1) return new OwnerMatch { OwnerId = byPhone[0] };
                if (byPhone.Count > 1)
                    return new OwnerMatch
                    {
                        Reason = "同名且同电话的业主有 " + byPhone.Count + " 名，无法确定对象",
                        Suggestion = "请先在「业主档案」核对重复档案后再导入"
                    };
                List<int> blankPhone = QueryOwnersWithoutPhone(c, tx, n);
                if (blankPhone.Count == 1)
                    return new OwnerMatch { OwnerId = blankPhone[0], BackfillPhone = true };
                if (blankPhone.Count == 0)
                    return new OwnerMatch
                    {
                        IsNewOwner = true,
                        NewOwnerFromRow = true,
                        Reason = "同名业主的电话都与本行不一致，判定为不同业主",
                        Suggestion = "如确为另一位业主，可直接导入（关系模板会按本行信息建档并关联；业主模板会新增档案）"
                    };
                return new OwnerMatch
                {
                    Reason = "同名且「电话为空」的档案有 " + blankPhone.Count + " 份，无法确定是哪一位",
                    Suggestion = "请先补齐这些档案的联系电话（或在本行改填「业主证件号」），再重新导入"
                };
            }

            var byName = c.Query<OwnerPick>(
                "SELECT id AS Id, COALESCE(NULLIF(TRIM(id_card), ''), '') AS IdCard, " +
                "COALESCE(NULLIF(TRIM(phone), ''), '') AS Phone, " +
                "COALESCE(TRIM(resident_address), '') AS ResidentAddress " +
                "FROM t_owner WHERE name = @n AND del_flag = 0", new { n }, tx).ToList();
            if (byName.Count == 1) return new OwnerMatch { OwnerId = byName[0].Id };
            if (byName.Count > 1)
            {
                // ① 本行「楼栋/房号」指向的房产已经绑定了其中一位同名业主 → 判为该业主
                //    （负责人 2026-09-21 第 2 轮口径：不同楼栋/房号的同名业主属不同业主）
                if (propertyId.HasValue && propertyId.Value > 0)
                {
                    var ids = byName.Select(x => x.Id).ToList();
                    // 只认与本行同类型的关系：同名的「共有人/租户备案」不会被用来判定「业主」行
                    var boundToTarget = relType.HasValue
                        ? c.Query<int>(
                            "SELECT DISTINCT owner_id FROM t_owner_property_rel " +
                            "WHERE property_id = @propertyId AND del_flag = 0 AND rel_type = @relType AND owner_id IN @ids",
                            new { propertyId = propertyId.Value, relType = (int)relType.Value, ids }, tx).ToList()
                        : c.Query<int>(
                            "SELECT DISTINCT owner_id FROM t_owner_property_rel " +
                            "WHERE property_id = @propertyId AND del_flag = 0 AND owner_id IN @ids",
                            new { propertyId = propertyId.Value, ids }, tx).ToList();
                    if (boundToTarget.Count == 1) return new OwnerMatch { OwnerId = boundToTarget[0] };
                }

                // ② 同名业主的「常住地址」与本行 楼栋[+单元]+房号 一致且唯一 → 判为该业主
                //    （业主档案从原始资料导入时地址往往已写明，等同「不同楼栋/房号」的档案依据）
                if (!string.IsNullOrWhiteSpace(buildingNo) && !string.IsNullOrWhiteSpace(roomNo))
                {
                    var addressHit = byName
                        .Where(x => AddressMatchesProperty(x.ResidentAddress, buildingNo, unitNo, roomNo))
                        .ToList();
                    if (addressHit.Count == 1) return new OwnerMatch { OwnerId = addressHit[0].Id };
                }

                bool hasPropertyContext = !string.IsNullOrWhiteSpace(buildingNo) && !string.IsNullOrWhiteSpace(roomNo);
                return new OwnerMatch
                {
                    Reason = "同名业主有 " + byName.Count + " 名，无法确定对应哪一位"
                        + (hasPropertyContext
                            ? "（本行「" + (buildingNo ?? string.Empty).Trim() + (roomNo ?? string.Empty).Trim()
                              + "」没有绑定其中任何一位，且没有一位的常住地址与本行楼栋/房号一致）"
                            : "（本行未填「业主证件号」与「业主电话」，缺少额外判定条件）"),
                    Suggestion = hasPropertyContext
                        ? "同名业主绑定不同房产时，请在本行补填「业主证件号」或「业主电话」后再导入；也可在「业主-房产关系」页面手动绑定"
                        : "请补充「证件号」或「电话」区分同名业主后再导入"
                };
            }
            return new OwnerMatch
            {
                IsNewOwner = true,
                Reason = "找不到该业主",
                Suggestion = "请在「业主档案」维护该业主，或在本行填写「业主证件号」/「业主电话」"
            };
        }

        /// <summary>该姓名下「证件号为空」的档案 id（v1.3.0 第 2 轮：档案缺信息 → 视为同一人并用导入值补齐）。</summary>
        private static List<int> QueryOwnersWithoutIdCard(IDbConnection c, IDbTransaction tx, string name)
        {
            return c.Query<int>(
                "SELECT id FROM t_owner WHERE name = @n AND del_flag = 0 " +
                "AND COALESCE(NULLIF(TRIM(id_card), ''), '') = '' ORDER BY id",
                new { n = name }, tx).ToList();
        }

        /// <summary>该姓名下「电话为空」的档案 id（口径同「证件号为空」）。</summary>
        private static List<int> QueryOwnersWithoutPhone(IDbConnection c, IDbTransaction tx, string name)
        {
            return c.Query<int>(
                "SELECT id FROM t_owner WHERE name = @n AND del_flag = 0 " +
                "AND COALESCE(NULLIF(TRIM(phone), ''), '') = '' ORDER BY id",
                new { n = name }, tx).ToList();
        }

        /// <summary>
        /// 关系模板命中档案、但该档案缺「证件号 / 电话」→ 判为同一人，并用本行值**只补齐空字段**（写变更留痕），
        /// 返回回执提示文案；没有可补的字段返回 null。v1.3.0 第 2 轮（负责人 2026-09-23 裁定③：允许用导入值补齐档案）。
        /// </summary>
        private string BackfillOwnerFromRelationRow(IDbConnection c, IDbTransaction tx, int ownerId,
            string idCard, string phone, bool fillIdCard, bool fillPhone)
        {
            OwnerDto before = LoadOwner(c, tx, ownerId);
            if (before == null) return null;
            var after = new OwnerDto
            {
                Id = before.Id,
                Name = before.Name,
                IdCardType = before.IdCardType,
                IdCard = before.IdCard,
                Phone = before.Phone,
                ResidentAddress = before.ResidentAddress,
                EmergencyContactName = before.EmergencyContactName,
                EmergencyContactPhone = before.EmergencyContactPhone,
                CheckInDate = before.CheckInDate,
                Status = before.Status
            };
            var notes = new List<string>();
            if (fillIdCard && !string.IsNullOrWhiteSpace(idCard))
            {
                after.IdCard = idCard.Trim();
                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, ownerId, "证件号", before.IdCard, after.IdCard, "批量导入");
                notes.Add("证件号 " + Show(before.IdCard) + " → " + Show(after.IdCard));
            }
            if (fillPhone && !string.IsNullOrWhiteSpace(phone))
            {
                after.Phone = phone.Trim();
                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, ownerId, "联系电话", before.Phone, after.Phone, "批量导入");
                notes.Add("联系电话 " + Show(before.Phone) + " → " + Show(after.Phone));
            }
            if (notes.Count == 0) return null;
            _repo.UpdateOwner(c, tx, after);
            return "档案 #" + ownerId + " 补齐：" + string.Join("；", notes);
        }

        /// <summary>取指定房产的活跃业主 id（业主类型关系，优先），用于车位绑定房产后回填 owner_id。</summary>
        private int? ResolveOwnerIdByProperty(IDbConnection c, IDbTransaction tx, int propertyId)
        {
            return c.ExecuteScalar<int?>(
                "SELECT r.owner_id FROM t_owner_property_rel r " +
                "WHERE r.property_id = @propertyId AND r.del_flag = 0 AND r.rel_status IN (0,1) AND r.rel_type = 0 " +
                "ORDER BY r.id DESC LIMIT 1",
                new { propertyId = propertyId }, tx);
        }

        private static string BuildErrorFile(int importId, List<ImportErrorItemDto> errors)
        {
            string fileName = "导入错误_" + importId + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xlsx";
            string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("错误清单");
                sheet.Cell(1, 1).Value = "行号";
                sheet.Cell(1, 2).Value = "字段";
                sheet.Cell(1, 3).Value = "单元格值";
                sheet.Cell(1, 4).Value = "错误原因";
                sheet.Cell(1, 5).Value = "处理建议";
                int r = 2;
                foreach (var e in errors)
                {
                    sheet.Cell(r, 1).Value = e.RowNo;
                    sheet.Cell(r, 2).Value = e.Field ?? string.Empty;
                    sheet.Cell(r, 3).Value = e.Content ?? string.Empty;
                    sheet.Cell(r, 4).Value = e.Reason ?? string.Empty;
                    sheet.Cell(r, 5).Value = e.Suggestion ?? string.Empty;
                    r++;
                }
                workbook.SaveAs(filePath);
            }
            return filePath;
        }

        private void ExportProperties(XLWorkbook workbook, BaseInfoQueryRequest filter)
        {
            var data = QueryProperties(filter ?? new BaseInfoQueryRequest { PageIndex = 1, PageSize = 100000 });
            var sheet = workbook.Worksheets.Add("房产");
            string[] headers = { "房产编号", "楼栋-单元-房号", "房号", "建筑面积", "业主", "联系电话", "状态", "当前欠费" };
            for (int i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            int r = 2;
            foreach (var p in data.Items)
            {
                sheet.Cell(r, 1).Value = p.Id;
                sheet.Cell(r, 2).Value = p.UnitPath ?? string.Empty;
                sheet.Cell(r, 3).Value = p.RoomNo ?? string.Empty;
                sheet.Cell(r, 4).Value = p.Area;
                sheet.Cell(r, 5).Value = p.OwnerName ?? string.Empty;
                sheet.Cell(r, 6).Value = p.OwnerPhone ?? string.Empty;
                sheet.Cell(r, 7).Value = StatusText(p.Status);
                sheet.Cell(r, 8).Value = p.CurrentArrear;
                r++;
            }
            for (int c = 1; c <= headers.Length; c++) sheet.Column(c).Width = 16;
        }

        private void ExportOwners(XLWorkbook workbook, BaseInfoQueryRequest filter)
        {
            var data = QueryOwners(filter ?? new BaseInfoQueryRequest { PageIndex = 1, PageSize = 100000 });
            var sheet = workbook.Worksheets.Add("业主");
            string[] headers = { "业主编号", "姓名", "证件类型", "证件号", "联系电话", "入住日期", "常住地址", "紧急联系人", "状态" };
            for (int i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            int r = 2;
            foreach (var o in data.Items)
            {
                sheet.Cell(r, 1).Value = o.Id;
                sheet.Cell(r, 2).Value = o.Name ?? string.Empty;
                sheet.Cell(r, 3).Value = IdCardTypeName(o.IdCardType);
                sheet.Cell(r, 4).Value = o.IdCard ?? string.Empty;
                sheet.Cell(r, 5).Value = o.Phone ?? string.Empty;
                sheet.Cell(r, 6).Value = o.CheckInDate.HasValue ? o.CheckInDate.Value.ToString("yyyy-MM-dd") : string.Empty;
                sheet.Cell(r, 7).Value = o.ResidentAddress ?? string.Empty;
                sheet.Cell(r, 8).Value = ((o.EmergencyContactName ?? string.Empty) + " " + (o.EmergencyContactPhone ?? string.Empty)).Trim();
                sheet.Cell(r, 9).Value = o.Status == OwnerStatus.Living ? "在住" : "搬离";
                r++;
            }
            for (int c = 1; c <= headers.Length; c++) sheet.Column(c).Width = 16;
        }

        private void ExportParkings(XLWorkbook workbook, BaseInfoQueryRequest filter)
        {
            var data = QueryParkings(filter ?? new BaseInfoQueryRequest { PageIndex = 1, PageSize = 100000 });
            var sheet = workbook.Worksheets.Add("车位");
            string[] headers = { "车位编号", "区域", "类型", "状态", "绑定房产", "租金", "租期至" };
            for (int i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            int r = 2;
            foreach (var p in data.Items)
            {
                sheet.Cell(r, 1).Value = p.SpaceNo ?? string.Empty;
                sheet.Cell(r, 2).Value = p.Area ?? string.Empty;
                sheet.Cell(r, 3).Value = p.SpaceTypeText ?? string.Empty;
                sheet.Cell(r, 4).Value = p.StatusText ?? string.Empty;
                sheet.Cell(r, 5).Value = p.BindingProperty ?? string.Empty;
                sheet.Cell(r, 6).Value = (double?)(p.MonthlyRent ?? 0);
                sheet.Cell(r, 7).Value = p.RentTo.HasValue ? p.RentTo.Value.ToString("yyyy-MM-dd") : string.Empty;
                r++;
            }
            for (int c = 1; c <= headers.Length; c++) sheet.Column(c).Width = 16;
        }

        /// <summary>导出业主-房产关系（UC-INF-007）。</summary>
        private void ExportRelations(XLWorkbook workbook, BaseInfoQueryRequest filter)
        {
            var query = filter ?? new BaseInfoQueryRequest { PageIndex = 1, PageSize = 100000 };
            var data = QueryRelations(query);
            var sheet = workbook.Worksheets.Add("业主房产关系");
            string[] headers = { "关系编号", "房产编号", "楼栋-单元-房号", "业主", "关系类型", "份额", "起始日期", "终止日期", "状态" };
            for (int i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            int r = 2;
            foreach (var it in data.Items)
            {
                sheet.Cell(r, 1).Value = it.Id;
                sheet.Cell(r, 2).Value = it.PropertyId;
                sheet.Cell(r, 3).Value = string.IsNullOrEmpty(it.PropertyUnitPath) ? it.PropertyRoomNo : it.PropertyUnitPath;
                sheet.Cell(r, 4).Value = it.OwnerName ?? string.Empty;
                sheet.Cell(r, 5).Value = RelTypeName(it.RelType);
                sheet.Cell(r, 6).Value = it.RelType == OwnerRelType.RentRecord ? (double)0 : (double)it.Share;
                sheet.Cell(r, 7).Value = it.EffectiveAt == default ? string.Empty : it.EffectiveAt.ToString("yyyy-MM-dd");
                sheet.Cell(r, 8).Value = it.ExpireAt.HasValue ? it.ExpireAt.Value.ToString("yyyy-MM-dd") : "长期";
                sheet.Cell(r, 9).Value = it.StatusText ?? RelStatusText(it.Status);
                r++;
            }
            for (int c = 1; c <= headers.Length; c++) sheet.Column(c).Width = 16;
        }

        private static string StatusText(PropertyStatus status)
        {
            switch (status)
            {
                case PropertyStatus.Occupied: return "已入住";
                case PropertyStatus.Renovating: return "装修中";
                default: return "空置";
            }
        }

        /// <summary>房产用途文案（v1.2.0 CHG-v1.2.0-03：模板「用途」列口径与房产列表一致）。</summary>
        private static string UsageText(PropertyUsage usage)
        {
            switch (usage)
            {
                case PropertyUsage.Commercial: return "商铺";
                case PropertyUsage.Vacant: return "空置";
                default: return "住宅";
            }
        }

        private static string RelTypeName(OwnerRelType type)
        {
            switch (type)
            {
                case OwnerRelType.Owner: return "业主";
                case OwnerRelType.CoOwner: return "共有人";
                default: return "租户备案";
            }
        }

        private static string RelStatusText(OwnerRelStatus status)
        {
            switch (status)
            {
                case OwnerRelStatus.Expiring: return "即将到期";
                case OwnerRelStatus.Released: return "已解除";
                default: return "有效";
            }
        }

        private static string IdCardTypeName(OwnerIdCardType type)
        {
            switch (type)
            {
                case OwnerIdCardType.Passport: return "护照";
                case OwnerIdCardType.Hukou: return "户口簿";
                case OwnerIdCardType.Other: return "其他";
                default: return "身份证";
            }
        }

        /// <summary>
        /// 读取单元格文本（v1.2.0 CHG-v1.2.0-04：兼容「从原始档案复制粘贴」的各式单元格）。
        /// 旧实现直接 GetString()：日期单元格按显示格式回显、数字单元格回显带千分位的格式串，
        /// 现场把档案数据整段粘进模板后常出现 yyyy/MM/dd、1,234.56、全角空格等情况。
        /// 现按单元格真实类型取值：日期 → yyyy-MM-dd、数字 → 原始数值串、文本 → 原样（去首尾空白）。
        /// </summary>
        private static string Cell(IXLWorksheet sheet, int row, int col)
        {
            var cell = sheet.Cell(row, col);
            if (cell.IsEmpty()) return string.Empty;
            string text;
            switch (cell.DataType)
            {
                case XLDataType.DateTime:
                    DateTime dt = cell.GetDateTime();
                    text = dt.TimeOfDay == TimeSpan.Zero
                        ? dt.ToString("yyyy-MM-dd")
                        : dt.ToString("yyyy-MM-dd HH:mm:ss");
                    break;
                case XLDataType.Number:
                    double num = cell.GetDouble();
                    text = Math.Abs(num) < 1e15 && Math.Abs(num - Math.Truncate(num)) < 1e-9
                        ? ((long)num).ToString(CultureInfo.InvariantCulture)
                        : num.ToString("0.##########", CultureInfo.InvariantCulture);
                    break;
                case XLDataType.Boolean:
                    text = cell.GetBoolean() ? "是" : "否";
                    break;
                default:
                    text = cell.GetString();
                    break;
            }
            // 复制粘贴常见：首尾带普通空格／不换行空格／全角空格
            return (text ?? string.Empty).Trim(' ', '\t', '\r', '\n', '\u00A0', '\u3000');
        }

        private static ImportErrorItemDto Err(int row, string field, string content, string reason, string suggestion) =>
            new ImportErrorItemDto { RowNo = row, Field = field, Content = content, Reason = reason, Suggestion = suggestion };

        /// <summary>
        /// 宽容日期解析（v1.2.0 CHG-v1.2.0-04）：全角数字/句点、2026年1月1日、2026/1/1、2026.1.1 都能识别。
        /// 兼容不了历史档案的常见写法会由调用方按「留空」处理，不再整行失败。
        /// </summary>
        private static DateTime? ParseDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = AreaValue.Normalize(text);
            if (t.Length == 0) return null;
            DateTime result;
            if (DateTime.TryParse(t, CultureInfo.CurrentCulture, DateTimeStyles.None, out result)) return result;
            if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out result)) return result;
            string zh = t.Replace("年", "-").Replace("月", "-").Replace("日", string.Empty).Trim('-', '.', '/');
            if (DateTime.TryParse(zh, CultureInfo.InvariantCulture, DateTimeStyles.None, out result)) return result;
            return null;
        }

        private static TEnum ParseEnum<TEnum>(string text, TEnum fallback) where TEnum : struct
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            TEnum value;
            if (Enum.TryParse(text, true, out value)) return value;
            int intVal;
            if (int.TryParse(text, out intVal) && Enum.IsDefined(typeof(TEnum), intVal)) return (TEnum)Enum.ToObject(typeof(TEnum), intVal);
            return fallback;
        }

        // ===== 导入枚举中文标签映射（模板列直接写中文：产权/普通/已售…；CHG-v1.1.2-48：人防→普通） =====
        private static readonly Dictionary<string, PropertyUsage> PropertyUsageMap = new Dictionary<string, PropertyUsage>
        { { "住宅", PropertyUsage.Residential }, { "商铺", PropertyUsage.Commercial }, { "空置", PropertyUsage.Vacant } };
        private static readonly Dictionary<string, PropertyStatus> PropertyStatusMap = new Dictionary<string, PropertyStatus>
        { { "空置", PropertyStatus.Vacant }, { "入住", PropertyStatus.Occupied }, { "已入住", PropertyStatus.Occupied }, { "装修中", PropertyStatus.Renovating } };
        private static readonly Dictionary<string, OwnerIdCardType> IdCardTypeMap = new Dictionary<string, OwnerIdCardType>
        { { "身份证", OwnerIdCardType.IdCard }, { "护照", OwnerIdCardType.Passport }, { "户口簿", OwnerIdCardType.Hukou }, { "其他", OwnerIdCardType.Other } };
        private static readonly Dictionary<string, OwnerStatus> OwnerStatusMap = new Dictionary<string, OwnerStatus>
        { { "在住", OwnerStatus.Living }, { "搬离", OwnerStatus.MovedOut } };
        private static readonly Dictionary<string, ParkingSpaceType> ParkingTypeMap = new Dictionary<string, ParkingSpaceType>
        { { "产权", ParkingSpaceType.PropertyRight }, { "普通", ParkingSpaceType.CivilDefense }, { "临时", ParkingSpaceType.Temporary } };
        private static readonly Dictionary<string, ParkingSpaceStatus> ParkingStatusMap = new Dictionary<string, ParkingSpaceStatus>
        { { "已售", ParkingSpaceStatus.Owned }, { "已租", ParkingSpaceStatus.Rented }, { "空置", ParkingSpaceStatus.Vacant }, { "维修中", ParkingSpaceStatus.Repairing } };
        private static readonly Dictionary<string, OwnerRelType> OwnerRelTypeMap = new Dictionary<string, OwnerRelType>
        { { "业主", OwnerRelType.Owner }, { "共有人", OwnerRelType.CoOwner }, { "租户备案", OwnerRelType.RentRecord } };
        private static readonly Dictionary<string, OwnerRelStatus> OwnerRelStatusMap = new Dictionary<string, OwnerRelStatus>
        { { "有效", OwnerRelStatus.Active }, { "正常", OwnerRelStatus.Active }, { "即将到期", OwnerRelStatus.Expiring }, { "已解除", OwnerRelStatus.Released } };

        /// <summary>解析中文标签/枚举名/数字为枚举值；命中返回 true，否则 false（调用方对非空非法值报错）。</summary>
        private static bool TryParseLabeled<TEnum>(string text, Dictionary<string, TEnum> map, out TEnum value) where TEnum : struct
        {
            value = default(TEnum);
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            if (map.TryGetValue(t, out value)) return true;
            if (Enum.TryParse(t, true, out value)) return true;
            if (int.TryParse(t, out int intVal) && Enum.IsDefined(typeof(TEnum), intVal)) { value = (TEnum)Enum.ToObject(typeof(TEnum), intVal); return true; }
            return false;
        }

        private TResult WithConnection<TResult>(Func<IDbConnection, TResult> action)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return action(connection);
            }
        }

        private TResult WithTransaction<TResult>(Func<IDbConnection, IDbTransaction, TResult> action)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                var result = action(connection, transaction);
                transaction.Commit();
                return result;
            }
        }

        private void WithTransaction(Action<IDbConnection, IDbTransaction> action)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                action(connection, transaction);
                transaction.Commit();
            }
        }
    }
}
