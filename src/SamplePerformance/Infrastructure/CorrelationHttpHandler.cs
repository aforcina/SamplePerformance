using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SamplePerformance
{
    public class CorrelationHttpHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var correlationId = CorrelationContext.Current?.CorrelationId;
            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
