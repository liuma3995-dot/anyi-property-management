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
            Assert.Contains("BR-INF-01", ex.Message);
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
            Assert.Contains("BR-INF-02", ex.Message);
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

        [Fact]
        public void SaveParking_同一房产绑定第二个产权车位_抛Conflict()
        {
            int property = NewProperty("车位绑定小区");
            TestData.Parking("B-001", ParkingSpaceType.PropertyRight, ParkingSpaceStatus.Owned, property);

            var ex = Assert.Throws<ApiException>(() => _service.SaveParking(0, new ParkingSpaceRequest
            {
                SpaceNo = "B-002", SpaceType = ParkingSpaceType.PropertyRight,
                Status = ParkingSpaceStatus.Owned, PropertyId = property
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
            Assert.Contains("人防车位不可标为出售", ex.Message);
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
                sheet.Cell(2, 1).Value = "8";        // 楼栋号
                sheet.Cell(2, 2).Value = "1";        // 单元号
                sheet.Cell(2, 3).Value = validRoomNo;
                sheet.Cell(2, 4).Value = "88.5";
                sheet.Cell(2, 5).Value = "住宅";
                sheet.Cell(2, 6).Value = "空置";
                sheet.Cell(3, 1).Value = "8";
                sheet.Cell(3, 2).Value = "1";
                sheet.Cell(3, 3).Value = invalidRoomNo; // 房号为空 → 错误行
                sheet.Cell(3, 4).Value = "not-a-number";
                using (var output = new MemoryStream())
                {
                    workbook.SaveAs(output);
                    return output.ToArray();
                }
            }
        }
    }
}
