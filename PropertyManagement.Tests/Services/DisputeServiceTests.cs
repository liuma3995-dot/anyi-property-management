using System;
using System.Collections.Generic;
using System.Linq;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Dispute;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Services;
using PropertyManagement.Tests.Infrastructure;
using Xunit;

namespace PropertyManagement.Tests.Services
{
    /// <summary>民事纠纷调解服务单元测试（BR-DIS-01~05）。</summary>
    public class DisputeServiceTests : DbTestBase
    {
        private readonly DisputeService _service = new DisputeService();

        // ===================== BR-DIS-01 状态流：已登记 → 处理中 → 已结案 =====================

        [Fact]
        public void UpdateStatus_已登记转处理中_状态留痕()
        {
            int caseId = NewCase();

            var dto = _service.UpdateStatus(caseId, new DisputeCaseStatusRequest { Status = DisputeCaseStatus.Handling });

            Assert.Equal(DisputeCaseStatus.Handling, dto.Status);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_dispute_status_log WHERE case_id = @id AND new_status = 1", new { id = caseId }));
        }

        [Fact]
        public void UpdateStatus_跳过处理中直接置已结案_抛ValidationFailed()
        {
            int caseId = NewCase();

            var ex = Assert.Throws<ApiException>(() => _service.UpdateStatus(caseId,
                new DisputeCaseStatusRequest { Status = DisputeCaseStatus.Closed }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("非法状态流转", ex.Message);
            Assert.Equal((int)DisputeCaseStatus.Registered,
                ScalarInt("SELECT status FROM t_dispute_case WHERE id = @id", new { id = caseId }));
        }

        [Fact]
        public void CloseCase_已登记未受理_抛Conflict()
        {
            int caseId = NewCase();

            var ex = Assert.Throws<ApiException>(() => _service.CloseCase(caseId, new DisputeCloseRequest
            {
                CaseId = caseId, CloseType = DisputeCloseType.Mediated, Summary = "调解成功"
            }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("BR-DIS-01", ex.Message);
        }

        // ===================== BR-DIS-02 结案必须至少一条处理方案记录 =====================

        [Fact]
        public void CloseCase_无处理方案记录_抛Conflict()
        {
            int caseId = NewCase();
            _service.UpdateStatus(caseId, new DisputeCaseStatusRequest { Status = DisputeCaseStatus.Handling });

            var ex = Assert.Throws<ApiException>(() => _service.CloseCase(caseId, new DisputeCloseRequest
            {
                CaseId = caseId, CloseType = DisputeCloseType.Mediated, Summary = "调解成功"
            }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("BR-DIS-02", ex.Message);
            Assert.Equal((int)DisputeCaseStatus.Handling,
                ScalarInt("SELECT status FROM t_dispute_case WHERE id = @id", new { id = caseId }));
        }

        [Fact]
        public void CloseCase_存在处理方案记录_结案成功并写结案报告()
        {
            int caseId = NewCase();
            _service.UpdateStatus(caseId, new DisputeCaseStatusRequest { Status = DisputeCaseStatus.Handling });
            _service.AddRecord(new DisputeRecordRequest
            {
                CaseId = caseId, RecordTime = DateTime.Now, Method = "现场调解", PlanSummary = "双方各让一步", Result = "达成一致"
            });

            var dto = _service.CloseCase(caseId, new DisputeCloseRequest
            {
                CaseId = caseId, CloseType = DisputeCloseType.Mediated, Summary = "双方签署调解协议，纠纷化解"
            });

            Assert.Equal(DisputeCaseStatus.Closed, dto.Status);
            Assert.Equal(DisputeCloseType.Mediated, dto.CloseType);
            Assert.Equal("双方签署调解协议，纠纷化解", dto.CloseSummary);
        }

        [Fact]
        public void CloseCase_结案报告为空_抛ValidationFailed()
        {
            int caseId = NewCase();
            _service.UpdateStatus(caseId, new DisputeCaseStatusRequest { Status = DisputeCaseStatus.Handling });
            _service.AddRecord(new DisputeRecordRequest { CaseId = caseId, RecordTime = DateTime.Now, Content = "现场调解" });

            var ex = Assert.Throws<ApiException>(() => _service.CloseCase(caseId, new DisputeCloseRequest
            {
                CaseId = caseId, CloseType = DisputeCloseType.Mediated, Summary = "   "
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("结案报告不能为空", ex.Message);
        }

        // ===================== BR-DIS-03 调解员引用员工档案 =====================

        [Fact]
        public void RecommendMediators_仅返回在职员工()
        {
            int typeId = TestData.DisputeType("调解员测试类型");
            int zhangQiang = TestData.EmployeeIdByName("张强");
            Assert.Contains(_service.RecommendMediators(typeId, null), m => m.EmployeeId == zhangQiang);

            new OrgService().ResignEmployee(zhangQiang);

            Assert.DoesNotContain(_service.RecommendMediators(typeId, null), m => m.EmployeeId == zhangQiang);
        }

        [Fact]
        public void RecommendMediators_员工离职_不再作为调解员候选()
        {
            int typeId = TestData.DisputeType("调解员离职类型");
            int employeeId = TestData.EmployeeIdByName("刘芳");
            Assert.Contains(_service.RecommendMediators(typeId, null), m => m.EmployeeId == employeeId);

            new OrgService().ResignEmployee(employeeId);

            Assert.DoesNotContain(_service.RecommendMediators(typeId, null), m => m.EmployeeId == employeeId);
            Assert.Equal(0, ScalarInt("SELECT del_flag FROM t_employee WHERE id = @id", new { id = employeeId })); // 档案保留
        }

        // ===================== BR-DIS-04 结案后可补录 + 普通新增记录被拒 =====================

        [Fact]
        public void AddRecord_已结案案件_抛Conflict()
        {
            int caseId = ClosedCase();

            var ex = Assert.Throws<ApiException>(() => _service.AddRecord(new DisputeRecordRequest
            {
                CaseId = caseId, RecordTime = DateTime.Now, Content = "结案后再新增普通记录"
            }));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("已结案需走补录", ex.Message);
        }

        [Fact]
        public void SupplementRecord_已结案_允许补录并留痕()
        {
            int caseId = ClosedCase();

            var dto = _service.SupplementRecord(caseId, new DisputeSupplementRequest
            {
                Content = "补充：双方约定一周内修复渗水", PlanSummary = "补充履行约定", Reason = "结案后补充履行细节"
            }, "admin");

            Assert.True(dto.IsSupplement);
            Assert.Equal("结案后补充履行细节", dto.SupplementReason);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_dispute_record WHERE case_id = @id AND is_supplement = 1", new { id = caseId }));
        }

        [Fact]
        public void SupplementRecord_补录原因为空_抛ValidationFailed()
        {
            int caseId = ClosedCase();

            var ex = Assert.Throws<ApiException>(() => _service.SupplementRecord(caseId, new DisputeSupplementRequest
            {
                Content = "补充内容", Reason = "   "
            }, "admin"));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("补录原因不能为空", ex.Message);
        }

        [Fact]
        public void SupplementRecord_非管理员_抛Forbidden()
        {
            int caseId = ClosedCase();

            var ex = Assert.Throws<ApiException>(() => _service.SupplementRecord(caseId, new DisputeSupplementRequest
            {
                Content = "补充内容", Reason = "补充原因"
            }, "other_user"));

            Assert.Equal(ErrorCode.Forbidden, ex.Code);
        }

        [Fact]
        public void SupplementRecord_未结案案件_抛Conflict()
        {
            int caseId = NewCase();

            var ex = Assert.Throws<ApiException>(() => _service.SupplementRecord(caseId, new DisputeSupplementRequest
            {
                Content = "补充内容", Reason = "补充原因"
            }, "admin"));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("仅已结案案件可补录", ex.Message);
        }

        // ===================== BR-DIS-05 当事人可引用业主或外部登记 =====================

        [Fact]
        public void CreateCase_外部当事人_允许登记()
        {
            int typeId = TestData.DisputeType("外部当事人类型");

            var dto = _service.CreateCase(new DisputeCaseCreateRequest
            {
                TypeId = typeId, OccurTime = DateTime.Now, Location = "3#楼 2 单元", Detail = "噪音纠纷", Level = 0,
                Parties = new List<DisputePartyDto>
                {
                    new DisputePartyDto { PartyType = "0", Name = "外部登记人甲", Phone = "13900001111" },
                    new DisputePartyDto { PartyType = "1", Name = "外部登记人乙", Phone = "13900002222" }
                }
            });

            DisputeCaseDetailDto detail = _service.GetCase(dto.Id);
            Assert.Equal(2, detail.Parties.Count);
            Assert.All(detail.Parties, p => Assert.Null(p.OwnerId)); // 外部人员不引用业主档案
            Assert.Contains(detail.Parties, p => p.PartyType == "1");
        }

        [Fact]
        public void CreateCase_未选纠纷类型_抛ValidationFailed()
        {
            var ex = Assert.Throws<ApiException>(() => _service.CreateCase(new DisputeCaseCreateRequest
            {
                TypeId = 0, OccurTime = DateTime.Now, Detail = "缺少类型"
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("请选择纠纷类型", ex.Message);
        }

        [Fact]
        public void CreateCase_未选发生时间_抛ValidationFailed()
        {
            int typeId = TestData.DisputeType("时间校验类型");

            var ex = Assert.Throws<ApiException>(() => _service.CreateCase(new DisputeCaseCreateRequest
            {
                TypeId = typeId, OccurTime = default(DateTime), Detail = "缺少时间"
            }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("请选择发生时间", ex.Message);
        }

        // ---------- helpers ----------

        private int NewCase()
        {
            var dto = _service.CreateCase(new DisputeCaseCreateRequest
            {
                TypeId = TestData.DisputeType("单元测试类型"), OccurTime = DateTime.Now,
                Location = "1#楼 1 单元", Detail = "单元测试纠纷", Level = 0
            });
            return dto.Id;
        }

        private int ClosedCase()
        {
            int caseId = NewCase();
            _service.UpdateStatus(caseId, new DisputeCaseStatusRequest { Status = DisputeCaseStatus.Handling });
            _service.AddRecord(new DisputeRecordRequest
            {
                CaseId = caseId, RecordTime = DateTime.Now, Method = "现场调解", PlanSummary = "双方协商", Result = "达成一致"
            });
            _service.CloseCase(caseId, new DisputeCloseRequest
            {
                CaseId = caseId, CloseType = DisputeCloseType.Mediated, Summary = "调解成功并签署协议"
            });
            return caseId;
        }
    }
}
