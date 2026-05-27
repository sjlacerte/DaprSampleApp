using Dapr.Actors;
using Dapr.Actors.Client;
using Dapr.Client;
using Dapr.Workflow;
using DaprIoT.Processor.Actors;
using DaprIoT.Processor.Models;
using DaprIoT.Processor.Workflows;
using DaprIoT.Processor.Workflows.Activities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();

builder.Services.AddActors(options =>
{
    options.Actors.RegisterActor<DeviceActor>();
});

builder.Services.AddDaprWorkflow(options =>
{
    options.RegisterWorkflow<AnomalyDetectionWorkflow>();
    options.RegisterActivity<ValidateReadingActivity>();
    options.RegisterActivity<AnalyzeReadingActivity>();
    options.RegisterActivity<AlertActivity>();
});

var app = builder.Build();

app.MapPost("/process-reading", async (
    ProcessRequest request,
    DaprClient daprClient,
    DaprWorkflowClient workflowClient,
    ILogger<Program> logger) =>
{
    var lockOwner = Guid.NewGuid().ToString();
    const string lockStore = "lockstore";

    var lockResponse = await daprClient.Lock(
        lockStore, request.DeviceId, lockOwner, expiryInSeconds: 30);

    if (!lockResponse.Success)
    {
        logger.LogWarning("Could not acquire lock for device {DeviceId}", request.DeviceId);
        return Results.Conflict($"Device {request.DeviceId} is currently being processed.");
    }

    try
    {
        var actor = ActorProxy.Create<IDeviceActor>(
            new ActorId(request.DeviceId), nameof(DeviceActor));

        var actorInput = new ActorInput(request.Reading, request.Thresholds);
        var result = await actor.RecordReadingAsync(actorInput);

        if (result.AnomalyDetected)
        {
            var thresholdKey = request.Reading.Unit.Equals("bar", StringComparison.OrdinalIgnoreCase)
                ? "minPressure"
                : "maxTemperature";
            if (!float.TryParse(
                    request.Thresholds.GetValueOrDefault(thresholdKey, "50"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var threshold))
                threshold = 50f;

            var workflowInput = new AnomalyWorkflowInput(
                request.DeviceId, request.Reading, threshold, result.History);

            var instanceId =
                $"anomaly-{request.DeviceId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

            await workflowClient.ScheduleNewWorkflowAsync(
                name: nameof(AnomalyDetectionWorkflow),
                instanceId: instanceId,
                input: workflowInput);

            logger.LogInformation("Started workflow {InstanceId} for device {DeviceId}",
                instanceId, request.DeviceId);
        }

        return Results.Ok(new { processed = true, result.AnomalyDetected });
    }
    finally
    {
        await daprClient.Unlock(lockStore, request.DeviceId, lockOwner);
    }
});

app.MapActorsHandlers();

app.Run();
