using System;
using System.Threading.Tasks;

namespace SamplePerformance
{
    public class PerformanceTracker : IPerformanceTracker
    {
        private readonly IPerformanceLogger _performanceLogger;

        public PerformanceTracker(IPerformanceLogger performanceLogger)
        {
            _performanceLogger = performanceLogger;
        }

        public T Measure<T>(string name, Func<T> action)
        {
            var scope = new PerformanceScope(name, _performanceLogger);
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                scope.Fail(ex);
                throw;
            }
            finally
            {
                scope.Dispose();
            }
        }

        public void Measure(string name, Action action)
        {
            var scope = new PerformanceScope(name, _performanceLogger);
            try
            {
                action();
            }
            catch (Exception ex)
            {
                scope.Fail(ex);
                throw;
            }
            finally
            {
                scope.Dispose();
            }
        }

        public async Task<T> MeasureAsync<T>(string name, Func<Task<T>> action)
        {
            var scope = new PerformanceScope(name, _performanceLogger);
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                scope.Fail(ex);
                throw;
            }
            finally
            {
                scope.Dispose();
            }
        }

        public async Task MeasureAsync(string name, Func<Task> action)
        {
            var scope = new PerformanceScope(name, _performanceLogger);
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                scope.Fail(ex);
                throw;
            }
            finally
            {
                scope.Dispose();
            }
        }
    }
}
