using Dapr.Workflow;

namespace DaprIoT.Processor.Workflows.Activities;

public class AlertActivity : WorkflowActivity<AlertInput, string>
{
    private readonly ILogger<AlertActivity> _logger;

    public AlertActivity(ILogger<AlertActivity> logger)
    {
        _logger = logger;
    }

    public override Task<string> RunAsync(WorkflowActivityContext context, AlertInput input)
    {
        _logger.LogWarning(
            "[Alert] ANOMALY DETECTED — device: {DeviceId}, value: {Value}{Unit}, " +
            "threshold: {Threshold}. {Analysis}",
            input.DeviceId, input.Reading.Value, input.Reading.Unit,
            input.Threshold, input.Analysis);

        return Task.FromResult("alert-sent");
    }
}
