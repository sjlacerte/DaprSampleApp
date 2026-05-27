using System.Collections.Concurrent;
using Dapr.Client;

namespace DaprIoT.Ingestor.Services;

public class ThresholdService : BackgroundService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<ThresholdService> _logger;
    private const string ConfigStore = "configstore";
    private static readonly IReadOnlyList<string> Keys = ["maxTemperature", "minPressure"];

    public ConcurrentDictionary<string, string> Current { get; } = new(
        new Dictionary<string, string> { ["maxTemperature"] = "50", ["minPressure"] = "10" });

    public ThresholdService(DaprClient daprClient, ILogger<ThresholdService> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial load
        var initial = await _daprClient.GetConfiguration(ConfigStore, Keys,
            cancellationToken: stoppingToken);
        foreach (var (key, item) in initial.Items)
        {
            if (!string.IsNullOrEmpty(item.Value))
            {
                Current[key] = item.Value;
                _logger.LogInformation("Loaded config {Key} = {Value}", key, item.Value);
            }
        }

        // Subscribe to live updates
        var subscription = await _daprClient.SubscribeConfiguration(ConfigStore, Keys,
            cancellationToken: stoppingToken);

        await foreach (var update in subscription.Source.WithCancellation(stoppingToken))
        {
            foreach (var (key, item) in update)
            {
                if (!string.IsNullOrEmpty(item.Value))
                {
                    Current[key] = item.Value;
                    _logger.LogInformation("Config updated: {Key} = {Value}", key, item.Value);
                }
            }
        }
    }
}
