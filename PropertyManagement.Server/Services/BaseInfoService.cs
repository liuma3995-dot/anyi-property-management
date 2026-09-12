using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Dapper;
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
    /// 业务规则：BR-INF-01（房号唯一）、BR-INF-02（一房一业主）、BR-INF-03（车位唯一/人防禁售）、
    /// BR-INF-04（变更留痕）、BR-INF-05（导入错误行不入库）。
    /// </summary>
    public class BaseInfoService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IBaseInfoRepository _repo;

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
                    throw ApiException.Conflict(unitId.HasValue ? "该单元下房号已存在（BR-INF-01）" : "该楼栋下房号已存在（BR-INF-01）");

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
                if (string.IsNullOrWhiteSpace(request.Phone)) throw ApiException.ValidationFailed("联系电话不能为空");

                var owner = new OwnerDto
                {
                    Name = request.Name.Trim(),
                    IdCardType = request.IdCardType,
                    IdCard = (request.IdCard ?? string.Empty).Trim(),
                    Phone = request.Phone.Trim(),
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
                    if (ownerCount > 0) throw ApiException.Conflict("该房产已存在一名业主（一房仅一名业主 BR-INF-02）");
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
                if (request.SpaceType == ParkingSpaceType.PropertyRight && request.Status == ParkingSpaceStatus.Owned && request.PropertyId == null)
                    throw ApiException.ValidationFailed("产权车位标记为已售需绑定房产");

                // 人防禁售：人防车位不可为已售
                if (request.SpaceType == ParkingSpaceType.CivilDefense && request.Status == ParkingSpaceStatus.Owned)
                    throw ApiException.ValidationFailed("人防车位不可标为出售（BR-INF-03）");

                string spaceNo = request.SpaceNo.Trim();
                var dup = _repo.GetParkingByNo(connection, spaceNo, id);
                if (dup != null) throw ApiException.Conflict("车位编号已存在（BR-INF-03）");

                // 一房最多绑定一个产权车位
                if (request.SpaceType == ParkingSpaceType.PropertyRight && request.PropertyId.HasValue && request.PropertyId > 0)
                {
                    int bound = _repo.CountParkingsBoundToProperty(connection, transaction, request.PropertyId.Value, id, (int)ParkingSpaceType.PropertyRight);
                    if (bound > 0) throw ApiException.Conflict("该房产已绑定一个产权车位（BR-INF-03）");
                }

                var parking = new ParkingSpaceDto
                {
                    SpaceNo = spaceNo,
                    Area = request.Area,
                    SpaceType = request.SpaceType,
                    Status = request.Status,
                    PropertyId = request.PropertyId,
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
                int total = 0;
                try
                {
                    using (var stream = new MemoryStream(request.FileContent))
                    using (var workbook = new XLWorkbook(stream))
                    {
                        IXLWorksheet sheet = workbook.Worksheets.FirstOrDefault();
                        if (sheet == null) throw ApiException.BadRequest("Excel 未包含工作表");
                        total = sheet.LastRowUsed() == null ? 0 : sheet.LastRowUsed().RowNumber() - 1;
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
                            case ImportModule.Property: success = ImportProperties(connection, transaction, sheet, errors); break;
                            case ImportModule.Owner: success = ImportOwners(connection, transaction, sheet, errors); break;
                            case ImportModule.Parking: success = ImportParkings(connection, transaction, sheet, errors); break;
                            case ImportModule.OwnerRelation: success = ImportRelations(connection, transaction, sheet, errors); break;
                            default: throw ApiException.BadRequest("不支持的数据类型");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new ImportErrorItemDto { RowNo = 0, Field = "文件", Content = string.Empty, Reason = ex.Message, Suggestion = "请检查文件格式后重试" });
                }

                int fail = errors.Count;
                ImportStatus status = total == 0 ? ImportStatus.Failed
                    : (fail == 0 ? ImportStatus.Success
                        : (success > 0 ? ImportStatus.PartialSuccess : ImportStatus.Failed));
                string errorFile = fail > 0 ? BuildErrorFile(log.Id, errors) : null;

                _repo.UpdateImportLog(connection, transaction, log.Id, status, success, fail, errorFile);
                if (fail > 0) _repo.InsertImportErrors(connection, transaction, errors, log.Id);
                transaction.Commit();

                log.Success = success;
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

        public byte[] BuildTemplate(ImportModule module)
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("模板");
                string[] headers;
                switch (module)
                {
                    case ImportModule.Property:
                        headers = new[] { "楼栋号", "单元号", "房号", "建筑面积", "用途", "状态" }; break;
                    case ImportModule.Owner:
                        headers = new[] { "姓名", "证件类型", "证件号", "联系电话", "常住地址", "紧急联系人", "紧急联系人电话", "入住日期", "状态" }; break;
                    case ImportModule.Parking:
                        headers = new[] { "车位编号", "区域", "类型", "状态", "绑定楼栋号", "绑定单元号", "绑定房号", "租金", "租期至" }; break;
                    case ImportModule.OwnerRelation:
                        headers = new[] { "楼栋号", "单元号", "房号", "业主姓名", "业主证件号", "业主电话", "关系类型", "份额", "起始日期", "终止日期", "状态" }; break;
                    default:
                        throw ApiException.BadRequest("不支持的数据类型");
                }
                for (int i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
                sheet.SheetView.FreezeRows(1);
                for (int col = 1; col <= headers.Length; col++) sheet.Column(col).Width = 18;

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        // ============================ 私有辅助 ============================
        private int ImportProperties(IDbConnection c, IDbTransaction tx, IXLWorksheet sheet, List<ImportErrorItemDto> errors)
        {
            int success = 0;
            int row = 2;
            while (row <= sheet.LastRowUsed().RowNumber())
            {
                string buildingNo = Cell(sheet, row, 1);
                string unitNo = Cell(sheet, row, 2);
                string roomNo = Cell(sheet, row, 3);
                string areaText = Cell(sheet, row, 4);
                string usageText = Cell(sheet, row, 5);
                string statusText = Cell(sheet, row, 6);

                var rowErrors = new List<ImportErrorItemDto>();
                if (string.IsNullOrWhiteSpace(buildingNo)) rowErrors.Add(Err(row, "楼栋号", buildingNo, "楼栋号不能为空", "填写正确的楼栋号"));
                if (string.IsNullOrWhiteSpace(unitNo)) rowErrors.Add(Err(row, "单元号", unitNo, "单元号不能为空", "填写正确的单元号"));
                if (string.IsNullOrWhiteSpace(roomNo)) rowErrors.Add(Err(row, "房号", roomNo, "房号不能为空", "填写正确的房号"));
                decimal area = 0;
                if (!decimal.TryParse(areaText, out area) || area <= 0) rowErrors.Add(Err(row, "建筑面积", areaText, "建筑面积必须为正数", "填写数字，如 88.5"));
                PropertyUsage usage = PropertyUsage.Residential;
                if (!string.IsNullOrWhiteSpace(usageText) && !TryParseLabeled(usageText, PropertyUsageMap, out usage))
                    rowErrors.Add(Err(row, "用途", usageText, "用途不合法（住宅/商铺）", "填写 住宅/商铺"));
                PropertyStatus propStatus = PropertyStatus.Vacant;
                if (!string.IsNullOrWhiteSpace(statusText) && !TryParseLabeled(statusText, PropertyStatusMap, out propStatus))
                    rowErrors.Add(Err(row, "状态", statusText, "状态不合法（空置/已入住/装修中）", "填写 空置/已入住/装修中"));

                if (rowErrors.Count == 0)
                {
                    buildingNo = buildingNo.Trim(); unitNo = unitNo.Trim(); roomNo = roomNo.Trim();
                    int? unitId = ResolveUnit(c, tx, buildingNo, unitNo);
                    if (!unitId.HasValue) rowErrors.Add(Err(row, "楼栋号/单元号", buildingNo + "-" + unitNo, "找不到对应的楼栋/单元", "请先维护楼栋/单元"));
                    else if (_repo.GetPropertyByUnitRoom(c, tx, unitId.Value, roomNo, 0) != null)
                        rowErrors.Add(Err(row, "房号", roomNo, "该单元下房号已存在（BR-INF-01）", "更换房号或先删除既有房产"));
                    else
                    {
                        _repo.InsertProperty(c, tx, new PropertyDto
                        {
                            BuildingId = c.ExecuteScalar<int?>("SELECT building_id FROM t_unit WHERE id = @uid", new { uid = unitId.Value }, tx),
                            UnitId = unitId.Value,
                            RoomNo = roomNo,
                            Area = area,
                            Usage = usage,
                            Status = propStatus
                        });
                        success++;
                    }
                }
                errors.AddRange(rowErrors);
                row++;
            }
            return success;
        }

        private int ImportOwners(IDbConnection c, IDbTransaction tx, IXLWorksheet sheet, List<ImportErrorItemDto> errors)
        {
            int success = 0;
            int row = 2;
            while (row <= sheet.LastRowUsed().RowNumber())
            {
                string name = Cell(sheet, row, 1);
                string idType = Cell(sheet, row, 2);
                string idCard = Cell(sheet, row, 3);
                string phone = Cell(sheet, row, 4);
                string address = Cell(sheet, row, 5);
                string emergency = Cell(sheet, row, 6);
                string emergencyPhone = Cell(sheet, row, 7);
                string checkIn = Cell(sheet, row, 8);
                string statusText = Cell(sheet, row, 9);

                var rowErrors = new List<ImportErrorItemDto>();
                if (string.IsNullOrWhiteSpace(name)) rowErrors.Add(Err(row, "姓名", name, "姓名不能为空", "填写姓名"));
                if (string.IsNullOrWhiteSpace(phone)) rowErrors.Add(Err(row, "联系电话", phone, "联系电话不能为空", "填写联系电话"));
                OwnerIdCardType idCardType = OwnerIdCardType.IdCard;
                if (!string.IsNullOrWhiteSpace(idType) && !TryParseLabeled(idType, IdCardTypeMap, out idCardType))
                    rowErrors.Add(Err(row, "证件类型", idType, "证件类型不合法（身份证/护照/户口簿/其他）", "填写 身份证/护照/户口簿/其他"));
                OwnerStatus ownerStatus = OwnerStatus.Living;
                if (!string.IsNullOrWhiteSpace(statusText) && !TryParseLabeled(statusText, OwnerStatusMap, out ownerStatus))
                    rowErrors.Add(Err(row, "状态", statusText, "状态不合法（在住/搬离）", "填写 在住/搬离"));

                if (rowErrors.Count == 0)
                {
                    string n = name.Trim();
                    string ph = phone.Trim();
                    string ic = string.IsNullOrWhiteSpace(idCard) ? string.Empty : idCard.Trim();
                    // 证件号可选：有证件号按（姓名+证件号）去重，无证件号按（姓名+电话）去重
                    string dedupSql = string.IsNullOrWhiteSpace(ic)
                        ? "SELECT COUNT(1) FROM t_owner WHERE name = @name AND phone = @phone AND del_flag = 0"
                        : "SELECT COUNT(1) FROM t_owner WHERE name = @name AND id_card = @idCard AND del_flag = 0";
                    int existingOwner = c.ExecuteScalar<int>(dedupSql, new { name = n, idCard = ic, phone = ph }, tx);
                    if (existingOwner > 0) rowErrors.Add(Err(row, "证件号", idCard, "该业主已存在", "跳过或先查询既有业主"));
                    else
                    {
                        _repo.InsertOwner(c, tx, new OwnerDto
                        {
                            Name = n,
                            IdCardType = idCardType,
                            IdCard = string.IsNullOrWhiteSpace(ic) ? null : ic,
                            Phone = ph,
                            ResidentAddress = address,
                            EmergencyContactName = emergency,
                            EmergencyContactPhone = emergencyPhone,
                            CheckInDate = ParseDate(checkIn),
                            Status = ownerStatus
                        });
                        success++;
                    }
                }
                errors.AddRange(rowErrors);
                row++;
            }
            return success;
        }

        private int ImportParkings(IDbConnection c, IDbTransaction tx, IXLWorksheet sheet, List<ImportErrorItemDto> errors)
        {
            int success = 0;
            int row = 2;
            while (row <= sheet.LastRowUsed().RowNumber())
            {
                string spaceNo = Cell(sheet, row, 1);
                string area = Cell(sheet, row, 2);
                string typeText = Cell(sheet, row, 3);
                string statusText = Cell(sheet, row, 4);
                string bindBuildingNo = Cell(sheet, row, 5);
                string bindUnitNo = Cell(sheet, row, 6);
                string bindRoomNo = Cell(sheet, row, 7);
                string rentText = Cell(sheet, row, 8);
                string rentToText = Cell(sheet, row, 9);

                var rowErrors = new List<ImportErrorItemDto>();
                if (string.IsNullOrWhiteSpace(spaceNo)) rowErrors.Add(Err(row, "车位编号", spaceNo, "车位编号不能为空", "填写车位编号"));

                ParkingSpaceType spaceType = ParkingSpaceType.PropertyRight;
                if (!string.IsNullOrWhiteSpace(typeText) && !TryParseLabeled(typeText, ParkingTypeMap, out spaceType))
                    rowErrors.Add(Err(row, "类型", typeText, "类型不合法（产权/人防/临时）", "填写 产权/人防/临时"));
                ParkingSpaceStatus status = ParkingSpaceStatus.Vacant;
                if (!string.IsNullOrWhiteSpace(statusText) && !TryParseLabeled(statusText, ParkingStatusMap, out status))
                    rowErrors.Add(Err(row, "状态", statusText, "状态不合法（已售/已租/空置/维修中）", "填写 已售/已租/空置/维修中"));
                if (spaceType == ParkingSpaceType.CivilDefense && status == ParkingSpaceStatus.Owned)
                    rowErrors.Add(Err(row, "类型/状态", typeText + "-" + statusText, "人防车位不可标为出售（BR-INF-03）", "改为已租/空置"));
                decimal monthlyRent = 0;
                if (!string.IsNullOrWhiteSpace(rentText) && !decimal.TryParse(rentText, out monthlyRent))
                    rowErrors.Add(Err(row, "租金", rentText, "数字格式不正确", "填写数字"));
                DateTime? rentTo = null;
                if (!string.IsNullOrWhiteSpace(rentToText) && !DateTime.TryParse(rentToText, out DateTime rd))
                    rowErrors.Add(Err(row, "租期至", rentToText, "日期格式不正确", "填写 yyyy-MM-dd"));

                if (rowErrors.Count == 0)
                {
                    string key = spaceNo.Trim();
                    if (_repo.GetParkingByNo(c, key, 0) != null) rowErrors.Add(Err(row, "车位编号", key, "车位编号已存在（BR-INF-03）", "更换编号"));
                    else
                    {
                        int? propertyId = null;
                        int? ownerId = null;
                        bool hasBind = !string.IsNullOrWhiteSpace(bindBuildingNo) || !string.IsNullOrWhiteSpace(bindUnitNo) || !string.IsNullOrWhiteSpace(bindRoomNo);
                        if (hasBind)
                        {
                            if (string.IsNullOrWhiteSpace(bindBuildingNo) || string.IsNullOrWhiteSpace(bindUnitNo) || string.IsNullOrWhiteSpace(bindRoomNo))
                                rowErrors.Add(Err(row, "绑定房产", bindBuildingNo + "-" + bindUnitNo + "-" + bindRoomNo, "绑定房产需填写完整的楼栋号/单元号/房号", "填写 楼栋号/单元号/房号"));
                            else
                            {
                                propertyId = ResolvePropertyByKey(c, tx, bindBuildingNo.Trim(), bindUnitNo.Trim(), bindRoomNo.Trim());
                                if (!propertyId.HasValue) rowErrors.Add(Err(row, "绑定房产", bindBuildingNo + "-" + bindUnitNo + "-" + bindRoomNo, "找不到该房产", "填写存在的楼栋/单元/房号"));
                            }
                        }
                        if (rowErrors.Count == 0)
                        {
                            if (spaceType == ParkingSpaceType.PropertyRight && propertyId.HasValue &&
                                _repo.CountParkingsBoundToProperty(c, tx, propertyId.Value, 0, (int)ParkingSpaceType.PropertyRight) > 0)
                                rowErrors.Add(Err(row, "绑定房产", bindBuildingNo + "-" + bindUnitNo + "-" + bindRoomNo, "该房产已绑定一个产权车位（BR-INF-03）", "更换房产"));
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
                                MonthlyRent = monthlyRent > 0 ? monthlyRent : (decimal?)null,
                                RentTo = rentTo
                            });
                            success++;
                        }
                    }
                }
                errors.AddRange(rowErrors);
                row++;
            }
            return success;
        }

        private int ImportRelations(IDbConnection c, IDbTransaction tx, IXLWorksheet sheet, List<ImportErrorItemDto> errors)
        {
            int success = 0;
            int row = 2;
            while (row <= sheet.LastRowUsed().RowNumber())
            {
                string buildingNo = Cell(sheet, row, 1);
                string unitNo = Cell(sheet, row, 2);
                string roomNo = Cell(sheet, row, 3);
                string ownerName = Cell(sheet, row, 4);
                string ownerIdCard = Cell(sheet, row, 5);
                string ownerPhone = Cell(sheet, row, 6);
                string relTypeText = Cell(sheet, row, 7);
                string shareText = Cell(sheet, row, 8);
                string startText = Cell(sheet, row, 9);
                string endText = Cell(sheet, row, 10);
                string statusText = Cell(sheet, row, 11);

                var rowErrors = new List<ImportErrorItemDto>();
                if (string.IsNullOrWhiteSpace(buildingNo)) rowErrors.Add(Err(row, "楼栋号", buildingNo, "楼栋号不能为空", "填写楼栋号"));
                if (string.IsNullOrWhiteSpace(unitNo)) rowErrors.Add(Err(row, "单元号", unitNo, "单元号不能为空", "填写单元号"));
                if (string.IsNullOrWhiteSpace(roomNo)) rowErrors.Add(Err(row, "房号", roomNo, "房号不能为空", "填写房号"));
                if (string.IsNullOrWhiteSpace(ownerName)) rowErrors.Add(Err(row, "业主姓名", ownerName, "业主姓名不能为空", "填写业主姓名"));
                OwnerRelType relType = OwnerRelType.Owner;
                if (!string.IsNullOrWhiteSpace(relTypeText) && !TryParseLabeled(relTypeText, OwnerRelTypeMap, out relType))
                    rowErrors.Add(Err(row, "关系类型", relTypeText, "关系类型不合法（业主/共有人/租户备案）", "填写 业主/共有人/租户备案"));
                decimal share = 0;
                if (!string.IsNullOrWhiteSpace(shareText) && !decimal.TryParse(shareText, out share)) rowErrors.Add(Err(row, "份额", shareText, "数字格式不正确", "填写 0~100"));

                if (rowErrors.Count == 0)
                {
                    int? propertyId = ResolvePropertyByKey(c, tx, buildingNo.Trim(), unitNo.Trim(), roomNo.Trim());
                    int? ownerId = ResolveOwner(c, tx, ownerName.Trim(), ownerIdCard, ownerPhone);
                    if (!propertyId.HasValue) rowErrors.Add(Err(row, "房号", buildingNo + "-" + unitNo + "-" + roomNo, "找不到该房产", "请先维护房产"));
                    else if (!ownerId.HasValue) rowErrors.Add(Err(row, "业主姓名", ownerName, "找不到该业主（重名请填写业主证件号或电话）", "请先维护业主或填写证件号/电话"));
                    else
                    {
                        DateTime effectiveAt = ParseDate(startText) ?? DateTime.Today;
                        DateTime? expireAt = ParseDate(endText);
                        if (relType == OwnerRelType.Owner && _repo.CountActiveOwnerRelationsByProperty(c, tx, propertyId.Value, 0) > 0)
                            rowErrors.Add(Err(row, "关系类型", relTypeText, "该房产已有一名业主（一房仅一名业主 BR-INF-02）", "改为共有人/租户备案"));
                        if (rowErrors.Count == 0)
                        {
                            decimal defaultShare = relType == OwnerRelType.Owner ? 100m : (relType == OwnerRelType.CoOwner ? 50m : 0m);
                            decimal effShare = share <= 0 ? defaultShare : share;
                            if (effShare < 0 || effShare > 100) rowErrors.Add(Err(row, "份额", shareText, "份额须在 0~100 之间", "填写 0~100"));
                        }
                        if (rowErrors.Count == 0)
                        {
                            _repo.InsertRelation(c, tx, new OwnerPropertyRelationDto
                            {
                                PropertyId = propertyId.Value,
                                OwnerId = ownerId.Value,
                                RelType = relType,
                                Share = share <= 0 ? (relType == OwnerRelType.Owner ? 100m : (relType == OwnerRelType.CoOwner ? 50m : 0m)) : share,
                                EffectiveAt = effectiveAt,
                                ExpireAt = expireAt,
                                Status = ResolveRelStatus(expireAt)
                            });
                            success++;
                        }
                    }
                }
                errors.AddRange(rowErrors);
                row++;
            }
            return success;
        }

        private void LogOwnerChanges(IDbConnection c, IDbTransaction tx, OwnerDto before, OwnerDto after)
        {
            if (!Equals(before.Phone, after.Phone))
                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, after.Id, "联系电话", before.Phone, after.Phone, "后台维护");
            if (!Equals(before.EmergencyContactName, after.EmergencyContactName))
                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, after.Id, "紧急联系人", before.EmergencyContactName, after.EmergencyContactName, "后台维护");
            if (!Equals(before.EmergencyContactPhone, after.EmergencyContactPhone))
                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, after.Id, "紧急联系人电话", before.EmergencyContactPhone, after.EmergencyContactPhone, "后台维护");
            if (!Equals(before.ResidentAddress, after.ResidentAddress))
                WriteChangeLog(c, tx, BaseChangeObjectType.Owner, after.Id, "常住地址", before.ResidentAddress, after.ResidentAddress, "后台维护");
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

        /// <summary>按 姓名+证件号/电话 精确匹配业主，消除同名歧义；无证件/电话时按姓名（唯一）或提示重名。</summary>
        private int? ResolveOwner(IDbConnection c, IDbTransaction tx, string name, string idCard, string phone)
        {
            string ic = string.IsNullOrWhiteSpace(idCard) ? string.Empty : idCard.Trim();
            string ph = string.IsNullOrWhiteSpace(phone) ? string.Empty : phone.Trim();
            if (!string.IsNullOrWhiteSpace(ic))
            {
                var ids = c.Query<int>("SELECT id FROM t_owner WHERE name = @name AND id_card = @idCard AND del_flag = 0", new { name, idCard = ic }, tx).ToList();
                if (ids.Count == 1) return ids[0];
                return null;
            }
            if (!string.IsNullOrWhiteSpace(ph))
            {
                var ids = c.Query<int>("SELECT id FROM t_owner WHERE name = @name AND phone = @phone AND del_flag = 0", new { name, phone = ph }, tx).ToList();
                if (ids.Count == 1) return ids[0];
                return null;
            }
            var byName = c.Query<int>("SELECT id FROM t_owner WHERE name = @name AND del_flag = 0", new { name }, tx).ToList();
            if (byName.Count == 1) return byName[0];
            return null;
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

        private static string Cell(IXLWorksheet sheet, int row, int col)
        {
            var cell = sheet.Cell(row, col);
            return cell.IsEmpty() ? string.Empty : cell.GetString().Trim();
        }

        private static ImportErrorItemDto Err(int row, string field, string content, string reason, string suggestion) =>
            new ImportErrorItemDto { RowNo = row, Field = field, Content = content, Reason = reason, Suggestion = suggestion };

        private static DateTime? ParseDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            DateTime result;
            return DateTime.TryParse(text, out result) ? result : (DateTime?)null;
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

        // ===== 导入枚举中文标签映射（模板列直接写中文：产权/人防/已售…） =====
        private static readonly Dictionary<string, PropertyUsage> PropertyUsageMap = new Dictionary<string, PropertyUsage>
        { { "住宅", PropertyUsage.Residential }, { "商铺", PropertyUsage.Commercial } };
        private static readonly Dictionary<string, PropertyStatus> PropertyStatusMap = new Dictionary<string, PropertyStatus>
        { { "空置", PropertyStatus.Vacant }, { "入住", PropertyStatus.Occupied }, { "已入住", PropertyStatus.Occupied }, { "装修中", PropertyStatus.Renovating } };
        private static readonly Dictionary<string, OwnerIdCardType> IdCardTypeMap = new Dictionary<string, OwnerIdCardType>
        { { "身份证", OwnerIdCardType.IdCard }, { "护照", OwnerIdCardType.Passport }, { "户口簿", OwnerIdCardType.Hukou }, { "其他", OwnerIdCardType.Other } };
        private static readonly Dictionary<string, OwnerStatus> OwnerStatusMap = new Dictionary<string, OwnerStatus>
        { { "在住", OwnerStatus.Living }, { "搬离", OwnerStatus.MovedOut } };
        private static readonly Dictionary<string, ParkingSpaceType> ParkingTypeMap = new Dictionary<string, ParkingSpaceType>
        { { "产权", ParkingSpaceType.PropertyRight }, { "人防", ParkingSpaceType.CivilDefense }, { "临时", ParkingSpaceType.Temporary } };
        private static readonly Dictionary<string, ParkingSpaceStatus> ParkingStatusMap = new Dictionary<string, ParkingSpaceStatus>
        { { "已售", ParkingSpaceStatus.Owned }, { "已租", ParkingSpaceStatus.Rented }, { "空置", ParkingSpaceStatus.Vacant }, { "维修中", ParkingSpaceStatus.Repairing } };
        private static readonly Dictionary<string, OwnerRelType> OwnerRelTypeMap = new Dictionary<string, OwnerRelType>
        { { "业主", OwnerRelType.Owner }, { "共有人", OwnerRelType.CoOwner }, { "租户备案", OwnerRelType.RentRecord } };

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
