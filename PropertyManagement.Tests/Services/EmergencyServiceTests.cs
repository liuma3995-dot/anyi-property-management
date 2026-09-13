using System;
using System.Collections.Generic;
using System.Linq;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Emergency;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Services;
using PropertyManagement.Tests.Infrastructure;
using Xunit;

namespace PropertyManagement.Tests.Services
{
    /// <summary>应急处置服务单元测试（BR-EMG-01~06）。</summary>
    public class EmergencyServiceTests : DbTestBase
    {
        private readonly EmergencyService _service = new EmergencyService();

        // ===================== BR-EMG-01 事件必须关联应急场景 =====================

        [Fact]
        public void CreateEvent_未选场景_抛ValidationFailed()
        {
            var ex = Assert.Throws<ApiException>(() => _service.CreateEvent(new EmergencyEventCreateRequest
            {
                SceneId = 0, EventTime = DateTime.Now, Level = 2
            }, "admin"));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("BR-EMG-01", ex.Message);
        }

        [Fact]
        public void CreateEvent_场景已停用_抛Conflict()
        {
            int sceneId = _service.SaveScene(0, new EmergencySceneRequest { Name = "停用场景测试", Category = "测试" }).Id;
            _service.SetSceneStatus(sceneId, 1);

            var ex = Assert.Throws<ApiException>(() => _service.CreateEvent(new EmergencyEventCreateRequest
            {
                SceneId = sceneId, EventTime = DateTime.Now, Level = 2
            }, "admin"));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("停用", ex.Message);
        }

        [Fact]
        public void CreateEvent_关联有效场景_生成事件编号与状态留痕()
        {
            int sceneId = TestData.SceneIdByName("火灾");

            var detail = _service.CreateEvent(new EmergencyEventCreateRequest
            {
                SceneId = sceneId, EventTime = DateTime.Now, Location = "1#楼 3 单元", Description = "演练", Level = 2, InitiatorName = "张强"
            }, "admin");

            Assert.True(detail.Event.Id > 0);
            Assert.StartsWith("EM-", detail.Event.EventNo);
            Assert.Equal(EmergencyEventStatus.Initiated, detail.Event.Status);
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_event_status_log WHERE event_id = @id AND new_status = 0",
                new { id = detail.Event.Id }));
        }

        // ===================== BR-EMG-02 处置完成方可结案；复盘可补录 =====================

        [Fact]
        public void CloseEvent_无处置记录_抛Conflict()
        {
            int eventId = NewEvent(TestData.SceneIdByName("火灾"));

            var ex = Assert.Throws<ApiException>(() => _service.CloseEvent(eventId,
                new EmergencyCloseRequest { EventId = eventId, Summary = "处置完毕" }, "admin"));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("处置完成方可结案", ex.Message);
        }

        [Fact]
        public void CloseEvent_已有处置记录_结案成功且归档后只能补录()
        {
            int eventId = NewEvent(TestData.SceneIdByName("火灾"));
            _service.AddRecord(eventId, new EmergencyRecordRequest
            {
                EventId = eventId, RecordTime = DateTime.Now, Content = "关闭燃气总阀并疏散", Result = "现场已控制", Recorder = "张强"
            }, "admin");

            var detail = _service.CloseEvent(eventId, new EmergencyCloseRequest { EventId = eventId, Summary = "处置完毕，无人员受伤" }, "admin");

            Assert.Equal(EmergencyEventStatus.Closed, detail.Event.Status);
            // BR-EMG-06：归档后不可新增普通记录，只能带补录标记
            var blocked = Assert.Throws<ApiException>(() => _service.AddRecord(eventId, new EmergencyRecordRequest
            {
                EventId = eventId, RecordTime = DateTime.Now, Content = "事后补充说明", Result = "补充"
            }, "admin"));
            Assert.Equal(ErrorCode.Conflict, blocked.Code);
            Assert.Contains("补录", blocked.Message);

            var supplemented = _service.AddRecord(eventId, new EmergencyRecordRequest
            {
                EventId = eventId, RecordTime = DateTime.Now, Content = "事后补充说明", Result = "补充", IsSupplement = true
            }, "admin");
            Assert.True(supplemented.IsSupplement);
        }

        [Fact]
        public void CloseEvent_重复结案_抛Conflict()
        {
            int eventId = NewEvent(TestData.SceneIdByName("火灾"));
            _service.AddRecord(eventId, new EmergencyRecordRequest
            {
                EventId = eventId, RecordTime = DateTime.Now, Content = "灭火", Result = "已扑灭"
            }, "admin");
            _service.CloseEvent(eventId, new EmergencyCloseRequest { EventId = eventId, Summary = "处置完毕" }, "admin");

            var ex = Assert.Throws<ApiException>(() => _service.CloseEvent(eventId,
                new EmergencyCloseRequest { EventId = eventId, Summary = "再次结案" }, "admin"));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("已结案", ex.Message);
        }

        [Fact]
        public void ReviewEvent_结案后补录复盘_更新为已复盘()
        {
            int eventId = NewEvent(TestData.SceneIdByName("火灾"));
            _service.AddRecord(eventId, new EmergencyRecordRequest
            {
                EventId = eventId, RecordTime = DateTime.Now, Content = "灭火", Result = "已扑灭"
            }, "admin");
            _service.CloseEvent(eventId, new EmergencyCloseRequest { EventId = eventId, Summary = "处置完毕" }, "admin");

            var review = _service.ReviewEvent(eventId, new EmergencyReviewRequest
            {
                EventId = eventId, Cause = "线路老化", Measure = "更换老化线路并加装监测",
                Items = new List<EmergencyReviewItemRequest>
                {
                    new EmergencyReviewItemRequest { Content = "更换老化线路", Owner = "工程部", Status = 2 }
                }
            }, "admin");

            Assert.Equal(eventId, review.EventId);
            Assert.Single(review.Items);
            Assert.Equal(EmergencyEventStatus.Reviewed, _service.GetEvent(eventId).Event.Status);
        }

        [Fact]
        public void ReviewEvent_未结案事件_抛Conflict()
        {
            int eventId = NewEvent(TestData.SceneIdByName("火灾"));

            var ex = Assert.Throws<ApiException>(() => _service.ReviewEvent(eventId,
                new EmergencyReviewRequest { EventId = eventId, Cause = "未结案", Measure = "无" }, "admin"));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("仅已结案事件可录入复盘", ex.Message);
        }

        // ===================== BR-EMG-03 责任匹配引用排班与在岗状态 =====================

        [Fact]
        public void MatchResponsible_按步骤角色匹配在岗员工_返回候选人()
        {
            int sceneId = TestData.SceneIdByName("火灾");

            List<EmergencyMatchDto> matches = _service.MatchResponsible(sceneId);

            Assert.NotEmpty(matches);
            Assert.Contains(matches, m => m.EmployeeId == TestData.EmployeeIdByName("张强"));
            Assert.All(matches, m => Assert.True(m.EmployeeId > 0));
        }

        [Fact]
        public void MatchResponsible_无匹配角色_返回空名单不阻塞()
        {
            int sceneId = _service.SaveScene(0, new EmergencySceneRequest { Name = "监控离线演练", Category = "测试" }).Id;
            _service.SaveStep(0, sceneId, new EmergencyStepRequest
            {
                SceneId = sceneId, StepNo = 1, Content = "确认监控离线范围", Role = "监控中心值班"
            });

            List<EmergencyMatchDto> matches = _service.MatchResponsible(sceneId);

            Assert.Empty(matches);
        }

        [Fact]
        public void AssignResponsible_无匹配时人工指派_落指派记录()
        {
            int sceneId = _service.SaveScene(0, new EmergencySceneRequest { Name = "人工指派演练", Category = "测试" }).Id;
            _service.SaveStep(0, sceneId, new EmergencyStepRequest
            {
                SceneId = sceneId, StepNo = 1, Content = "确认监控离线范围", Role = "监控中心值班"
            });
            int eventId = NewEvent(sceneId);
            int employeeId = TestData.EmployeeIdByName("李伟");

            var detail = _service.AssignResponsible(eventId, new EmergencyAssignRequest
            {
                EventId = eventId, ResponsibleIds = new List<int> { employeeId }, DutyIds = new List<int>()
            });

            Assert.Contains(detail.Assignments, a => a.EmployeeId == employeeId && a.AssignType == EmergencyAssignType.Responsible);
        }

        // ===================== BR-EMG-04 处置记录必须含时间/操作/结果 =====================

        [Fact]
        public void AddRecord_缺处置结果_抛ValidationFailed()
        {
            int eventId = NewEvent(TestData.SceneIdByName("火灾"));

            var ex = Assert.Throws<ApiException>(() => _service.AddRecord(eventId, new EmergencyRecordRequest
            {
                EventId = eventId, RecordTime = DateTime.Now, Content = "关闭阀门", Result = "   "
            }, "admin"));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("处置结果不能为空", ex.Message);
            Assert.Equal(0, ScalarInt("SELECT COUNT(1) FROM t_emergency_record WHERE event_id = @id", new { id = eventId }));
        }

        [Fact]
        public void AddRecord_缺处置内容_抛ValidationFailed()
        {
            int eventId = NewEvent(TestData.SceneIdByName("火灾"));

            var ex = Assert.Throws<ApiException>(() => _service.AddRecord(eventId, new EmergencyRecordRequest
            {
                EventId = eventId, RecordTime = DateTime.Now, Content = "  ", Result = "已控制"
            }, "admin"));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("处置记录内容不能为空", ex.Message);
        }

        [Fact]
        public void AddRecord_完整记录_落库含时间操作与结果且事件转处置中()
        {
            int eventId = NewEvent(TestData.SceneIdByName("火灾"));
            DateTime recordTime = DateTime.Now.AddMinutes(-5);

            var dto = _service.AddRecord(eventId, new EmergencyRecordRequest
            {
                EventId = eventId, RecordTime = recordTime, Content = "启动消防泵", Result = "水压正常", Recorder = "王工"
            }, "admin");

            Assert.Equal(recordTime.ToString("yyyy-MM-dd HH:mm"), dto.RecordTime.ToString("yyyy-MM-dd HH:mm"));
            Assert.Equal("启动消防泵", dto.Content);
            Assert.Equal("水压正常", dto.Result);
            Assert.Equal(EmergencyEventStatus.Handling, _service.GetEvent(eventId).Event.Status);
        }

        // ===================== BR-EMG-05 处置步骤版本化 =====================

        [Fact]
        public void SaveStep_更新步骤_历史版本保留且版本号递增()
        {
            int sceneId = _service.SaveScene(0, new EmergencySceneRequest { Name = "步骤版本演练", Category = "测试" }).Id;
            var created = _service.SaveStep(0, sceneId, new EmergencyStepRequest
            {
                SceneId = sceneId, StepNo = 1, Content = "初始步骤", Role = "工程值班"
            });
            Assert.Equal("v1", created.VersionNo);

            var updated = _service.SaveStep(created.Id, sceneId, new EmergencyStepRequest
            {
                StepNo = 1, Content = "修订后的步骤", Role = "工程值班"
            });

            Assert.Equal("v2", updated.VersionNo);
            Assert.Single(_service.ListSteps(sceneId)); // 启用态只保留最新版本
            Assert.Equal(2, ScalarInt("SELECT COUNT(1) FROM t_emergency_step WHERE scene_id = @id", new { id = sceneId }));
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_emergency_step WHERE id = @id AND status = 1", new { id = created.Id }));
            Assert.Equal("初始步骤", ScalarText("SELECT content FROM t_emergency_step WHERE id = @id", new { id = created.Id }));
        }

        [Fact]
        public void SaveStep_编辑已归档版本_抛ValidationFailed()
        {
            int sceneId = _service.SaveScene(0, new EmergencySceneRequest { Name = "归档步骤演练", Category = "测试" }).Id;
            var created = _service.SaveStep(0, sceneId, new EmergencyStepRequest { SceneId = sceneId, StepNo = 1, Content = "初始步骤" });
            _service.SaveStep(created.Id, sceneId, new EmergencyStepRequest { StepNo = 1, Content = "修订后的步骤" });

            var ex = Assert.Throws<ApiException>(() => _service.SaveStep(created.Id, sceneId,
                new EmergencyStepRequest { StepNo = 1, Content = "再次修订" }));

            Assert.Equal(ErrorCode.ValidationFailed, ex.Code);
            Assert.Contains("已归档", ex.Message);
        }

        // ===================== BR-EMG-06 归档后不得删除，只能补录 =====================

        [Fact]
        public void CancelEvent_发起60秒内_撤销成功且写状态留痕()
        {
            int eventId = NewEvent(TestData.SceneIdByName("火灾"));

            _service.CancelEvent(eventId, "admin");

            Assert.Equal(1, ScalarInt("SELECT del_flag FROM t_emergency_event WHERE id = @id", new { id = eventId }));
            Assert.Equal(1, ScalarInt("SELECT COUNT(1) FROM t_event_status_log WHERE event_id = @id AND new_status = 4",
                new { id = eventId }));
        }

        [Fact]
        public void CancelEvent_超过60秒_抛Conflict()
        {
            int eventId = NewEvent(TestData.SceneIdByName("火灾"));
            Execute("UPDATE t_emergency_event SET created_at = datetime('now','localtime','-10 minutes') WHERE id = @id",
                new { id = eventId });

            var ex = Assert.Throws<ApiException>(() => _service.CancelEvent(eventId, "admin"));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("60 秒", ex.Message);
            Assert.Equal(0, ScalarInt("SELECT del_flag FROM t_emergency_event WHERE id = @id", new { id = eventId }));
        }

        [Fact]
        public void CancelEvent_已结案事件_抛Conflict()
        {
            int eventId = NewEvent(TestData.SceneIdByName("火灾"));
            _service.AddRecord(eventId, new EmergencyRecordRequest
            {
                EventId = eventId, RecordTime = DateTime.Now, Content = "灭火", Result = "已扑灭"
            }, "admin");
            _service.CloseEvent(eventId, new EmergencyCloseRequest { EventId = eventId, Summary = "处置完毕" }, "admin");

            var ex = Assert.Throws<ApiException>(() => _service.CancelEvent(eventId, "admin"));

            Assert.Equal(ErrorCode.Conflict, ex.Code);
            Assert.Contains("仅「已发起」", ex.Message);
        }

        // ---------- helper ----------

        private int NewEvent(int sceneId, int level = 2)
        {
            return _service.CreateEvent(new EmergencyEventCreateRequest
            {
                SceneId = sceneId, EventTime = DateTime.Now, Location = "1#楼", Description = "单元测试事件", Level = level
            }, "admin").Event.Id;
        }
    }
}
