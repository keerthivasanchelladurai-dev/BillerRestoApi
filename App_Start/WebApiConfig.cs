using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace BillerRestoApi
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Web API configuration and services
            config.Formatters.Remove(config.Formatters.XmlFormatter);
            config.Formatters.JsonFormatter.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented;

            // Never put exception details in a response body, regardless of where the request comes from.
            config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Never;

            // Outermost handler: correlation id, X-Correlation-Id header, generic 5xx body, request-completion log.
            config.MessageHandlers.Add(new BillerRestoApi.Security.RequestLoggingHandler());
            config.Services.Add(typeof(System.Web.Http.ExceptionHandling.IExceptionLogger),
                new BillerRestoApi.Security.DiagExceptionLogger());

            // Secure by default: every endpoint requires a valid bearer token unless it is
            // explicitly marked [AllowAnonymous] (currently only POST api/auth/login and GET api/health/database).
            config.MessageHandlers.Add(new BillerRestoApi.Security.BearerTokenHandler());
            config.Filters.Add(new BillerRestoApi.Security.ApiAuthorizeAttribute());

            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }
    }
}
