using Dapr.Client;
using DaprIoT.Ingestor.Models;
using DaprIoT.Ingestor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
builder.Services.AddSingleton<ThresholdService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ThresholdService>());

var app = builder.Build();

// Load secret from AWS Secrets Manager via Dapr secret store at startup.
// Demonstrates the Dapr Secrets building block — no credentials in config or code.
using (var startupClient = new DaprClientBuilder().Build())
{
    var secret = await startupClient.GetSecretAsync("secretstore", "dapr-iot-api-key");
    // Dapr returns secret as IDictionary<string,string>. The key "api-key" matches the
    // JSON field name inside the AWS Secrets Manager secret value: {"api-key": "<value>"}.
    var apiKey = secret.GetValueOrDefault("api-key", string.Empty);
    app.Logger.LogInformation("Loaded API key from Secrets Manager (length: {Len})", apiKey.Length);
    // In a real app you would store apiKey and validate it on each request.
}

app.MapPost("/sensors/{deviceId}/readings", async (
    string deviceId,
    SensorReading reading,
    DaprClient daprClient,
    ThresholdService thresholds,
    ILogger<Program> logger) =>
{
    logger.LogInformation(
        "Received reading from {DeviceId}: {Value}{Unit}",
        deviceId, reading.Value, reading.Unit);

    var request = new ProcessRequest(deviceId, reading, new Dictionary<string, string>(thresholds.Current));

    try
    {
        await daprClient.InvokeMethodAsync(
            HttpMethod.Post,
            "dapr-iot-processor",
            "process-reading",
            request);

        return Results.Ok(new { status = "accepted", deviceId });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to forward reading to Processor");
        return Results.Problem("Failed to process sensor reading.");
    }
});

app.Run();
