using System;
using System.Threading;

namespace SamplePerformance
{
    public sealed class PerformanceScope : IDisposable
    {
        private static readonly AsyncLocal<PerformanceScope> CurrentScope = new AsyncLocal<PerformanceScope>();
        private readonly TimingMeasurement _measurement;
        private readonly string _name;
        private readonly PerformanceScope _parent;
        private readonly IPerformanceLogger _performanceLogger;

        public PerformanceScope(string name)
            : this(name, null)
        {
        }

        public PerformanceScope(string name, IPerformanceLogger performanceLogger)
        {
            _name = name;
            _parent = CurrentScope.Value;
            _performanceLogger = performanceLogger ?? new NLogPerformanceLogger();
            _measurement = new TimingMeasurement((elapsedMs, exception) =>
            {
                var message = $"{_name}: {elapsedMs:0.##} ms";
                if (exception != null)
                {
                    _performanceLogger.LogException($"{_name} failed after {elapsedMs:0.##} ms", exception);
                }
                else
                {
                    _performanceLogger.Log(message, _parent, elapsedMs, _name);
                }
            });
            CurrentScope.Value = this;
        }

        public static PerformanceScope Current => CurrentScope.Value;

        public void Dispose()
        {
            _measurement.Dispose();
            CurrentScope.Value = _parent;
        }

        public void Fail(Exception exception)
        {
            _measurement.Fail(exception);
            CurrentScope.Value = _parent;
        }

        public string Name => _name;
    }
}
