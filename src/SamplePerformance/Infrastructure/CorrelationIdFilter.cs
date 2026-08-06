using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace SamplePerformance
{
    public class CorrelationIdFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            var correlationId = actionContext.Request?.Headers?.GetValues("X-Correlation-Id")?.GetEnumerator()?.Current?.ToString();
            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                CorrelationContext.Current = new CorrelationContext(correlationId);
            }

            base.OnActionExecuting(actionContext);
        }
    }
}
