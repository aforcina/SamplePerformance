using System;
using NLog;
using Newtonsoft.Json;

namespace SamplePerformance
{
    public interface IPerformanceLogger
    {
        void Log(string message, PerformanceScope parentScope, double elapsedMs, string stepName);
        void LogException(string message, Exception exception);
    }

    public class NLogPerformanceLogger : IPerformanceLogger
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public void Log(string message, PerformanceScope parentScope, double elapsedMs, string stepName)
        {
            var correlationId = CorrelationContext.Current?.CorrelationId ?? "-";
            var payload = new
            {
                timestamp = DateTimeOffset.UtcNow,
                level = "Info",
                correlationId,
                message,
                step = stepName,
                elapsedMs = Math.Round(elapsedMs, 2)
            };

            Logger.Info(JsonConvert.SerializeObject(payload));
        }

        public void LogException(string message, Exception exception)
        {
            var correlationId = CorrelationContext.Current?.CorrelationId ?? "-";
            var payload = new
            {
                timestamp = DateTimeOffset.UtcNow,
                level = "Error",
                correlationId,
                message,
                exceptionType = exception.GetType().FullName,
                exceptionMessage = exception.Message
            };

            Logger.Error(JsonConvert.SerializeObject(payload));
        }
    }
}
