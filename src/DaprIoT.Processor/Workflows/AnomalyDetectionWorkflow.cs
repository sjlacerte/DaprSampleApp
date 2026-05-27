using Dapr.Workflow;
using DaprIoT.Processor.Workflows.Activities;

namespace DaprIoT.Processor.Workflows;

public class AnomalyDetectionWorkflow : Workflow<AnomalyWorkflowInput, string>
{
    public override async Task<string> RunAsync(
        WorkflowContext context, AnomalyWorkflowInput input)
    {
        var isValid = await context.CallActivityAsync<bool>(
            nameof(ValidateReadingActivity), input);

        if (!isValid)
            return "Reading failed validation — workflow aborted.";

        var analysis = await context.CallActivityAsync<string>(
            nameof(AnalyzeReadingActivity), input);

        var alertInput = new AlertInput(
            input.DeviceId, input.Reading, input.Threshold, analysis);

        await context.CallActivityAsync<string>(
            nameof(AlertActivity), alertInput);

        return analysis;
    }
}
