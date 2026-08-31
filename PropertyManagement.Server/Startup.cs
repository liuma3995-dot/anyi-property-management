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
    }
}
