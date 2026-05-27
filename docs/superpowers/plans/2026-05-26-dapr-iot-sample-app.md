# DaprIoT Sample App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a two-service IoT sensor pipeline on AWS EKS that demonstrates six Dapr building blocks: Secrets, External Configuration, Service Invocation, Distributed Lock, Actors, and Workflow.

**Architecture:** `DaprIoT.Ingestor` (ASP.NET Core Minimal API) receives sensor readings, loads secrets from AWS Secrets Manager, subscribes to alert thresholds from Redis via Dapr External Configuration, and forwards readings to `DaprIoT.Processor` via Dapr Service Invocation. The Processor acquires a Distributed Lock per device, records readings in a `DeviceActor` (DynamoDB-backed state), and triggers an `AnomalyDetectionWorkflow` when a threshold is exceeded.

**Tech Stack:** .NET 9, Dapr .NET SDK 1.14, Dapr Actors, Dapr Workflow, AWS EKS, DynamoDB, ElastiCache (Redis), Secrets Manager, Terraform ≥ 1.0, Helm, kubectl

---

## File Map

| File | Responsibility |
|---|---|
| `DaprIoT.Ingestor/Program.cs` | Startup: secrets, config subscription, HTTP endpoint, service invocation |
| `DaprIoT.Ingestor/Services/ThresholdService.cs` | Background service: subscribes to Redis config, exposes current thresholds |
| `DaprIoT.Ingestor/Models/SensorReading.cs` | Inbound HTTP body |
| `DaprIoT.Ingestor/Models/ProcessRequest.cs` | Outbound payload to Processor |
| `DaprIoT.Processor/Program.cs` | Startup: actors, workflow, process-reading endpoint with lock |
| `DaprIoT.Processor/Actors/IDeviceActor.cs` | Actor interface |
| `DaprIoT.Processor/Actors/DeviceActor.cs` | Actor: stores reading history, detects anomaly |
| `DaprIoT.Processor/Workflows/AnomalyDetectionWorkflow.cs` | Workflow: Validate → Analyze → Alert |
| `DaprIoT.Processor/Workflows/Activities/ValidateReadingActivity.cs` | Activity: validates reading fields |
| `DaprIoT.Processor/Workflows/Activities/AnalyzeReadingActivity.cs` | Activity: compares reading to history average |
| `DaprIoT.Processor/Workflows/Activities/AlertActivity.cs` | Activity: logs anomaly alert |
| `DaprIoT.Processor/Models/*.cs` | SensorReading, ProcessRequest, ActorInput, ActorResult, workflow I/O types |
| `dapr/components/secretstore.yaml` | Dapr secret store → AWS Secrets Manager |
| `dapr/components/statestore.yaml` | Dapr state store → DynamoDB (Actors + Workflow) |
| `dapr/components/lock.yaml` | Dapr lock store → Redis |
| `dapr/components/configuration.yaml` | Dapr config store → Redis (same instance) |
| `dapr/components/resiliency.yaml` | Retry/timeout/circuit-breaker policies |
| `k8s/namespace.yaml` | `dapr-iot` namespace |
| `k8s/ingestor-deployment.yaml` | Ingestor Deployment + Service + ServiceAccount |
| `k8s/processor-deployment.yaml` | Processor Deployment + Service + ServiceAccount |
| `terraform/main.tf` | Providers, terraform config, common locals/tags |
| `terraform/variables.tf` | Input variables |
| `terraform/outputs.tf` | Cluster endpoint, kubeconfig command |
| `terraform/vpc.tf` | VPC, public/private subnets, NAT gateway |
| `terraform/eks.tf` | EKS cluster + managed node group |
| `terraform/dynamodb.tf` | DynamoDB table for Dapr state |
| `terraform/elasticache.tf` | ElastiCache Redis cluster |
| `terraform/secrets-manager.tf` | Secrets Manager secret |
| `terraform/iam.tf` | IRSA roles for Ingestor and Processor pods |

---

## Task 1: Repository scaffold

**Files:**
- Create: `.gitignore`
- Create: `DaprSampleApp.sln`

- [ ] **Step 1: Initialise git and create .gitignore**

```bash
cd /path/to/DaprSampleApp
git init
```

Create `.gitignore`:

```
# .NET
bin/
obj/
*.user
.vs/

# Terraform
**/.terraform/
*.tfstate
*.tfstate.backup
*.tfvars
.terraform.lock.hcl

# Superpowers
.superpowers/

# Misc
.DS_Store
```

- [ ] **Step 2: Create the solution file**

```bash
dotnet new sln --name DaprSampleApp
```

Expected output: `The template "Solution File" was created successfully.`

- [ ] **Step 3: Create src directory**

```bash
mkdir -p src
```

- [ ] **Step 4: Commit**

```bash
git add .gitignore DaprSampleApp.sln
git commit -m "chore: initialise solution"
```

---

## Task 2: DaprIoT.Ingestor project

**Files:**
- Create: `src/DaprIoT.Ingestor/DaprIoT.Ingestor.csproj`
- Create: `src/DaprIoT.Ingestor/Models/SensorReading.cs`
- Create: `src/DaprIoT.Ingestor/Models/ProcessRequest.cs`

- [ ] **Step 1: Scaffold the project**

```bash
dotnet new web --name DaprIoT.Ingestor --output src/DaprIoT.Ingestor --framework net9.0
dotnet sln add src/DaprIoT.Ingestor/DaprIoT.Ingestor.csproj
```

- [ ] **Step 2: Replace the generated csproj with the correct one**

Replace the contents of `src/DaprIoT.Ingestor/DaprIoT.Ingestor.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Dapr.AspNetCore" Version="1.14.0" />
    <PackageReference Include="Dapr.Extensions.Configuration" Version="1.14.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create SensorReading model**

Create `src/DaprIoT.Ingestor/Models/SensorReading.cs`:

```csharp
namespace DaprIoT.Ingestor.Models;

public record SensorReading(float Value, string Unit, DateTimeOffset Timestamp);
```

- [ ] **Step 4: Create ProcessRequest model**

Create `src/DaprIoT.Ingestor/Models/ProcessRequest.cs`:

```csharp
namespace DaprIoT.Ingestor.Models;

public record ProcessRequest(
    string DeviceId,
    SensorReading Reading,
    Dictionary<string, string> Thresholds);
```

- [ ] **Step 5: Verify the project builds**

```bash
dotnet build src/DaprIoT.Ingestor/DaprIoT.Ingestor.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/DaprIoT.Ingestor/
git commit -m "feat: scaffold DaprIoT.Ingestor project with models"
```

---

## Task 3: DaprIoT.Processor project and models

**Files:**
- Create: `src/DaprIoT.Processor/DaprIoT.Processor.csproj`
- Create: `src/DaprIoT.Processor/Models/SensorReading.cs`
- Create: `src/DaprIoT.Processor/Models/ProcessRequest.cs`
- Create: `src/DaprIoT.Processor/Models/ActorInput.cs`
- Create: `src/DaprIoT.Processor/Models/ActorResult.cs`
- Create: `src/DaprIoT.Processor/Workflows/AnomalyWorkflowInput.cs`
- Create: `src/DaprIoT.Processor/Workflows/Activities/AlertInput.cs`

- [ ] **Step 1: Scaffold the project**

```bash
dotnet new web --name DaprIoT.Processor --output src/DaprIoT.Processor --framework net9.0
dotnet sln add src/DaprIoT.Processor/DaprIoT.Processor.csproj
```

- [ ] **Step 2: Replace the generated csproj**

Replace `src/DaprIoT.Processor/DaprIoT.Processor.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Dapr.AspNetCore" Version="1.14.0" />
    <PackageReference Include="Dapr.Actors.AspNetCore" Version="1.14.0" />
    <PackageReference Include="Dapr.Workflow" Version="1.14.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create shared model types**

Create `src/DaprIoT.Processor/Models/SensorReading.cs`:

```csharp
namespace DaprIoT.Processor.Models;

public record SensorReading(float Value, string Unit, DateTimeOffset Timestamp);
```

Create `src/DaprIoT.Processor/Models/ProcessRequest.cs`:

```csharp
namespace DaprIoT.Processor.Models;

public record ProcessRequest(
    string DeviceId,
    SensorReading Reading,
    Dictionary<string, string> Thresholds);
```

Create `src/DaprIoT.Processor/Models/ActorInput.cs`:

```csharp
namespace DaprIoT.Processor.Models;

public record ActorInput(SensorReading Reading, Dictionary<string, string> Thresholds);
```

Create `src/DaprIoT.Processor/Models/ActorResult.cs`:

```csharp
namespace DaprIoT.Processor.Models;

public record ActorResult(bool AnomalyDetected, List<SensorReading> History);
```

- [ ] **Step 4: Create workflow input/output types**

Create `src/DaprIoT.Processor/Workflows/AnomalyWorkflowInput.cs`:

```csharp
using DaprIoT.Processor.Models;

namespace DaprIoT.Processor.Workflows;

public record AnomalyWorkflowInput(
    string DeviceId,
    SensorReading Reading,
    float Threshold,
    List<SensorReading> History);
```

Create `src/DaprIoT.Processor/Workflows/Activities/AlertInput.cs`:

```csharp
using DaprIoT.Processor.Models;

namespace DaprIoT.Processor.Workflows.Activities;

public record AlertInput(
    string DeviceId,
    SensorReading Reading,
    float Threshold,
    string Analysis);
```

- [ ] **Step 5: Verify the project builds**

```bash
dotnet build src/DaprIoT.Processor/DaprIoT.Processor.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/DaprIoT.Processor/
git commit -m "feat: scaffold DaprIoT.Processor project with models"
```

---

## Task 4: DeviceActor

**Files:**
- Create: `src/DaprIoT.Processor/Actors/IDeviceActor.cs`
- Create: `src/DaprIoT.Processor/Actors/DeviceActor.cs`

- [ ] **Step 1: Create the Actor interface**

Create `src/DaprIoT.Processor/Actors/IDeviceActor.cs`:

```csharp
using Dapr.Actors;
using DaprIoT.Processor.Models;

namespace DaprIoT.Processor.Actors;

public interface IDeviceActor : IActor
{
    Task<ActorResult> RecordReadingAsync(ActorInput input);
}
```

- [ ] **Step 2: Implement the Actor**

Create `src/DaprIoT.Processor/Actors/DeviceActor.cs`:

```csharp
using Dapr.Actors.Runtime;
using DaprIoT.Processor.Models;

namespace DaprIoT.Processor.Actors;

public class DeviceActor : Actor, IDeviceActor
{
    private const string HistoryKey = "readingHistory";
    private const int MaxHistory = 10;

    public DeviceActor(ActorHost host) : base(host) { }

    public async Task<ActorResult> RecordReadingAsync(ActorInput input)
    {
        var history = await StateManager.GetOrAddStateAsync(HistoryKey, new List<SensorReading>());

        history.Add(input.Reading);
        if (history.Count > MaxHistory)
            history.RemoveAt(0);

        await StateManager.SetStateAsync(HistoryKey, history);

        var anomaly = DetectAnomaly(input.Reading, input.Thresholds);
        return new ActorResult(anomaly, new List<SensorReading>(history));
    }

    private static bool DetectAnomaly(SensorReading reading, Dictionary<string, string> thresholds)
    {
        if (reading.Unit.Equals("celsius", StringComparison.OrdinalIgnoreCase)
            && thresholds.TryGetValue("maxTemperature", out var maxTempStr)
            && float.TryParse(maxTempStr, out var maxTemp)
            && reading.Value > maxTemp)
            return true;

        if (reading.Unit.Equals("bar", StringComparison.OrdinalIgnoreCase)
            && thresholds.TryGetValue("minPressure", out var minPressStr)
            && float.TryParse(minPressStr, out var minPress)
            && reading.Value < minPress)
            return true;

        return false;
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build src/DaprIoT.Processor/DaprIoT.Processor.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/DaprIoT.Processor/Actors/
git commit -m "feat: add DeviceActor with reading history and anomaly detection"
```

---

## Task 5: Workflow activities

**Files:**
- Create: `src/DaprIoT.Processor/Workflows/Activities/ValidateReadingActivity.cs`
- Create: `src/DaprIoT.Processor/Workflows/Activities/AnalyzeReadingActivity.cs`
- Create: `src/DaprIoT.Processor/Workflows/Activities/AlertActivity.cs`

- [ ] **Step 1: Create ValidateReadingActivity**

Create `src/DaprIoT.Processor/Workflows/Activities/ValidateReadingActivity.cs`:

```csharp
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
```

- [ ] **Step 2: Create AnalyzeReadingActivity**

Create `src/DaprIoT.Processor/Workflows/Activities/AnalyzeReadingActivity.cs`:

```csharp
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
```

- [ ] **Step 3: Create AlertActivity**

Create `src/DaprIoT.Processor/Workflows/Activities/AlertActivity.cs`:

```csharp
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
```

- [ ] **Step 4: Verify build**

```bash
dotnet build src/DaprIoT.Processor/DaprIoT.Processor.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/DaprIoT.Processor/Workflows/Activities/
git commit -m "feat: add workflow activities (validate, analyze, alert)"
```

---

## Task 6: AnomalyDetectionWorkflow

**Files:**
- Create: `src/DaprIoT.Processor/Workflows/AnomalyDetectionWorkflow.cs`

- [ ] **Step 1: Create the workflow**

Create `src/DaprIoT.Processor/Workflows/AnomalyDetectionWorkflow.cs`:

```csharp
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
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/DaprIoT.Processor/DaprIoT.Processor.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/DaprIoT.Processor/Workflows/AnomalyDetectionWorkflow.cs
git commit -m "feat: add AnomalyDetectionWorkflow"
```

---

## Task 7: DaprIoT.Processor — Program.cs

**Files:**
- Modify: `src/DaprIoT.Processor/Program.cs`

- [ ] **Step 1: Replace the generated Program.cs**

Replace the entire contents of `src/DaprIoT.Processor/Program.cs`:

```csharp
using Dapr.Actors;
using Dapr.Actors.AspNetCore;
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
            if (!float.TryParse(
                    request.Thresholds.GetValueOrDefault("maxTemperature", "50"),
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
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/DaprIoT.Processor/DaprIoT.Processor.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/DaprIoT.Processor/Program.cs
git commit -m "feat: implement Processor endpoint with lock, actor, and workflow"
```

---

## Task 8: DaprIoT.Ingestor — ThresholdService

**Files:**
- Create: `src/DaprIoT.Ingestor/Services/ThresholdService.cs`

This background service connects to the Dapr configuration store (Redis) on startup, performs an initial read of the two threshold keys, then subscribes to changes via an async stream. When Redis values change, the in-memory dictionary is updated without restarting the pod.

- [ ] **Step 1: Create the ThresholdService**

Create `src/DaprIoT.Ingestor/Services/ThresholdService.cs`:

```csharp
using Dapr.Client;

namespace DaprIoT.Ingestor.Services;

public class ThresholdService : BackgroundService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<ThresholdService> _logger;
    private const string ConfigStore = "configstore";
    private static readonly IReadOnlyList<string> Keys = ["maxTemperature", "minPressure"];

    public Dictionary<string, string> Current { get; } = new()
    {
        ["maxTemperature"] = "50",
        ["minPressure"] = "10"
    };

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
            Current[key] = item.Value;
            _logger.LogInformation("Loaded config {Key} = {Value}", key, item.Value);
        }

        // Subscribe to live updates
        var subscription = await _daprClient.SubscribeConfiguration(ConfigStore, Keys,
            cancellationToken: stoppingToken);

        await foreach (var update in subscription.Source.WithCancellation(stoppingToken))
        {
            foreach (var (key, item) in update)
            {
                Current[key] = item.Value;
                _logger.LogInformation("Config updated: {Key} = {Value}", key, item.Value);
            }
        }
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/DaprIoT.Ingestor/DaprIoT.Ingestor.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/DaprIoT.Ingestor/Services/ThresholdService.cs
git commit -m "feat: add ThresholdService for Dapr External Configuration subscription"
```

---

## Task 9: DaprIoT.Ingestor — Program.cs

**Files:**
- Modify: `src/DaprIoT.Ingestor/Program.cs`

- [ ] **Step 1: Replace the generated Program.cs**

Replace the entire contents of `src/DaprIoT.Ingestor/Program.cs`:

```csharp
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
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/DaprIoT.Ingestor/DaprIoT.Ingestor.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Verify full solution builds**

```bash
dotnet build DaprSampleApp.sln
```

Expected: `Build succeeded.` for both projects.

- [ ] **Step 4: Commit**

```bash
git add src/DaprIoT.Ingestor/Program.cs
git commit -m "feat: implement Ingestor endpoint with secrets, config, and service invocation"
```

---

## Task 10: Dapr component YAML files

**Files:**
- Create: `dapr/components/secretstore.yaml`
- Create: `dapr/components/statestore.yaml`
- Create: `dapr/components/lock.yaml`
- Create: `dapr/components/configuration.yaml`
- Create: `dapr/components/resiliency.yaml`

- [ ] **Step 1: Create directory**

```bash
mkdir -p dapr/components
```

- [ ] **Step 2: Create secretstore.yaml**

Create `dapr/components/secretstore.yaml`:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: secretstore
  namespace: dapr-iot
spec:
  type: secretstores.aws.secretmanager
  version: v1
  metadata:
    - name: region
      value: "us-east-1"  # update to match your Terraform var.aws_region
```

- [ ] **Step 3: Create statestore.yaml**

Create `dapr/components/statestore.yaml`:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: statestore
  namespace: dapr-iot
spec:
  type: state.aws.dynamodb
  version: v1
  metadata:
    - name: table
      value: "dapr-iot-state"   # must match terraform/dynamodb.tf table name
    - name: region
      value: "us-east-1"
    - name: ttlAttributeName
      value: "ttl"
```

- [ ] **Step 4: Create lock.yaml**

Create `dapr/components/lock.yaml`:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: lockstore
  namespace: dapr-iot
spec:
  type: lock.redis
  version: v1
  metadata:
    - name: redisHost
      secretKeyRef:
        name: dapr-iot-redis-host
        key: host               # K8s secret populated by Terraform output
    - name: redisPassword
      value: ""
    - name: enableTLS
      value: "false"
```

- [ ] **Step 5: Create configuration.yaml**

Create `dapr/components/configuration.yaml`:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: configstore
  namespace: dapr-iot
spec:
  type: configuration.redis
  version: v1
  metadata:
    - name: redisHost
      secretKeyRef:
        name: dapr-iot-redis-host
        key: host               # same Redis instance as lockstore
    - name: redisPassword
      value: ""
    - name: enableTLS
      value: "false"
```

- [ ] **Step 6: Create resiliency.yaml**

Create `dapr/components/resiliency.yaml`:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Resiliency
metadata:
  name: dapr-iot-resiliency
  namespace: dapr-iot
spec:
  policies:
    retries:
      DefaultRetry:
        policy: exponential
        maxInterval: 10s
        maxRetries: 3
    timeouts:
      DefaultTimeout: 5s
    circuitBreakers:
      DefaultCB:
        maxRequests: 1
        interval: 10s
        timeout: 30s
        trip: consecutiveFailures >= 5
  targets:
    apps:
      dapr-iot-processor:
        retry: DefaultRetry
        timeout: DefaultTimeout
        circuitBreaker: DefaultCB
    components:
      statestore:
        inbound:
          retry: DefaultRetry
        outbound:
          retry: DefaultRetry
```

- [ ] **Step 7: Commit**

```bash
git add dapr/
git commit -m "feat: add Dapr component YAML files (secretstore, statestore, lock, config, resiliency)"
```

---

## Task 11: Kubernetes manifests

**Files:**
- Create: `k8s/namespace.yaml`
- Create: `k8s/ingestor-deployment.yaml`
- Create: `k8s/processor-deployment.yaml`

- [ ] **Step 1: Create directory**

```bash
mkdir -p k8s
```

- [ ] **Step 2: Create namespace.yaml**

Create `k8s/namespace.yaml`:

```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: dapr-iot
  labels:
    project: dapr-iot-sample
```

- [ ] **Step 3: Create ingestor-deployment.yaml**

Create `k8s/ingestor-deployment.yaml`:

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: dapr-iot-ingestor
  namespace: dapr-iot
  annotations:
    eks.amazonaws.com/role-arn: "${INGESTOR_ROLE_ARN}"  # replaced by deploy script
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: dapr-iot-ingestor
  namespace: dapr-iot
  labels:
    app: dapr-iot-ingestor
    project: dapr-iot-sample
spec:
  replicas: 1
  selector:
    matchLabels:
      app: dapr-iot-ingestor
  template:
    metadata:
      labels:
        app: dapr-iot-ingestor
      annotations:
        dapr.io/enabled: "true"
        dapr.io/app-id: "dapr-iot-ingestor"
        dapr.io/app-port: "8080"
        dapr.io/log-level: "info"
    spec:
      serviceAccountName: dapr-iot-ingestor
      containers:
        - name: ingestor
          image: "${INGESTOR_IMAGE}"  # replaced by deploy script
          ports:
            - containerPort: 8080
          env:
            - name: ASPNETCORE_URLS
              value: "http://+:8080"
---
apiVersion: v1
kind: Service
metadata:
  name: dapr-iot-ingestor
  namespace: dapr-iot
spec:
  selector:
    app: dapr-iot-ingestor
  ports:
    - port: 80
      targetPort: 8080
  type: LoadBalancer
```

- [ ] **Step 4: Create processor-deployment.yaml**

Create `k8s/processor-deployment.yaml`:

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: dapr-iot-processor
  namespace: dapr-iot
  annotations:
    eks.amazonaws.com/role-arn: "${PROCESSOR_ROLE_ARN}"  # replaced by deploy script
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: dapr-iot-processor
  namespace: dapr-iot
  labels:
    app: dapr-iot-processor
    project: dapr-iot-sample
spec:
  replicas: 1
  selector:
    matchLabels:
      app: dapr-iot-processor
  template:
    metadata:
      labels:
        app: dapr-iot-processor
      annotations:
        dapr.io/enabled: "true"
        dapr.io/app-id: "dapr-iot-processor"
        dapr.io/app-port: "8080"
        dapr.io/log-level: "info"
    spec:
      serviceAccountName: dapr-iot-processor
      containers:
        - name: processor
          image: "${PROCESSOR_IMAGE}"  # replaced by deploy script
          ports:
            - containerPort: 8080
          env:
            - name: ASPNETCORE_URLS
              value: "http://+:8080"
---
apiVersion: v1
kind: Service
metadata:
  name: dapr-iot-processor
  namespace: dapr-iot
spec:
  selector:
    app: dapr-iot-processor
  ports:
    - port: 80
      targetPort: 8080
  type: ClusterIP
```

- [ ] **Step 5: Commit**

```bash
git add k8s/
git commit -m "feat: add Kubernetes manifests (namespace, ingestor, processor)"
```

---

## Task 12: Terraform — foundation

**Files:**
- Create: `terraform/main.tf`
- Create: `terraform/variables.tf`
- Create: `terraform/outputs.tf`
- Create: `terraform/vpc.tf`

- [ ] **Step 1: Create terraform directory**

```bash
mkdir -p terraform
```

- [ ] **Step 2: Create main.tf**

Create `terraform/main.tf`:

```hcl
terraform {
  required_version = ">= 1.6"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    kubernetes = {
      source  = "hashicorp/kubernetes"
      version = "~> 2.0"
    }
  }
}

provider "aws" {
  region = var.aws_region
}

locals {
  common_tags = {
    project     = "dapr-iot-sample"
    environment = "demo"
    managed_by  = "terraform"
  }
}
```

- [ ] **Step 3: Create variables.tf**

Create `terraform/variables.tf`:

```hcl
variable "aws_region" {
  description = "AWS region for all resources"
  type        = string
  default     = "us-east-1"
}

variable "cluster_name" {
  description = "EKS cluster name"
  type        = string
  default     = "dapr-iot-eks"
}

variable "vpc_cidr" {
  description = "CIDR block for the VPC"
  type        = string
  default     = "10.0.0.0/16"
}
```

- [ ] **Step 4: Create outputs.tf**

Create `terraform/outputs.tf`:

```hcl
output "cluster_name" {
  value = module.eks.cluster_name
}

output "cluster_endpoint" {
  value = module.eks.cluster_endpoint
}

output "kubeconfig_command" {
  value = "aws eks update-kubeconfig --region ${var.aws_region} --name ${module.eks.cluster_name}"
}

output "redis_host" {
  value     = "${aws_elasticache_cluster.dapr_iot.cache_nodes[0].address}:${aws_elasticache_cluster.dapr_iot.cache_nodes[0].port}"
  sensitive = false
}

output "ingestor_role_arn" {
  value = aws_iam_role.ingestor.arn
}

output "processor_role_arn" {
  value = aws_iam_role.processor.arn
}
```

- [ ] **Step 5: Create vpc.tf**

Create `terraform/vpc.tf`:

```hcl
data "aws_availability_zones" "available" {}

module "vpc" {
  source  = "terraform-aws-modules/vpc/aws"
  version = "~> 5.0"

  name = "${var.cluster_name}-vpc"
  cidr = var.vpc_cidr

  azs             = slice(data.aws_availability_zones.available.names, 0, 2)
  private_subnets = ["10.0.1.0/24", "10.0.2.0/24"]
  public_subnets  = ["10.0.101.0/24", "10.0.102.0/24"]

  enable_nat_gateway   = true
  single_nat_gateway   = true   # cost optimisation: one NAT GW instead of one per AZ
  enable_dns_hostnames = true

  public_subnet_tags = {
    "kubernetes.io/role/elb" = 1
  }

  private_subnet_tags = {
    "kubernetes.io/role/internal-elb" = 1
  }

  tags = local.common_tags
}
```

- [ ] **Step 6: Commit**

```bash
git add terraform/main.tf terraform/variables.tf terraform/outputs.tf terraform/vpc.tf
git commit -m "feat: add Terraform foundation (providers, variables, outputs, VPC)"
```

---

## Task 13: Terraform — EKS cluster

**Files:**
- Create: `terraform/eks.tf`

- [ ] **Step 1: Create eks.tf**

Create `terraform/eks.tf`:

```hcl
module "eks" {
  source  = "terraform-aws-modules/eks/aws"
  version = "~> 20.0"

  cluster_name    = var.cluster_name
  cluster_version = "1.30"

  vpc_id                         = module.vpc.vpc_id
  subnet_ids                     = module.vpc.private_subnets
  cluster_endpoint_public_access = true

  enable_irsa = true

  eks_managed_node_groups = {
    default = {
      min_size       = 2
      max_size       = 2
      desired_size   = 2
      instance_types = ["t3.small"]

      labels = local.common_tags
    }
  }

  tags = local.common_tags
}
```

- [ ] **Step 2: Commit**

```bash
git add terraform/eks.tf
git commit -m "feat: add Terraform EKS cluster (t3.small x2)"
```

---

## Task 14: Terraform — DynamoDB, ElastiCache, and Secrets Manager

**Files:**
- Create: `terraform/dynamodb.tf`
- Create: `terraform/elasticache.tf`
- Create: `terraform/secrets-manager.tf`

- [ ] **Step 1: Create dynamodb.tf**

Create `terraform/dynamodb.tf`:

```hcl
resource "aws_dynamodb_table" "dapr_iot_state" {
  name         = "dapr-iot-state"
  billing_mode = "PAY_PER_REQUEST"   # on-demand — no minimum charge
  hash_key     = "key"

  attribute {
    name = "key"
    type = "S"
  }

  ttl {
    attribute_name = "ttl"
    enabled        = true
  }

  tags = local.common_tags
}
```

- [ ] **Step 2: Create elasticache.tf**

Create `terraform/elasticache.tf`:

```hcl
resource "aws_elasticache_subnet_group" "dapr_iot" {
  name       = "dapr-iot-redis-subnet-group"
  subnet_ids = module.vpc.private_subnets
}

resource "aws_security_group" "redis" {
  name        = "dapr-iot-redis-sg"
  description = "Allow Redis access from EKS nodes"
  vpc_id      = module.vpc.vpc_id

  ingress {
    from_port       = 6379
    to_port         = 6379
    protocol        = "tcp"
    security_groups = [module.eks.node_security_group_id]
  }

  tags = local.common_tags
}

resource "aws_elasticache_cluster" "dapr_iot" {
  cluster_id           = "dapr-iot-redis"
  engine               = "redis"
  node_type            = "cache.t3.micro"   # smallest viable size
  num_cache_nodes      = 1
  parameter_group_name = "default.redis7"
  engine_version       = "7.0"
  port                 = 6379
  subnet_group_name    = aws_elasticache_subnet_group.dapr_iot.name
  security_group_ids   = [aws_security_group.redis.id]

  tags = local.common_tags
}
```

- [ ] **Step 3: Create secrets-manager.tf**

Create `terraform/secrets-manager.tf`:

```hcl
resource "aws_secretsmanager_secret" "dapr_iot_api_key" {
  name                    = "dapr-iot-api-key"
  description             = "Sample API key for DaprIoT.Ingestor — demonstrates Dapr Secrets building block"
  recovery_window_in_days = 0   # immediate deletion on destroy (no 30-day window)

  tags = local.common_tags
}

resource "aws_secretsmanager_secret_version" "dapr_iot_api_key" {
  secret_id     = aws_secretsmanager_secret.dapr_iot_api_key.id
  secret_string = jsonencode({ "api-key" = "demo-key-replace-in-production" })
}
```

- [ ] **Step 4: Commit**

```bash
git add terraform/dynamodb.tf terraform/elasticache.tf terraform/secrets-manager.tf
git commit -m "feat: add Terraform DynamoDB, ElastiCache, and Secrets Manager resources"
```

---

## Task 15: Terraform — IAM / IRSA

**Files:**
- Create: `terraform/iam.tf`

Each pod gets its own IAM role, bound to its Kubernetes service account via IRSA (IAM Roles for Service Accounts). This means no static AWS credentials anywhere in the codebase — the Dapr sidecar inherits the pod's role automatically.

- [ ] **Step 1: Create iam.tf**

Create `terraform/iam.tf`:

```hcl
data "aws_caller_identity" "current" {}

# --- IRSA trust policy helper ---
data "aws_iam_policy_document" "irsa_assume" {
  for_each = toset(["ingestor", "processor"])

  statement {
    actions = ["sts:AssumeRoleWithWebIdentity"]
    principals {
      type        = "Federated"
      identifiers = [module.eks.oidc_provider_arn]
    }
    condition {
      test     = "StringEquals"
      variable = "${module.eks.oidc_provider}:sub"
      values   = ["system:serviceaccount:dapr-iot:dapr-iot-${each.key}"]
    }
    condition {
      test     = "StringEquals"
      variable = "${module.eks.oidc_provider}:aud"
      values   = ["sts.amazonaws.com"]
    }
  }
}

# --- Ingestor role: Secrets Manager + DynamoDB (for Dapr state) ---
resource "aws_iam_role" "ingestor" {
  name               = "dapr-iot-ingestor-role"
  assume_role_policy = data.aws_iam_policy_document.irsa_assume["ingestor"].json
  tags               = local.common_tags
}

resource "aws_iam_role_policy" "ingestor" {
  name = "dapr-iot-ingestor-policy"
  role = aws_iam_role.ingestor.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue", "secretsmanager:DescribeSecret"]
        Resource = aws_secretsmanager_secret.dapr_iot_api_key.arn
      }
    ]
  })
}

# --- Processor role: DynamoDB (Actor + Workflow state) ---
resource "aws_iam_role" "processor" {
  name               = "dapr-iot-processor-role"
  assume_role_policy = data.aws_iam_policy_document.irsa_assume["processor"].json
  tags               = local.common_tags
}

resource "aws_iam_role_policy" "processor" {
  name = "dapr-iot-processor-policy"
  role = aws_iam_role.processor.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:UpdateItem",
          "dynamodb:DeleteItem", "dynamodb:Query", "dynamodb:Scan",
          "dynamodb:BatchWriteItem", "dynamodb:DescribeTable"
        ]
        Resource = aws_dynamodb_table.dapr_iot_state.arn
      }
    ]
  })
}
```

- [ ] **Step 2: Commit**

```bash
git add terraform/iam.tf
git commit -m "feat: add Terraform IRSA roles for Ingestor and Processor pods"
```

---

## Task 16: README

**Files:**
- Create: `README.md`

- [ ] **Step 1: Create README.md**

Create `README.md`:

```markdown
# DaprIoT Sample App

A minimal two-service IoT sensor pipeline that demonstrates six Dapr building blocks on AWS EKS.

| Building Block | Where used | AWS backing |
|---|---|---|
| Secrets | Ingestor startup | AWS Secrets Manager |
| External Configuration | Ingestor threshold subscription | ElastiCache (Redis) |
| Service Invocation | Ingestor → Processor | Dapr native |
| Distributed Lock | Processor per-device lock | ElastiCache (Redis) |
| Actors | Processor DeviceActor | DynamoDB |
| Workflow | Processor AnomalyDetectionWorkflow | DynamoDB |

## Prerequisites

| Tool | Version |
|---|---|
| AWS CLI | v2 |
| kubectl | ≥ 1.28 |
| Helm | ≥ 3.12 |
| Terraform | ≥ 1.6 |
| Dapr CLI | ≥ 1.14 |
| .NET SDK | 9.0 |
| Docker | any recent |

Configure AWS credentials: `aws configure`

## Estimated Cost

| Resource | $/hour | $/day |
|---|---|---|
| EKS control plane | ~$0.10 | ~$2.40 |
| 2× t3.small EC2 nodes | ~$0.04 | ~$1.00 |
| NAT Gateway | ~$0.05 | ~$1.20 |
| cache.t3.micro Redis | ~$0.017 | ~$0.40 |
| DynamoDB (on-demand) | ~$0.00 | ~$0.00 |
| Secrets Manager | ~$0.00 | ~$0.01 |
| **Total** | **~$0.21** | **~$5.00** |

⚠️ **Always run `terraform destroy` when you are done.** Leaving the stack running costs ~$5/day.

## Deploy

### 1. Provision AWS infrastructure

```bash
cd terraform
terraform init
terraform apply
```

Note the outputs — you will need `redis_host`, `ingestor_role_arn`, and `processor_role_arn`.

### 2. Configure kubectl

```bash
$(terraform output -raw kubeconfig_command)
```

### 3. Install Dapr on the cluster

```bash
dapr init -k --namespace dapr-iot --wait
```

### 4. Create namespace and seed Redis config

```bash
kubectl apply -f k8s/namespace.yaml

# Seed initial alert thresholds into Redis
# Replace REDIS_HOST with the value from `terraform output redis_host`
kubectl run redis-seed --image=redis:7 --restart=Never -n dapr-iot -- \
  redis-cli -h <REDIS_HOST> -p 6379 \
  SET maxTemperature 50 SET minPressure 10
kubectl delete pod redis-seed -n dapr-iot
```

### 5. Create the Redis host K8s secret (used by Dapr component YAMLs)

```bash
kubectl create secret generic dapr-iot-redis-host \
  --from-literal=host="<REDIS_HOST>:6379" \
  -n dapr-iot
```

### 6. Apply Dapr components

```bash
kubectl apply -f dapr/components/
```

### 7. Build and push Docker images

```bash
# Replace ACCOUNT_ID and REGION with your values
export ECR_BASE=<ACCOUNT_ID>.dkr.ecr.<REGION>.amazonaws.com

aws ecr create-repository --repository-name dapr-iot-ingestor --region <REGION>
aws ecr create-repository --repository-name dapr-iot-processor --region <REGION>
aws ecr get-login-password --region <REGION> | docker login --username AWS --password-stdin $ECR_BASE

docker build -t $ECR_BASE/dapr-iot-ingestor:latest -f src/DaprIoT.Ingestor/Dockerfile src/DaprIoT.Ingestor
docker push $ECR_BASE/dapr-iot-ingestor:latest

docker build -t $ECR_BASE/dapr-iot-processor:latest -f src/DaprIoT.Processor/Dockerfile src/DaprIoT.Processor
docker push $ECR_BASE/dapr-iot-processor:latest
```

### 8. Deploy the services

```bash
# Fill in your role ARNs from Terraform outputs
INGESTOR_ROLE_ARN=$(cd terraform && terraform output -raw ingestor_role_arn)
PROCESSOR_ROLE_ARN=$(cd terraform && terraform output -raw processor_role_arn)

sed -e "s|\${INGESTOR_ROLE_ARN}|$INGESTOR_ROLE_ARN|g" \
    -e "s|\${INGESTOR_IMAGE}|$ECR_BASE/dapr-iot-ingestor:latest|g" \
    k8s/ingestor-deployment.yaml | kubectl apply -f -

sed -e "s|\${PROCESSOR_ROLE_ARN}|$PROCESSOR_ROLE_ARN|g" \
    -e "s|\${PROCESSOR_IMAGE}|$ECR_BASE/dapr-iot-processor:latest|g" \
    k8s/processor-deployment.yaml | kubectl apply -f -

kubectl rollout status deployment/dapr-iot-ingestor -n dapr-iot
kubectl rollout status deployment/dapr-iot-processor -n dapr-iot
```

## Try It

Get the Ingestor URL:

```bash
kubectl get svc dapr-iot-ingestor -n dapr-iot
# Copy EXTERNAL-IP
export BASE_URL=http://<EXTERNAL-IP>
```

Or use port-forward (no LoadBalancer needed):

```bash
kubectl port-forward svc/dapr-iot-ingestor 5000:80 -n dapr-iot &
export BASE_URL=http://localhost:5000
```

**Normal reading (no workflow):**

```bash
curl -s -X POST $BASE_URL/sensors/device-001/readings \
  -H "Content-Type: application/json" \
  -d '{"value": 22.5, "unit": "celsius", "timestamp": "2026-05-26T14:00:00Z"}' | jq
```

**Anomalous reading (triggers workflow):**

```bash
curl -s -X POST $BASE_URL/sensors/device-001/readings \
  -H "Content-Type: application/json" \
  -d '{"value": 95.0, "unit": "celsius", "timestamp": "2026-05-26T14:01:00Z"}' | jq
```

Watch the workflow execute in Processor logs:

```bash
kubectl logs -f deployment/dapr-iot-processor -n dapr-iot
```

Expected log lines:
```
[Validate]  Reading 95celsius for device-001: valid
[Analyze]   Device device-001: Average of last N readings: X.X. Current: 95.0. Delta: Y.Y. Anomaly confirmed.
[Alert]     ANOMALY DETECTED — device: device-001, value: 95celsius, threshold: 50 ...
```

**Lock contention test:** Send two `POST` requests for the same `deviceId` simultaneously. One returns `200 OK`, the other returns `409 Conflict`.

**Live config update (External Configuration demo):**

```bash
# Lower the threshold to 20°C so any reading above 20 triggers a workflow
kubectl run redis-update --image=redis:7 --restart=Never -n dapr-iot -- \
  redis-cli -h <REDIS_HOST> -p 6379 SET maxTemperature 20
kubectl delete pod redis-update -n dapr-iot

# Now a 22.5°C reading will trigger the anomaly workflow
curl -s -X POST $BASE_URL/sensors/device-002/readings \
  -H "Content-Type: application/json" \
  -d '{"value": 22.5, "unit": "celsius", "timestamp": "2026-05-26T14:05:00Z"}' | jq
```

The Ingestor picks up the new threshold without restarting (Dapr External Configuration hot-reload via `ThresholdService`).

## Teardown

```bash
# Remove K8s resources
kubectl delete -f k8s/
kubectl delete -f dapr/components/

# Destroy all AWS resources (removes all charges)
cd terraform
terraform destroy
```

Confirm the EKS cluster, ElastiCache instance, DynamoDB table, and Secrets Manager secret are gone in the AWS console before closing your session.
```

- [ ] **Step 2: Add Dockerfiles**

Create `src/DaprIoT.Ingestor/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["DaprIoT.Ingestor.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DaprIoT.Ingestor.dll"]
```

Create `src/DaprIoT.Processor/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["DaprIoT.Processor.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DaprIoT.Processor.dll"]
```

- [ ] **Step 3: Commit**

```bash
git add README.md src/DaprIoT.Ingestor/Dockerfile src/DaprIoT.Processor/Dockerfile
git commit -m "docs: add README with deploy, try-it, and teardown instructions"
```

---

## Self-Review Checklist

| Spec requirement | Covered by task |
|---|---|
| Secrets (AWS Secrets Manager) | Task 9 (Program.cs), Task 10 (secretstore.yaml), Task 15 (IAM) |
| External Configuration (Redis) | Task 8 (ThresholdService), Task 10 (configuration.yaml) |
| Service Invocation | Task 9 (InvokeMethodAsync) |
| Distributed Lock (Redis) | Task 7 (Lock/Unlock), Task 10 (lock.yaml) |
| Actors (DeviceActor) | Task 4, Task 7 |
| Workflow (AnomalyDetection) | Tasks 5, 6, 7 |
| Two services on EKS | Tasks 2–9 (code), Task 11 (K8s), Task 13 (EKS) |
| Terraform IaC | Tasks 12–15 |
| Cost-conscious sizing | Task 13 (t3.small, cache.t3.micro, on-demand DynamoDB) |
| `terraform destroy` teardown | Task 16 (README) |
| Cost estimate in README | Task 16 |
| Postman / curl examples | Task 16 (README Try It section) |
| .gitignore excludes tfvars | Task 1 |
| IRSA (no static credentials) | Task 15 |
| Dapr resiliency policy | Task 10 (resiliency.yaml) |
