using System;
using System.Threading;
using System.Threading.Tasks;

namespace SamplePerformance
{
    public class ItemProcessingService : IItemProcessingService
    {
        private readonly IPerformanceTracker _performanceTracker;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IExternalItemGateway _externalItemGateway;
        private readonly IPerformanceLogger _performanceLogger;

        public ItemProcessingService(
            IPerformanceTracker performanceTracker,
            IUnitOfWork unitOfWork,
            IExternalItemGateway externalItemGateway,
            IPerformanceLogger performanceLogger)
        {
            _performanceTracker = performanceTracker;
            _unitOfWork = unitOfWork;
            _externalItemGateway = externalItemGateway;
            _performanceLogger = performanceLogger;
        }

        public ItemResponse Process(ItemRequest request)
        {
            return _performanceTracker.Measure("item processing workflow", () =>
            {
                _performanceTracker.Measure("step 1: get existing item from db", () =>
                {
                    var existingItem = _unitOfWork.ItemRepository.GetById(request?.Id ?? 0);
                    return existingItem;
                });

                _performanceTracker.Measure("step 2: perform calculations", () =>
                {
                    Thread.Sleep(200);
                });

                _performanceTracker.Measure("step 3: save item into db", () =>
                {
                    _unitOfWork.Commit();
                });

                _performanceTracker.Measure("step 4: send item to external system", () =>
                {
                    _externalItemGateway.Send(request);
                });

                return new ItemResponse
                {
                    Id = request?.Id ?? 1,
                    Status = "Processed"
                };
            });
        }

        public async Task<ItemResponse> ProcessAsync(ItemRequest request)
        {
            try
            {
                return await _performanceTracker.MeasureAsync("item processing workflow", async () =>
                {
                    await _performanceTracker.MeasureAsync("step 1: get existing item from db", async () =>
                    {
                        await Task.Delay(200);
                        _unitOfWork.ItemRepository.GetById(request?.Id ?? 0);
                    });

                    await _performanceTracker.MeasureAsync("step 2: perform calculations", async () =>
                    {
                        await Task.Delay(200);
                    });

                    await _performanceTracker.MeasureAsync("step 3: save item into db", async () =>
                    {
                        await Task.Delay(200);
                        _unitOfWork.Commit();
                    });

                    await _performanceTracker.MeasureAsync("step 4: send item to external system", async () =>
                    {
                        await Task.Delay(200);
                        _externalItemGateway.Send(request);
                    });

                    return new ItemResponse
                    {
                        Id = request?.Id ?? 1,
                        Status = "Processed"
                    };
                });
            }
            catch (Exception ex)
            {
                _performanceLogger.LogException("Item processing failed", ex);
                throw;
            }
        }
    }
}
