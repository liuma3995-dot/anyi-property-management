using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Services;
using PropertyManagement.Tests.Infrastructure;
using Xunit;

namespace PropertyManagement.Tests.Services
{
    /// <summary>
    /// 基础信息服务单元测试（BR-INF-01~05）。
    /// 说明：BR-INF-02 的「生成账单前须存在有效业主关系」现状未强制，见
    /// <see cref="GenerateBill_房产无有效业主关系_现状放行并登记BUG002"/>（待负责人裁决）。
    /// </summary>
    public class BaseInfoServiceTests : DbTestBase
    {
        private readonly BaseInfoService _service = new BaseInfoService();

        // ===================== BR-INF-01 楼栋-单元-房产层级唯一，房号不重复 =====================

        [Fact]
        public void SaveProperty_同单元同房号_抛Conflict()
        {
            int community = TestData.Community("房号唯一小区");
            int building = TestData.Building(community, "1");
            int unit = TestData.Unit(building, "1");
            TestData.Property(building, "101", unit);

            var ex = Assert.Throws<ApiException>(() => _service.SaveProperty(0, new PropertyRequest
            {
                BuildingId = building, UnitId = unit, RoomNo = "101", Area = 88m,
                Usage = PropertyUsage.Residential, Status = PropertyStatus.Vacant
            }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("房号已存在", ex.Message);
        }

        [Fact]
        public void SaveProperty_无单元同楼栋同房号_抛Conflict()
        {
            int community = TestData.Community("无单元房号小区");
            int building = TestData.Building(community, "2");
            TestData.Property(building, "202");

            var ex = Assert.Throws<ApiException>(() => _service.SaveProperty(0, new PropertyRequest
            {
                BuildingId = building, RoomNo = "202", Area = 66m,
                Usage = PropertyUsage.Residential, Status = PropertyStatus.Vacant
            }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("该楼栋下房号已存在", ex.Message);
        }

        [Fact]
        public void SaveProperty_不同楼栋同房号_通过()
        {
            int community = TestData.Community("跨楼栋小区");
            int buildingA = TestData.Building(community, "1");
            int buildingB = TestData.Building(community, "2");
            TestData.Property(buildingA, "101");

            int created = TestData.Property(buildingB, "101");

            Assert.True(created > 0);
            Assert.Equal(2, ScalarInt("SELECT COUNT(1) FROM t_property WHERE room_no = '101' AND del_flag = 0"));
        }

        [Fact]
        public void SaveProperty_所选单元不属于该楼栋_抛ValidationFailed()
        {
            int community = TestData.Community("单元归属小区");
            int buildingA = TestData.Building(community, "1");
            int buildingB = TestData.Building(community, "2");
            int unitOfB = TestData.Unit(buildingB, "1");

            var ex = Assert.Throws<ApiException>(() => _service.SaveProperty(0, new PropertyRequest
            {
                BuildingId = buildingA, UnitId = unitOfB, RoomNo = "303", Area = 90m,
                Usage = PropertyUsage.Residential, Status = PropertyStatus.Vacant
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("所选单元不属于该楼栋", ex.Message);
        }

        // ===================== BR-INF-02 业主-房产关系（现状口径） =====================

        [Fact]
        public void SaveRelation_同房产再绑定第二名业主_抛Conflict()
        {
            int property = NewProperty("一房一主小区");
            TestData.Relation(property, TestData.Owner("业主甲", "13800001111"));

            var ex = Assert.Throws<ApiException>(() => _service.SaveRelation(0, new OwnerPropertyRelationRequest
            {
                PropertyId = property, OwnerId = TestData.Owner("业主乙", "13800002222"),
                RelType = OwnerRelType.Owner, Share = 100m, EffectiveAt = DateTime.Today, Status = OwnerRelStatus.Active
            }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("已存在一名业主", ex.Message);
        }

        [Fact]
        public void SaveRelation_同一业主绑定不同房产_通过()
        {
            int community = TestData.Community("多房产业主小区");
            int building = TestData.Building(community, "1");
            int firstProperty = TestData.Property(building, "101");
            int secondProperty = TestData.Property(building, "102");
            int owner = TestData.Owner("多房产业主", "13800003333");

            int first = TestData.Relation(firstProperty, owner);
            int second = TestData.Relation(secondProperty, owner);

            Assert.True(first > 0 && second > 0);
        }

        // ===================== BR-INF-03 固定车位同一时间只能绑定一个对象 =====================

        [Fact]
        public void SaveParking_车位编号重复_抛Conflict()
        {
            TestData.Parking("A-001");

            var ex = Assert.Throws<ApiException>(() => _service.SaveParking(0, new ParkingSpaceRequest
            {
                SpaceNo = "A-001", SpaceType = ParkingSpaceType.PropertyRight, Status = ParkingSpaceStatus.Vacant
            }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("车位编号已存在", ex.Message);
        }

        /// <summary>
        /// CHG-v1.1.0-12：车位不再手工绑定房产 —— 产权车位绑定业主后「自动引用」其名下有有效关系的房产；
        /// 因此「同一房产只能有一个产权车位」的口径改由业主维度校验（同一业主名下第二个产权车位被拒）。
        /// </summary>
        [Fact]
        public void SaveParking_同一业主名下第二个产权车位_抛Conflict()
        {
            int property = NewProperty("车位绑定小区");
            int owner = TestData.Owner("车位绑定业主", "13800008801");
            TestData.Relation(property, owner);
            TestData.Parking("B-001", ParkingSpaceType.PropertyRight, ParkingSpaceStatus.Owned, null, owner);

            var ex = Assert.Throws<ApiException>(() => _service.SaveParking(0, new ParkingSpaceRequest
            {
                SpaceNo = "B-002", SpaceType = ParkingSpaceType.PropertyRight,
                Status = ParkingSpaceStatus.Owned, OwnerId = owner
            }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("该房产已绑定一个产权车位", ex.Message);
        }

        [Fact]
        public void SaveParking_人防车位标记已售_抛ValidationFailed()
        {
            var ex = Assert.Throws<ApiException>(() => _service.SaveParking(0, new ParkingSpaceRequest
            {
                SpaceNo = "R-001", SpaceType = ParkingSpaceType.CivilDefense, Status = ParkingSpaceStatus.Owned
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("不可标为出售", ex.Message);
        }

        [Fact]
        public void SaveParking_固定车位空置未绑定_保存成功()
        {
            int parkingId = TestData.Parking("C-001");

            Assert.True(parkingId > 0);
            ParkingSpaceDto dto = _service.GetParking(parkingId);
            Assert.Equal("C-001", dto.SpaceNo);
            Assert.Equal(ParkingSpaceStatus.Vacant, dto.Status);
        }

        // ===================== BR-INF-04 联系方式/产权关系变更必须留痕 =====================

        [Fact]
        public void SaveOwner_联系方式变更_写变更留痕()
        {
            int ownerId = TestData.Owner("留痕业主", "13800004444");

            _service.SaveOwner(ownerId, new OwnerRequest
            {
                Name = "留痕业主", Phone = "13900005555", IdCardType = OwnerIdCardType.IdCard,
                IdCard = "110101199001011234", ResidentAddress = "测试地址", Status = OwnerStatus.Living
            });

            List<BaseChangeLogDto> logs = _service.GetChangeLogs((int)BaseChangeObjectType.Owner, ownerId);
            Assert.Equal(2, logs.Count); // 建档 + 变更
            BaseChangeLogDto change = Assert.Single(logs.Where(x => x.FieldName == "联系电话"));
            Assert.Equal("13800004444", change.OldValue);
            Assert.Equal("13900005555", change.NewValue);
        }

        [Fact]
        public void ReleaseRelation_解除关系_写留痕且关系不再有效()
        {
            int property = NewProperty("解除关系小区");
            int ownerId = TestData.Owner("解除关系业主", "13800006666");
            int relationId = TestData.Relation(property, ownerId);

            _service.ReleaseRelation(relationId, "业主出售房产");

            Assert.Equal(1, ScalarInt("SELECT del_flag FROM t_owner_property_rel WHERE id = @id", new { id = relationId }));
            List<BaseChangeLogDto> logs = _service.GetChangeLogs((int)BaseChangeObjectType.Relation, relationId);
            BaseChangeLogDto release = Assert.Single(logs.Where(x => x.FieldName == "解除"));
            Assert.Equal("解除：有效 → 已解除", release.ChangeContent);
            Assert.Contains("解除原因：业主出售房产", release.Channel); // 留痕含原因（建档 + 解除各一条）
        }

        [Fact]
        public void DeleteProperty_存在业主绑定关系_抛Conflict()
        {
            int property = NewProperty("删除保护小区");
            TestData.Relation(property, TestData.Owner("绑定业主", "13800007777"));

            var ex = Assert.Throws<ApiException>(() => _service.DeleteProperty(property));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("请先解除关系", ex.Message);
        }

        // ===================== v1.1.0 R1 删除入口跨模块引用校验（闭环） =====================

        [Fact]
        public void DeleteProperty_存在未删除账单_抛Conflict且提示先处理账单()
        {
            int property = NewProperty("账单引用小区");
            int owner = TestData.Owner("账单业主", "13800006666");
            TestData.Relation(property, owner);
            new BillingService().GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = TestData.ChargeItem("物业费R1", 1.5m),
                CycleId = TestData.Cycle(),
                PropertyIds = new List<int> { property },
                ParkingIds = new List<int>()
            });
            // 解除关系后仍不可删除（账单仍在）
            _service.ReleaseRelation(_service.QueryRelations(new BaseInfoQueryRequest { PageSize = 100 })
                .Items.First(x => x.PropertyId == property).Id, "R1 用例");

            var ex = Assert.Throws<ApiException>(() => _service.DeleteProperty(property));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("未删除账单", ex.Message);
            Assert.Equal(0, ScalarInt("SELECT del_flag FROM t_property WHERE id = @id", new { id = property }));
        }

        /// <summary>
        /// CHG-v1.1.0-12/06：产权车位经业主「自动引用」房产，故房产删除首先命中业主关系校验；
        /// 关系解除后车位仍引用该房产（未处理车位），此时命中跨模块引用校验（R1 口径）。
        /// </summary>
        [Fact]
        public void DeleteProperty_关系已解除但车位仍引用_抛Conflict且提示先解除车位绑定()
        {
            int property = NewProperty("车位引用小区");
            int owner = TestData.Owner("车位引用业主", "13800008802");
            int relationId = TestData.Relation(property, owner);
            TestData.Parking("R1-PK-1", ParkingSpaceType.PropertyRight, ParkingSpaceStatus.Owned, null, owner);
            _service.ReleaseRelation(relationId, "已过户");

            var ex = Assert.Throws<ApiException>(() => _service.DeleteProperty(property));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("绑定", ex.Message);
            Assert.Contains("车位", ex.Message);
        }

        [Fact]
        public void DeleteProperty_无任何跨模块引用_软删成功()
        {
            int property = NewProperty("无引用小区");

            _service.DeleteProperty(property);

            Assert.Equal(1, ScalarInt("SELECT del_flag FROM t_property WHERE id = @id", new { id = property }));
        }

        [Fact]
        public void DeleteParking_存在未删除账单_抛Conflict()
        {
            int owner = TestData.Owner("车位账单业主", "13800005555");
            int parking = TestData.Parking("R1-PK-2", ParkingSpaceType.Temporary, ParkingSpaceStatus.Rented,
                null, owner);
            new BillingService().GenerateBill(new BillGenerateRequest
            {
                ChargeItemId = TestData.ChargeItem("车位费R1", 120m, "parking"),
                CycleId = TestData.Cycle(),
                PropertyIds = new List<int>(),
                ParkingIds = new List<int> { parking }
            });

            var ex = Assert.Throws<ApiException>(() => _service.DeleteParking(parking));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("未删除账单", ex.Message);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_bill WHERE parking_id = @id AND del_flag = 0", new { id = parking }));
        }

        [Fact]
        public void DeleteOwner_存在车位绑定或预存款或纠纷当事人_抛Conflict()
        {
            int owner = TestData.Owner("受保护业主", "13800004444");
            TestData.Parking("R1-PK-3", ParkingSpaceType.Temporary, ParkingSpaceStatus.Rented, null, owner);

            var ex = Assert.Throws<ApiException>(() => _service.DeleteOwner(owner));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("解除绑定", ex.Message);
            Assert.Equal(0, ScalarInt("SELECT del_flag FROM t_owner WHERE id = @id", new { id = owner }));
        }

        [Fact]
        public void DeleteOwner_无任何跨模块引用_软删成功()
        {
            int owner = TestData.Owner("无引用业主", "13800003333");

            _service.DeleteOwner(owner);

            Assert.Equal(1, ScalarInt("SELECT del_flag FROM t_owner WHERE id = @id", new { id = owner }));
        }

        // ===================== BR-INF-05 Excel 导入按模板校验，错误行不影响正确行 =====================

        [Fact]
        public void Import_含错误行_正确行入库且错误清单可导出()
        {
            int community = TestData.Community("导入测试小区");
            int building = TestData.Building(community, "8");
            TestData.Unit(building, "1");
            byte[] file = BuildPropertyImportFile(validRoomNo: "801", invalidRoomNo: "");

            var result = _service.Import(new ImportRequest
            {
                Module = ImportModule.Property, FileName = "房产导入.xlsx", FileContent = file
            });

            Assert.Equal(ImportStatus.PartialSuccess, result.Batch.Status);
            Assert.Equal(1, result.Batch.Success);
            Assert.True(result.Batch.Fail >= 1);
            Assert.Equal(2, result.Batch.Total);   // v1.1.0：模板第 2 行示例不计入数据行
            Assert.NotEmpty(result.Errors);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_property WHERE room_no = '801' AND del_flag = 0"));
            byte[] errorBook = _service.BuildImportErrorsExcel(result.Batch.Id);
            Assert.True(errorBook.Length > 0, "错误清单应可导出为 Excel");
        }

        [Fact]
        public void Import_仅表头无数据行_标记失败并给出原因()
        {
            byte[] template = _service.BuildTemplate(ImportModule.Property);

            var result = _service.Import(new ImportRequest
            {
                Module = ImportModule.Property, FileName = "空模板.xlsx", FileContent = template
            });

            Assert.Equal(ImportStatus.Failed, result.Batch.Status);
            Assert.Contains(result.Errors, e => e.Reason.Contains("未包含数据行"));
        }

        // ===================== v1.1.0 导入模板与解析（F-05~F-08b） =====================

        [Fact]
        public void BuildTemplate_房产模板_含用途列与示例行与说明页()
        {
            byte[] file = _service.BuildTemplate(ImportModule.Property);
            using (var stream = new MemoryStream(file))
            using (var workbook = new XLWorkbook(stream))
            {
                IXLWorksheet sheet = workbook.Worksheets.First();
                List<string> headers = Enumerable.Range(1, 6).Select(c => sheet.Cell(1, c).GetString()).ToList();

                // v1.2.0（CHG-v1.2.0-03）：房产模板恢复「用途」列（选填）——收费规格的适用条件按房产用途判定
                Assert.Contains(headers, h => h.StartsWith("用途") && !h.EndsWith("*"));
                Assert.Contains(headers, h => h.StartsWith("楼栋号"));
                Assert.Contains(headers, h => h.StartsWith("建筑面积"));
                Assert.Contains(headers, h => h.StartsWith("单元号") && !h.EndsWith("*"));   // 单元号选填
                Assert.StartsWith("示例", sheet.Cell(2, 1).GetString());
                Assert.NotNull(workbook.Worksheets.FirstOrDefault(w => w.Name == "填写说明"));
            }
        }

        [Theory]
        // v1.2.0（CHG-v1.2.0-03）：房产模板恢复「用途」列 → 5 列变 6 列
        [InlineData(ImportModule.Property, "", 6)]
        [InlineData(ImportModule.Owner, "", 9)]
        // v1.1.2：车位模板下线「租金 / 租期至」两列（停车费统一由价目表定价）→ 9 列变 7 列
        [InlineData(ImportModule.Parking, "", 7)]
        [InlineData(ImportModule.OwnerRelation, "", 11)]
        public void BuildTemplate_示例行_每张模板都有(ImportModule module, string _, int columns)
        {
            byte[] file = _service.BuildTemplate(module);
            using (var stream = new MemoryStream(file))
            using (var workbook = new XLWorkbook(stream))
            {
                IXLWorksheet sheet = workbook.Worksheets.First();
                Assert.StartsWith("示例", sheet.Cell(2, 1).GetString());
                Assert.Equal(columns, Enumerable.Range(1, columns + 3).Count(c => sheet.Cell(1, c).GetString().Length > 0));
            }
        }

        [Theory]
        [InlineData(ImportModule.Property, "楼栋号", "单元号")]
        [InlineData(ImportModule.Owner, "姓名", "联系电话")]
        [InlineData(ImportModule.Parking, "车位编号", "绑定房号")]
        [InlineData(ImportModule.OwnerRelation, "业主姓名", "单元号")]
        public void BuildTemplate_必填标记_仅约定硬性必填列带星(ImportModule module, string required, string optional)
        {
            byte[] file = _service.BuildTemplate(module);
            using (var stream = new MemoryStream(file))
            using (var workbook = new XLWorkbook(stream))
            {
                IXLWorksheet sheet = workbook.Worksheets.First();
                List<string> headers = Enumerable.Range(1, 20).Select(c => sheet.Cell(1, c).GetString())
                    .Where(h => h.Length > 0).ToList();

                Assert.Contains(headers, h => h.StartsWith(required) && h.EndsWith("*"));
                Assert.Contains(headers, h => h.StartsWith(optional) && !h.EndsWith("*"));
            }
        }

        [Fact]
        public void Import_房产模板仅含示例行_不计入数据行()
        {
            byte[] template = _service.BuildTemplate(ImportModule.Property);

            var result = _service.Import(new ImportRequest
            {
                Module = ImportModule.Property, FileName = "模板.xlsx", FileContent = template
            });

            Assert.Equal(0, result.Batch.Total);
            Assert.Contains(result.Errors, e => e.Reason.Contains("未包含数据行"));
        }

        [Fact]
        public void Import_房产无单元号_按楼栋房号入库()
        {
            int community = TestData.Community("无单元导入小区");
            TestData.Building(community, "9");
            byte[] file = BuildSheet(
                new[] { "楼栋号", "单元号", "房号", "建筑面积", "状态" },
                new[] { new[] { "9", "", "901", "123.66", "空置" } });

            var result = _service.Import(new ImportRequest
            {
                Module = ImportModule.Property, FileName = "房产.xlsx", FileContent = file
            });

            Assert.Equal(1, result.Batch.Success);
            Assert.Equal("123.66", ScalarText("SELECT CAST(area AS TEXT) FROM t_property WHERE room_no = '901' AND del_flag = 0"));
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_property WHERE room_no = '901' AND unit_id IS NOT NULL"));
        }

        [Fact]
        public void Import_旧模板含用途列且列序不同_按表头解析()
        {
            int community = TestData.Community("旧模板兼容小区");
            TestData.Building(community, "7");
            byte[] file = BuildSheet(
                new[] { "房号", "楼栋号", "单元号", "用途", "建筑面积", "状态" },
                new[] { new[] { "701", "7", "", "住宅", "66.5", "空置" } });

            var result = _service.Import(new ImportRequest
            {
                Module = ImportModule.Property, FileName = "房产旧模板.xlsx", FileContent = file
            });

            Assert.Equal(1, result.Batch.Success);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_property WHERE room_no = '701' AND del_flag = 0"));
        }

        [Fact]
        public void Import_模板表头不匹配_给出缺少列提示()
        {
            byte[] file = BuildSheet(
                new[] { "车位编号", "区域" },
                new[] { new[] { "X-1", "B1" } });

            var result = _service.Import(new ImportRequest
            {
                Module = ImportModule.Property, FileName = "错模板.xlsx", FileContent = file
            });

            Assert.Equal(0, result.Batch.Success);
            Assert.Contains(result.Errors, e => e.Reason.Contains("缺少列"));
        }

        [Fact]
        public void Import_业主仅姓名_无电话可入库()
        {
            byte[] file = BuildSheet(
                new[] { "姓名", "联系电话" },
                new[] { new[] { "仅姓名业主", "" } });

            var result = _service.Import(new ImportRequest
            {
                Module = ImportModule.Owner, FileName = "业主.xlsx", FileContent = file
            });

            Assert.Equal(1, result.Batch.Success);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_owner WHERE name = '仅姓名业主' AND del_flag = 0"));
        }

        [Fact]
        public void Import_业主同名且无证件无电话_按覆盖口径更新既有档案()
        {
            int ownerId = TestData.Owner("同名业主", "13800008888");
            byte[] file = BuildSheet(
                new[] { "姓名" },
                new[] { new[] { "同名业主" } });

            var result = _service.Import(new ImportRequest
            {
                Module = ImportModule.Owner, FileName = "业主同名.xlsx", FileContent = file
            });

            // v1.1.2 I-01（负责人裁定 A）：重复数据改走「覆盖处理」——
            // 仅填姓名且命中唯一既有业主时按同一人覆盖（不计入 Success，计入 Updated），
            // 文件未填字段保留库内原值，不再报「该业主已存在」；
            // 仅当姓名命中多条（无法判定对象）时才报错要求补充证件号/电话。
            Assert.Equal(0, result.Batch.Success);
            Assert.Equal(1, result.Batch.Updated);
            Assert.Equal(0, result.Batch.Fail);
            Assert.Empty(result.Errors);
            Assert.Equal(1, ScalarInt(
                "SELECT COUNT(1) FROM t_owner WHERE id = @id AND phone = '13800008888' AND del_flag = 0",
                new { id = ownerId }));
        }

        [Fact]
        public void Import_关系无单元号_按楼栋房号定位()
        {
            int community = TestData.Community("关系导入小区");
            int building = TestData.Building(community, "5");
            TestData.Property(building, "501");
            TestData.Owner("关系导入业主", "13800009999");
            byte[] file = BuildSheet(
                new[] { "楼栋号", "单元号", "房号", "业主姓名" },
                new[] { new[] { "5", "", "501", "关系导入业主" } });

            var result = _service.Import(new ImportRequest
            {
                Module = ImportModule.OwnerRelation, FileName = "关系.xlsx", FileContent = file
            });

            Assert.Equal(1, result.Batch.Success);
            Assert.Equal(1, ScalarInt(
                "SELECT COUNT(1) FROM t_owner_property_rel r JOIN t_property p ON p.id = r.property_id " +
                "WHERE p.room_no = '501' AND r.del_flag = 0"));
        }

        [Fact]
        public void SaveOwner_无联系电话_可保存()
        {
            OwnerDto dto = _service.SaveOwner(0, new OwnerRequest
            {
                Name = "无电话业主", Phone = "", Status = OwnerStatus.Living
            });

            Assert.True(dto.Id > 0);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_owner WHERE name = '无电话业主' AND del_flag = 0"));
        }

        [Fact]
        public void SaveBuilding_软删留痕后_可重建同名()
        {
            int community = TestData.Community("软删重建小区");
            BuildingDto first = _service.SaveBuilding(0, new BuildingRequest { CommunityId = community, BuildingNo = "A" });
            _service.DeleteBuilding(first.Id);

            BuildingDto again = _service.SaveBuilding(0, new BuildingRequest { CommunityId = community, BuildingNo = "A" });

            Assert.True(again.Id > 0);
            Assert.NotEqual(first.Id, again.Id);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_building WHERE building_no = 'A' AND del_flag = 0"));
        }

        // ===================== v1.1.0 第 4 轮：软删留痕后的唯一性（migration_034） =====================

        [Fact]
        public void ReleaseRelation_解除留痕后_可重新绑定同一业主同一天()
        {
            int property = NewProperty("解除重绑小区");
            int ownerId = TestData.Owner("解除重绑业主", "13800001234");
            int first = TestData.Relation(property, ownerId);   // 生效日期 = 今天

            _service.ReleaseRelation(first, "业主出售房产");

            // 修复前：解除留痕行仍占用 UNIQUE(property_id, owner_id, effective_at) → 抛约束异常
            OwnerPropertyRelationDto again = _service.SaveRelation(0, new OwnerPropertyRelationRequest
            {
                PropertyId = property, OwnerId = ownerId, RelType = OwnerRelType.Owner,
                Share = 100m, EffectiveAt = DateTime.Today, Status = OwnerRelStatus.Active
            });

            Assert.True(again.Id > 0);
            Assert.NotEqual(first, again.Id);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_owner_property_rel WHERE property_id = @p AND del_flag = 0", new { p = property }));
        }

        [Fact]
        public void DeleteParking_软删留痕后_可重建同编号车位()
        {
            int parking = TestData.Parking("P-100");

            _service.DeleteParking(parking);

            ParkingSpaceDto again = _service.SaveParking(0, new ParkingSpaceRequest
            {
                SpaceNo = "P-100", SpaceType = ParkingSpaceType.Temporary, Status = ParkingSpaceStatus.Vacant
            });

            Assert.True(again.Id > 0);
            Assert.NotEqual(parking, again.Id);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_parking_space WHERE space_no = 'P-100' AND del_flag = 0"));
        }

        [Fact]
        public void DictItem_软删留痕后_同编码可重建()
        {
            Execute("INSERT INTO t_dict_item (type_code, item_code, item_name, sort, status, del_flag) " +
                    "VALUES ('charge_method', 'CM-X99', '测试字典项', 1, 0, 0)");
            Execute("UPDATE t_dict_item SET del_flag = 1 WHERE type_code = 'charge_method' AND item_code = 'CM-X99'");

            Execute("INSERT INTO t_dict_item (type_code, item_code, item_name, sort, status, del_flag) " +
                    "VALUES ('charge_method', 'CM-X99', '测试字典项2', 2, 0, 0)");

            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_dict_item WHERE type_code = 'charge_method' AND item_code = 'CM-X99' AND del_flag = 0"));
            Assert.Equal(2, ScalarInt("SELECT COUNT(1) FROM t_dict_item WHERE type_code = 'charge_method' AND item_code = 'CM-X99'"));
        }

        // ---------- helpers ----------

        private int NewProperty(string communityName)
        {
            int community = TestData.Community(communityName);
            int building = TestData.Building(community, "1");
            return TestData.Property(building, "101");
        }

        private byte[] BuildPropertyImportFile(string validRoomNo, string invalidRoomNo)
        {
            byte[] template = _service.BuildTemplate(ImportModule.Property);
            using (var stream = new MemoryStream(template))
            using (var workbook = new XLWorkbook(stream))
            {
                IXLWorksheet sheet = workbook.Worksheets.First();
                // v1.1.0：房产模板已移除「用途」列（列序：楼栋号/单元号/房号/建筑面积/状态）；
                // 第 2 行为示例行，真实数据从第 3 行开始
                sheet.Cell(3, 1).Value = "8";        // 楼栋号
                sheet.Cell(3, 2).Value = "1";        // 单元号
                sheet.Cell(3, 3).Value = validRoomNo;
                sheet.Cell(3, 4).Value = "88.5";
                sheet.Cell(3, 5).Value = "空置";
                sheet.Cell(4, 1).Value = "8";
                sheet.Cell(4, 2).Value = "1";
                sheet.Cell(4, 3).Value = invalidRoomNo; // 房号为空 → 错误行
                sheet.Cell(4, 4).Value = "not-a-number";
                using (var output = new MemoryStream())
                {
                    workbook.SaveAs(output);
                    return output.ToArray();
                }
            }
        }

        // ===================== v1.1.0-⑤ 导入批次记录批量删除（软删留痕） =====================

        [Fact]
        public void BatchDeleteImportLogs_选中批次_软删留痕且列表不再返回()
        {
            int keep = SeedImportLog("保留批次.xlsx");
            int deleted = SeedImportLog("待删批次.xlsx");

            RecordBatchDeleteResultDto result = _service.BatchDeleteImportLogs(
                new RecordBatchDeleteRequest { Ids = new List<int> { deleted } }, "admin", "127.0.0.1");

            Assert.Equal(1, result.Deleted);
            Assert.DoesNotContain(_service.ListImportLogs(), x => x.Id == deleted);
            Assert.Contains(_service.ListImportLogs(), x => x.Id == keep);
            // 软删留痕：物理行仍在，待「一键清理残余数据」清理
            Assert.Equal(1, ScalarInt("SELECT del_flag FROM t_import_log WHERE id = @id", new { id = deleted }));
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_audit_log WHERE action = 'IMPORT_LOG_DELETE' AND result = '成功'"));
        }

        [Fact]
        public void BatchDeleteImportLogs_未选批次_抛ValidationFailed且不写审计()
        {
            SeedImportLog();

            var ex = Assert.Throws<ApiException>(() =>
                _service.BatchDeleteImportLogs(new RecordBatchDeleteRequest { Ids = new List<int>() }, "admin"));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("请选择要删除的导入批次记录", ex.Message);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_import_log WHERE del_flag = 1"));
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_audit_log WHERE action = 'IMPORT_LOG_DELETE'"));
        }

        [Fact]
        public void BatchDeleteImportLogs_重复删除同一批次_抛NotFound()
        {
            int id = SeedImportLog();
            _service.BatchDeleteImportLogs(new RecordBatchDeleteRequest { Ids = new List<int> { id } }, "admin");

            var ex = Assert.Throws<ApiException>(() =>
                _service.BatchDeleteImportLogs(new RecordBatchDeleteRequest { Ids = new List<int> { id } }, "admin"));

            Assert.Equal(ErrorCode.NotFound, ex.Code);
            Assert.Contains("不存在或已删除", ex.Message);
        }

        [Fact]
        public void BatchDeleteImportLogs_删除批次_不影响已导入业务数据()
        {
            int community = TestData.Community("批次删除小区");
            int building = TestData.Building(community, "1");
            int property = TestData.Property(building, "101");
            int id = SeedImportLog();

            _service.BatchDeleteImportLogs(new RecordBatchDeleteRequest { Ids = new List<int> { id } }, "admin");

            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_property WHERE id = @id AND del_flag = 0", new { id = property }));
        }

        /// <summary>构造导入批次台账行（t_import_log），返回 id。</summary>
        private static int SeedImportLog(string fileName = "单元测试批次.xlsx")
        {
            Execute("INSERT INTO t_import_log (module, file_name, total, success, fail, status, created_by) " +
                    "VALUES (0, @fileName, 2, 1, 1, 2, 'admin')", new { fileName });
            return ScalarInt("SELECT MAX(id) FROM t_import_log");
        }

        /// <summary>按给定表头与数据行生成导入用 Excel（v1.1.0 用例构造）。</summary>
        private static byte[] BuildSheet(string[] headers, string[][] rows)
        {
            using (var workbook = new XLWorkbook())
            {
                IXLWorksheet sheet = workbook.Worksheets.Add("模板");
                for (int c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
                for (int r = 0; r < rows.Length; r++)
                {
                    for (int c = 0; c < rows[r].Length; c++) sheet.Cell(r + 2, c + 1).Value = rows[r][c];
                }
                using (var output = new MemoryStream())
                {
                    workbook.SaveAs(output);
                    return output.ToArray();
                }
            }
        }
    }
}
