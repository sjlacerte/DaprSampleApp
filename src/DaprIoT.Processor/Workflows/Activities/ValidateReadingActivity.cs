using Dapr.Workflow;
using DaprIoT.Processor.Workflows;

namespace DaprIoT.Processor.Workflows.Activities;

public class ValidateReadingActivity : WorkflowActivity<AnomalyWorkflowInput, bool>
{
    private readonly ILogger<ValidateReadingActivity> _logger;

    public ValidateReadingActivity(ILogger<ValidateReadingActivity> logger)
    {
        _logger = logger;
    }

    public override Task<bool> RunAsync(WorkflowActivityContext context, AnomalyWorkflowInput input)
    {
        var isValid = !float.IsNaN(input.Reading.Value)
            && !string.IsNullOrWhiteSpace(input.Reading.Unit)
            && input.Reading.Timestamp > DateTimeOffset.MinValue;

        _logger.LogInformation("[Validate] Reading {Value}{Unit} for {DeviceId}: {Result}",
            input.Reading.Value, input.Reading.Unit, input.DeviceId,
            isValid ? "valid" : "invalid");

        return Task.FromResult(isValid);
    }
}
