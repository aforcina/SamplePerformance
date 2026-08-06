using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SamplePerformance;

namespace SamplePerformance.Tests
{
    public class ItemProcessingServiceTests
    {
        [Fact]
        public void Process_returns_processed_response_and_invokes_workflow_dependencies()
        {
            var repository = new RecordingItemRepository();
            var gateway = new RecordingGateway();
            var unitOfWork = new RecordingUnitOfWork(repository);
            var logger = new RecordingLogger();
            var tracker = new PerformanceTracker(logger);
            var service = new ItemProcessingService(tracker, unitOfWork, gateway, logger);

            var request = new ItemRequest { Id = 42, Name = "Widget" };
            var response = service.Process(request);

            Assert.NotNull(response);
            Assert.Equal(42, response.Id);
            Assert.Equal("Processed", response.Status);
            Assert.Equal(1, repository.GetByIdCalls);
            Assert.Equal(1, unitOfWork.CommitCalls);
            Assert.Equal(1, gateway.SendCalls);
            Assert.Same(request, gateway.LastRequest);
        }

        [Fact]
        public async Task MeasureAsync_logs_failure_and_reraises()
        {
            var logger = new RecordingLogger();
            var tracker = new PerformanceTracker(logger);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => tracker.MeasureAsync("boom", async () =>
            {
                await Task.Delay(10);
                throw new InvalidOperationException("boom");
            }));

            Assert.Equal("boom", exception.Message);
            Assert.Contains(logger.ExceptionMessages, message => message.Contains("boom"));
        }

        [Fact]
        public async Task CorrelationHttpHandler_adds_correlation_id_header_from_context()
        {
            HttpRequestMessage capturedRequest = null;
            var innerHandler = new StubHttpMessageHandler(request =>
            {
                capturedRequest = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });
            var handler = new CorrelationHttpHandler
            {
                InnerHandler = innerHandler
            };

            CorrelationContext.Current = new CorrelationContext("corr-123");
            try
            {
                using (var client = new HttpClient(handler))
                {
                    var response = await client.GetAsync("https://example.test");
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }

                Assert.NotNull(capturedRequest);
                Assert.Equal("corr-123", capturedRequest.Headers.GetValues("X-Correlation-Id").Single());
            }
            finally
            {
                CorrelationContext.Current = null;
            }
        }

        private sealed class RecordingItemRepository : IItemRepository
        {
            public int GetByIdCalls { get; private set; }
            public ItemResponse GetById(int id)
            {
                GetByIdCalls++;
                return new ItemResponse { Id = id, Status = "Loaded" };
            }
        }

        private sealed class RecordingUnitOfWork : IUnitOfWork
        {
            public RecordingUnitOfWork(IItemRepository itemRepository)
            {
                ItemRepository = itemRepository;
            }

            public IItemRepository ItemRepository { get; }
            public int CommitCalls { get; private set; }
            public void Commit()
            {
                CommitCalls++;
            }
        }

        private sealed class RecordingGateway : IExternalItemGateway
        {
            public int SendCalls { get; private set; }
            public ItemRequest LastRequest { get; private set; }
            public void Send(ItemRequest request)
            {
                SendCalls++;
                LastRequest = request;
            }
        }

        private sealed class RecordingLogger : IPerformanceLogger
        {
            public List<string> LogMessages { get; } = new List<string>();
            public List<string> ExceptionMessages { get; } = new List<string>();
            public void Log(string message, PerformanceScope parentScope, double elapsedMs, string stepName)
            {
                LogMessages.Add($"{message}|{stepName}|{elapsedMs}");
            }
            public void LogException(string message, Exception exception)
            {
                ExceptionMessages.Add($"{message}|{exception.Message}");
            }
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
            public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _handler(request);
            }
        }
    }
}
