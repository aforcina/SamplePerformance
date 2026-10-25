using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Puma.MDE.Trader.WebUI.Services.Data;

namespace Puma.MDE.Trader.WebUI.Services;

public class PubRequestPollerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPubRequestCache _cache;
    private readonly IOptions<PubRequestCacheOptions> _options;
    private readonly ILogger<PubRequestPollerHostedService> _logger;

    public PubRequestPollerHostedService(
        IServiceScopeFactory scopeFactory,
        IPubRequestCache cache,
        IOptions<PubRequestCacheOptions> options,
        ILogger<PubRequestPollerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var target = _options.Value.Target;
        var intervalSeconds = Math.Max(1, _options.Value.PollingIntervalSeconds);
        _logger.LogInformation(
            "Starting PubRequestPollerHostedService (Polling interval: {Interval}s, Target: {Target})",
            intervalSeconds,
            target);

        // 1. Initial Load of today's requests
        while (!stoppingToken.IsCancellationRequested && !_cache.IsInitialized)
        {
            try
            {
                var today = DateTime.Today;
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IPubRequestRepository>();

                _logger.LogInformation("Performing initial fetch of today's publication requests for {Today:yyyy-MM-dd}...", today);
                var initialRequests = await repo.GetTodayRequestsAsync(today, target, stoppingToken);
                _cache.Initialize(today, initialRequests);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Initial load of today's publication requests failed. Retrying in 5 seconds...");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        // 2. Periodic Polling Loop
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;

                var today = DateTime.Today;

                // Day rollover check (e.g. midnight passed)
                if (today != _cache.CacheDate)
                {
                    _logger.LogInformation(
                        "Detected day rollover from {PreviousDate:yyyy-MM-dd} to {NewDate:yyyy-MM-dd}. Reloading today's requests...",
                        _cache.CacheDate,
                        today);

                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IPubRequestRepository>();
                    var dayRequests = await repo.GetTodayRequestsAsync(today, target, stoppingToken);
                    _cache.Initialize(today, dayRequests);
                    continue;
                }

                // Incremental fetch: new IDs + status refresh for in-flight requests
                var maxId = _cache.MaxRequestId;
                var inFlightIds = _cache.GetInFlightRequestIds();

                using (var scope = _scopeFactory.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<IPubRequestRepository>();

                    var newRequestsTask = repo.GetNewRequestsAfterIdAsync(maxId, today, target, stoppingToken);
                    var inFlightTask = inFlightIds.Count > 0
                        ? repo.GetRequestsByIdsAsync(inFlightIds, stoppingToken)
                        : Task.FromResult<IReadOnlyList<PubRequestSummary>>(Array.Empty<PubRequestSummary>());

                    await Task.WhenAll(newRequestsTask, inFlightTask);

                    var newRequests = await newRequestsTask;
                    var updatedRequests = await inFlightTask;

                    if (newRequests.Count > 0 || updatedRequests.Count > 0)
                    {
                        _cache.ApplyUpdates(newRequests, updatedRequests);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during publication request polling tick");
            }
        }

        _logger.LogInformation("PubRequestPollerHostedService stopped");
    }
}
