using System.Web.Http;
using Newtonsoft.Json.Serialization;
using Owin;

namespace PropertyManagement.Server
{
    /// <summary>
    /// OWIN 启动配置（M0 骨架）。
    /// 路由统一为 /api/v1/{controller}/{action}/{id}；JSON 序列化采用驼峰命名。
    /// 鉴权中间件、统一错误码中间件在 M2 补齐。
    /// </summary>
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            HttpConfiguration config = new HttpConfiguration();

            config.MapHttpAttributeRoutes();
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/v1/{controller}/{action}/{id}",
                defaults: new { id = RouteParameter.Optional });

            // 统一 JSON：去掉 XML，属性名驼峰（前端约定）
            config.Formatters.Remove(config.Formatters.XmlFormatter);
            config.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();
            config.Formatters.JsonFormatter.SerializerSettings.DateTimeZoneHandling =
                Newtonsoft.Json.DateTimeZoneHandling.Local;

            app.UseWebApi(config);
        }
    }
}
