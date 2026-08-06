using System;
using System.Web;
using System.Web.Http;
using Microsoft.Extensions.DependencyInjection;

namespace SamplePerformance
{
    public class WebApiApplication : HttpApplication
    {
        public static ServiceProvider ServiceProvider { get; private set; }

        protected void Application_Start()
        {
            var services = new ServiceCollection();

            services.AddSingleton<IPerformanceLogger, NLogPerformanceLogger>();
            services.AddSingleton<IPerformanceTracker, PerformanceTracker>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IItemRepository, ItemRepository>();
            services.AddScoped<IExternalItemGateway, ExternalItemGateway>();
            services.AddScoped<IItemProcessingService, ItemProcessingService>();

            services.AddHttpClient<IExternalItemGateway, ExternalItemGateway>()
                .ConfigurePrimaryHttpMessageHandler(() => new CorrelationHttpHandler
                {
                    InnerHandler = new HttpClientHandler()
                });

            ServiceProvider = services.BuildServiceProvider();

            GlobalConfiguration.Configure(WebApiConfig.Register);
            GlobalConfiguration.Configuration.DependencyResolver = new SampleDependencyResolver(ServiceProvider);
            FilterConfig.RegisterGlobalFilters(GlobalConfiguration.Configuration);
        }
    }
}
