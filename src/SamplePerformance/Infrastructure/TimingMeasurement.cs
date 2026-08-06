using System;
using System.Diagnostics;

namespace SamplePerformance
{
    public class TimingMeasurement : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly Action<double, Exception> _onComplete;
        private Exception _exception;

        public TimingMeasurement(Action<double, Exception> onComplete)
        {
            _stopwatch = Stopwatch.StartNew();
            _onComplete = onComplete;
        }

        public void Fail(Exception exception)
        {
            _exception = exception;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _onComplete(_stopwatch.Elapsed.TotalMilliseconds, _exception);
        }
    }
}
