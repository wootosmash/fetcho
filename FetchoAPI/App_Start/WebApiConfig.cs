﻿using System.Configuration;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Web.Http;
using Fetcho.Common;

namespace FetchoAPI
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // need this to turn on log4net
            log4net.Config.XmlConfigurator.Configure();

            // Web API configuration and services
            config.Formatters.JsonFormatter.SupportedMediaTypes
                .Add(new MediaTypeHeaderValue("text/html"));

            config.Formatters.Add(new BinaryMediaTypeFormatter());

            // Web API routes
            config.MapHttpAttributeRoutes();

            // Allow cross origin requests
            config.EnableCors();

            // enable controllers
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            GlobalConfiguration.Configuration.IncludeErrorDetailPolicy
            = IncludeErrorDetailPolicy.Always;

            // prettfy the JSON formatters
            var json = GlobalConfiguration.Configuration.Formatters.JsonFormatter;
            json.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented;

            // setup configuration
            var cfg = new FetchoConfiguration();
            cfg.SetConfigurationSetting(() => cfg.MLModelPath, ConfigurationManager.AppSettings["MLModelPath"]);
            FetchoConfiguration.Current = cfg;
        }
    }
}
