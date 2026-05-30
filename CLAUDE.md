# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Purpose

This is a Dapr sample app — a minimal IoT sensor pipeline demonstrating six Dapr building blocks on AWS EKS. It is intentionally kept simple; resist adding abstractions or features beyond what is needed to illustrate the Dapr concept at hand.

## Build & Run

```bash
# Build entire solution
dotnet build DaprSampleApp.sln

# Build a single project
dotnet build src/DaprIoT.Ingestor/DaprIoT.Ingestor.csproj
dotnet build src/DaprIoT.Processor/DaprIoT.Processor.csproj

# Run locally (Dapr sidecar required; services need a live Dapr runtime)
dapr run --app-id dapr-iot-ingestor --app-port 5000 -- dotnet run --project src/DaprIoT.Ingestor
dapr run --app-id dapr-iot-processor --app-port 5001 -- dotnet run --project src/DaprIoT.Processor
```

There are no automated tests in this repo — correctness is validated by running the full stack and exercising the `POST /sensors/{deviceId}/readings` endpoint (see README for curl examples).

## Architecture

Two ASP.NET Core Minimal API services, each with a Dapr sidecar injected at the pod level in Kubernetes.

### Data flow

```
POST /sensors/{deviceId}/readings
       │
  DaprIoT.Ingestor
  ├── Reads secret at startup via secretstore (AWS Secrets Manager)
  ├── Watches threshold config via ThresholdService (Redis/External Config, hot-reload)
  └── Forwards reading via Dapr Service Invocation → DaprIoT.Processor
                                 │
                         DaprIoT.Processor
                         ├── Acquires per-device Distributed Lock (Redis)
                         ├── Calls DeviceActor (one actor per deviceId — state in DynamoDB)
                         │   └── Returns AnomalyDetected=true if value exceeds threshold
                         └── If anomaly: starts AnomalyDetectionWorkflow (state in DynamoDB)
                                 ├── ValidateReadingActivity
                                 ├── AnalyzeReadingActivity (computes delta from actor history)
                                 └── AlertActivity (logs the alert)
```

### Dapr component → AWS mapping

| Dapr component | File | AWS backing |
|---|---|---|
| `secretstore` | `dapr/components/secretstore.yaml` | AWS Secrets Manager |
| `configstore` | `dapr/components/configuration.yaml` | ElastiCache (Redis) |
| `lockstore` | `dapr/components/lock.yaml` | ElastiCache (Redis, same instance) |
| `statestore` | `dapr/components/statestore.yaml` | DynamoDB (`dapr-iot-state` table) |
| Service Invocation | — | Dapr native (no AWS service) |
| Resiliency | `dapr/components/resiliency.yaml` | Dapr native |

Both Redis components (`configstore` and `lockstore`) share a single ElastiCache instance. The `statestore` backs both the Actor state and Workflow state.

### IAM / auth

Both pods use IRSA (IAM Roles for Service Accounts). The role ARNs come from Terraform outputs and are substituted into the K8s manifests at deploy time via `sed`. No static AWS credentials anywhere in code.

### Key design constraints (from directives.txt)

- **Cost first**: t3.small nodes, cache.t3.micro Redis, DynamoDB on-demand. All resources tagged `project = "dapr-iot-sample"`.
- **All infrastructure via Terraform** — a single `terraform destroy` removes everything billable.
- **AWS AppConfig has no Dapr component** — Redis is the correct External Configuration backing (this is a decided choice, not an oversight).
- The `CS0618` warning in Ingestor is suppressed intentionally — `InvokeMethodAsync` is used to demonstrate the Service Invocation building block.

## Terraform

All AWS infrastructure lives in `terraform/`. After `terraform apply`, three key outputs are needed for deployment:
- `redis_host` — used to seed Redis and create the `dapr-iot-redis-host` K8s secret
- `ingestor_role_arn` / `processor_role_arn` — substituted into K8s manifests

If `var.aws_region` is changed from `us-east-1`, also update the `region` field in `dapr/components/secretstore.yaml` and `dapr/components/statestore.yaml`.

## Dapr SDK version

Both services use Dapr SDK **1.17.9** (`Dapr.AspNetCore`, `Dapr.Actors`, `Dapr.Workflow`, `Dapr.Extensions.Configuration`). The CLI target for cluster install is `dapr init -k --namespace dapr-iot`.
