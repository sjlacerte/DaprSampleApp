using Dapr.Workflow;
using DaprIoT.Processor.Workflows;

namespace DaprIoT.Processor.Workflows.Activities;

public class AnalyzeReadingActivity : WorkflowActivity<AnomalyWorkflowInput, string>
{
    private readonly ILogger<AnalyzeReadingActivity> _logger;

    public AnalyzeReadingActivity(ILogger<AnalyzeReadingActivity> logger)
    {
        _logger = logger;
    }

    public override Task<string> RunAsync(WorkflowActivityContext context, AnomalyWorkflowInput input)
    {
        var average = input.History.Count > 0
            ? input.History.Average(r => r.Value)
            : 0f;
        var delta = input.Reading.Value - average;
        var message = $"Average of last {input.History.Count} readings: {average:F1}. " +
                      $"Current: {input.Reading.Value:F1}. Delta: {delta:F1}. Anomaly confirmed.";

        _logger.LogInformation("[Analyze] Device {DeviceId}: {Message}", input.DeviceId, message);

        return Task.FromResult(message);
    }
}
