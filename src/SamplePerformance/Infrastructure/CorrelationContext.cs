using System.Threading;

namespace SamplePerformance
{
    public sealed class CorrelationContext
    {
        private static readonly AsyncLocal<CorrelationContext> CurrentContext = new AsyncLocal<CorrelationContext>();

        public CorrelationContext(string correlationId)
        {
            CorrelationId = correlationId;
        }

        public string CorrelationId { get; }

        public static CorrelationContext Current
        {
            get => CurrentContext.Value;
            set => CurrentContext.Value = value;
        }
    }
}
