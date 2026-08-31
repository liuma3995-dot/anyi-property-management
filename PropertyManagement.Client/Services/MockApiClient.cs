using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PropertyManagement.Contract.Auth;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Health;

namespace PropertyManagement.Client.Services
{
    /// <summary>
    /// 演示数据客户端（M3-D3）：契约一致、数据为夹具。
    /// 用于 M2 后端落地前的界面评审；接入真实后端后由 ApiClientFactory 切换。
    /// </summary>
    public class MockApiClient : IApiClient
    {
        public bool IsMock => true;

        private const string MockUser = "admin";
        private const string MockPassword = "Admin@123";

        public Task<HealthResponse> GetHealthAsync()
        {
            return Task.FromResult(new HealthResponse
            {
                Service = "PropertyManagement.Server（Mock）",
                Version = "v0.1-mock",
                Status = "ok",
                ServerTime = DateTime.Now
            });
        }

        public Task<LoginResult> LoginAsync(LoginRequest request)
        {
            if (request == null ||
                !string.Equals(request.UserName, MockUser, StringComparison.OrdinalIgnoreCase) ||
                request.Password != MockPassword)
            {
                throw new ApiClientException(ErrorCode.Unauthorized, "用户名或密码错误（演示账号 admin / Admin@123）");
            }

            return Task.FromResult(new LoginResult
            {
                Token = "mock-token-" + Guid.NewGuid().ToString("N"),
                DisplayName = "系统管理员",
                ExpiresAt = DateTime.Now.AddHours(8)
            });
        }

        public Task LogoutAsync()
        {
            return Task.CompletedTask;
        }

        public Task ChangePasswordAsync(ChangePasswordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            {
                throw new ApiClientException(ErrorCode.ValidationFailed, "新密码长度不能少于 6 位");
            }
            return Task.CompletedTask;
        }

        public Task<DashboardDto> GetDashboardAsync()
        {
            var dto = new DashboardDto
            {
                PendingReminders = 6,
                ArrearAmount = 25000.00m,
                ArrearCount = 8,
                HandlingEmergency = 1,
                PendingReview = 2,
                HandlingDisputes = 3,
                RepairingDevices = 2,
                MonthReceivable = 120000.00m,
                ReceivableTrend = "较上月 +6.8%",
                MonthReceived = 95000.00m,
                CollectionRate = 79.2m,
                ReceivedTrend = "收缴率 79.2%",
                OverdueTrend = "较上月 -2 户",
                MaintenanceDue = 2,
                DutyToday = 4,
                RecentReminders = new List<ReminderDto>
                {
                    new ReminderDto { Id = 1, Type = "EQUIPMENT_INSPECTION", DueAt = DateTime.Now.AddDays(3), Status = ReminderStatus.Pending },
                    new ReminderDto { Id = 2, Type = "EMERGENCY_REVIEW", DueAt = DateTime.Now.AddDays(2), Status = ReminderStatus.Pending },
                    new ReminderDto { Id = 3, Type = "ARREARS", DueAt = DateTime.Now.AddDays(1), Status = ReminderStatus.Pending },
                    new ReminderDto { Id = 4, Type = "EQUIPMENT_MAINTENANCE", DueAt = DateTime.Now.AddDays(7), Status = ReminderStatus.Pending },
                    new ReminderDto { Id = 5, Type = "DISPUTE_OVERDUE", DueAt = DateTime.Now.AddDays(5), Status = ReminderStatus.Pending },
                    new ReminderDto { Id = 6, Type = "DISPUTE_OVERDUE", DueAt = DateTime.Now.AddDays(9), Status = ReminderStatus.Pending }
                }
            };
            return Task.FromResult(dto);
        }
    }
}

