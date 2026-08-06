using System;
using System.Threading.Tasks;

namespace SamplePerformance
{
    public interface IPerformanceTracker
    {
        T Measure<T>(string name, Func<T> action);
        void Measure(string name, Action action);
        Task<T> MeasureAsync<T>(string name, Func<Task<T>> action);
        Task MeasureAsync(string name, Func<Task> action);
    }
}
