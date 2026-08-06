using System.Web.Http;

namespace SamplePerformance
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(HttpConfiguration config)
        {
            config.Filters.Add(new CorrelationIdFilter());
            config.Filters.Add(new PerformanceActionFilter());
        }
    }
}
