using System;
using System.Web.Http;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Owin;
using PropertyManagement.Server.Api.Filters;
using PropertyManagement.Server.Api.Middleware;
using PropertyManagement.Server.Infrastructure.Data;

namespace PropertyManagement.Server
{
    /// <summary>
    /// OWIN 启动配置（M2）：
    /// 1) 启动时初始化数据库（schema + 种子，幂等）；
    /// 2) 路由统一 /api/v1/{controller}/{action}/{id}；
    /// 3) JSON 驼峰 + 枚举字符串（契约 §一）；
    /// 4) 统一校验/异常过滤器 + Bearer 鉴权中间件。
    /// </summary>
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            DatabaseInitializer.EnsureInitialized();
            StartDailyBackupTimer();

            var config = new HttpConfiguration();

            config.MapHttpAttributeRoutes();
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/v1/{controller}/{action}/{id}",
                defaults: new { id = RouteParameter.Optional });

            // 统一 JSON：去掉 XML，属性名驼峰，枚举字符串，时间本地（契约 §一）
            config.Formatters.Remove(config.Formatters.XmlFormatter);
            config.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();
            config.Formatters.JsonFormatter.SerializerSettings.DateTimeZoneHandling =
                Newtonsoft.Json.DateTimeZoneHandling.Local;
            config.Formatters.JsonFormatter.SerializerSettings.Converters.Add(
                new StringEnumConverter());

            // 统一错误码与校验信封
            config.Filters.Add(new ApiValidationFilterAttribute());
            config.Filters.Add(new ApiExceptionFilterAttribute());

            // Bearer 鉴权（除 login/health 外逐请求校验）
            app.Use<AuthMiddleware>();
            app.UseWebApi(config);
        }

        // P-09 每日自动备份（M6 T6-6-3）：每日 02:00 后首次轮询触发；进程存活期间按小时对时
        private System.Threading.Timer _backupTimer;

        private void StartDailyBackupTimer()
        {
            var timer = new System.Threading.Timer(
                _ =>
                {
                    try
                    {
                        string today = DateTime.Now.ToString("yyyy-MM-dd");
                        string flagFile = System.IO.Path.Combine(
                            Infrastructure.Data.DbConfig.BackupDirectory, "autobackup-" + today + ".flag");
                        if (System.IO.File.Exists(flagFile))
                        {
                            return;
                        }
                        if (DateTime.Now.Hour < 2)
                        {
                            return; // 未到 02:00
                        }
                        var svc = new Services.CommonService();
                        var dto = svc.RunBackup("每日自动备份（P-09 计划）", "系统计划", null, "auto");
                        System.IO.File.WriteAllText(flagFile, dto != null ? dto.Id.ToString() : "0");
                    }
                    catch (Exception)
                    {
                        // 备份失败留待下个轮询周期重试（审计与告警由 RunBackup 内部留痕）
                    }
                },
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromHours(1));
            _backupTimer = timer;
        }
    }
}
