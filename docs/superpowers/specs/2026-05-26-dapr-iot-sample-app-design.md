# DaprIoT Sample App — Design Spec

**Date:** 2026-05-26
**Domain:** IoT sensor pipeline
**Goal:** A minimal but complete sample app that teaches Dapr's core building blocks to developers, deployed on AWS EKS.

---

## 1. Overview

Two C# (.NET) services run as Kubernetes pods on AWS EKS. An IoT device POSTs sensor readings to **DaprIoT.Ingestor**, which validates the payload, reads current alert thresholds from External Configuration, and forwards the reading to **DaprIoT.Processor** via Dapr Service Invocation. The Processor manages per-device history using Actors, detects anomalies with a multi-step Workflow, and prevents duplicate processing with a Distributed Lock. All AWS infrastructure is provisioned with Terraform and torn down with a single `terraform destroy`.

---

## 2. Services

### DaprIoT.Ingestor (Pod 1)

**Type:** ASP.NET Core Minimal API

**Endpoint:**
- `POST /sensors/{deviceId}/readings` — accepts a JSON body `{ "value": float, "unit": string, "timestamp": ISO8601 }`

**Dapr building blocks used:**

| Block | How it's used |
|---|---|
| Secrets | On startup, loads app credentials (e.g., a dummy API key) from AWS Secrets Manager via the Dapr secret store API. Demonstrates secret retrieval without embedding credentials in config or code. |
| External Configuration | Subscribes to alert thresholds (`maxTemperature`, `minPressure`) stored as Redis keys in ElastiCache. A background service uses `DaprClient.SubscribeConfiguration()` to stream updates — the app hot-reloads thresholds without restarting. Thresholds are seeded at deploy time and can be updated live with the Redis CLI to demonstrate the feature. Note: AWS AppConfig has no Dapr component; Redis (`configuration.redis`) is used instead. |
| Service Invocation | Forwards the validated reading to `DaprIoT.Processor/process-reading` via the Dapr sidecar. The request body includes the reading and the current alert thresholds (read from External Config), so the Processor doesn't need its own AppConfig dependency. |

---

### DaprIoT.Processor (Pod 2)

**Type:** ASP.NET Core — Dapr Actor host

**Endpoint (Dapr service invocation target):** `POST /process-reading`

**Invoked by:** Ingestor via Dapr Service Invocation (not directly exposed to the internet)

**Dapr building blocks used:**

| Block | How it's used |
|---|---|
| Distributed Lock | Acquires a per-device lock (keyed on `deviceId`) from Redis before processing. Prevents duplicate or concurrent processing of readings for the same device. Released in a `finally` block. |
| Actors | Routes the reading to a `DeviceActor` instance for that `deviceId`. The Actor stores a rolling history of the last 10 readings in its state (DynamoDB via the Dapr state store). |
| Workflow | If the incoming reading exceeds the threshold supplied by the Ingestor, the Actor starts an `AnomalyDetectionWorkflow`. The workflow runs three activities in sequence: **Validate** (confirm reading is well-formed), **Analyze** (compare against historical average), **Alert** (log an anomaly record to the console). |

---

## 3. Data Flow

```
IoT Device
    │
    │ POST /sensors/{deviceId}/readings
    ▼
DaprIoT.Ingestor
    │ 1. Read secrets (Secrets Manager) — startup only
    │ 2. Read current thresholds (AppConfig External Config)
    │ 3. Dapr Service Invocation → Processor
    ▼
DaprIoT.Processor
    │ 4. Acquire Distributed Lock (Redis) for deviceId
    │ 5. Route to DeviceActor(deviceId)
    │ 6.   Actor stores reading in state (DynamoDB)
    │ 7.   If reading > threshold → start AnomalyDetectionWorkflow
    │ 8.     Workflow: Validate → Analyze → Alert (console log)
    │ 9. Release lock
    └─ Return 200 OK to Ingestor
```

---

## 4. AWS Resources

All resources provisioned by Terraform. All tagged `project = "dapr-iot-sample"`.

| Resource | Purpose | Sizing |
|---|---|---|
| EKS Cluster | Runs both pods + Dapr control plane | 2× t3.small worker nodes |
| AWS Secrets Manager | Backing store for Dapr secret store | 1–2 secrets |
| Amazon ElastiCache (Redis) | Backing store for Dapr External Configuration (same instance as lock) | cache.t3.micro, single node, single AZ |
| Amazon DynamoDB | Dapr state store — Actor state + Workflow state | On-demand billing |
| Amazon ElastiCache (Redis) | Dapr Distributed Lock | cache.t3.micro, single node, single AZ |
| IAM / IRSA | Pod-level AWS auth — no static credentials | One role per service |

**Note:** ElastiCache serves dual purpose — Dapr Distributed Lock (`lock.redis`) and Dapr External Configuration (`configuration.redis`) share the same Redis instance. AppConfig has been removed from the stack.

**Estimated cost while running:** ~$5–7/day (dominated by EKS control plane, EC2 nodes, and NAT Gateway). Always run `terraform destroy` when not actively using the stack.

---

## 5. Dapr Component Files

Located in `dapr/components/`. Applied via `kubectl apply -f dapr/components/` in namespace `dapr-iot`.

| File | Dapr type | AWS service |
|---|---|---|
| `secretstore.yaml` | `secretstores.aws.secretmanager` | AWS Secrets Manager |
| `configuration.yaml` | `configuration.redis` | Amazon ElastiCache (Redis) — same instance as lock, different Dapr component |
| `statestore.yaml` | `state.aws.dynamodb` | Amazon DynamoDB |
| `lock.yaml` | `lock.redis` | Amazon ElastiCache (Redis) |
| `resiliency.yaml` | Dapr resiliency policy | n/a |

**`resiliency.yaml`** configures:
- Service invocation: 3 retries with exponential backoff, 5-second timeout
- State operations: 3 retries
- Circuit breaker on service invocation after 5 consecutive failures

---

## 6. Project Structure

```
DaprSampleApp/
├── src/
│   ├── DaprIoT.Ingestor/
│   │   ├── Program.cs
│   │   ├── Models/SensorReading.cs
│   │   └── DaprIoT.Ingestor.csproj
│   └── DaprIoT.Processor/
│       ├── Program.cs
│       ├── Actors/
│       │   ├── IDeviceActor.cs
│       │   └── DeviceActor.cs
│       ├── Workflows/
│       │   ├── AnomalyDetectionWorkflow.cs
│       │   └── Activities/
│       │       ├── ValidateReadingActivity.cs
│       │       ├── AnalyzeReadingActivity.cs
│       │       └── AlertActivity.cs
│       ├── Models/SensorReading.cs
│       └── DaprIoT.Processor.csproj
├── dapr/components/
│   ├── secretstore.yaml
│   ├── statestore.yaml
│   ├── lock.yaml
│   ├── configuration.yaml
│   └── resiliency.yaml
├── k8s/
│   ├── ingestor-deployment.yaml
│   └── processor-deployment.yaml
├── terraform/
│   ├── main.tf
│   ├── variables.tf
│   ├── outputs.tf
│   ├── vpc.tf
│   ├── eks.tf
│   ├── dynamodb.tf
│   ├── elasticache.tf
│   ├── secrets-manager.tf
│   └── iam.tf
├── README.md
└── DaprSampleApp.sln
```

---

## 7. Error Handling

- **`resiliency.yaml`** handles transient failures at the Dapr layer (retries, timeouts, circuit breaker) — no C# retry code needed for those cases.
- **Distributed Lock contention:** if the lock cannot be acquired (already held), the Processor returns HTTP 409 Conflict. The Ingestor surfaces this as a 409 to the caller.
- **Unrecoverable errors** (e.g., DynamoDB unavailable, Actor throws): caught in a top-level `try/catch`, logged, and returned as HTTP 500 with a descriptive message. No silent swallowing of errors.
- **Workflow activity failure:** Dapr Workflow retries failed activities automatically per the resiliency policy. If all retries are exhausted the workflow instance moves to a failed state, which is logged.

---

## 8. Documentation

No automated tests. Instead, a `README.md` covers:

1. **Prerequisites** — AWS CLI, kubectl, Helm, Terraform, Dapr CLI, .NET SDK
2. **Deploy** — `terraform apply`, `helm install dapr`, `kubectl apply`, `docker build` + push, `kubectl apply` for K8s manifests
3. **Try it** — example `curl` commands to POST sensor readings and observe Actor state / Workflow execution in logs
4. **Teardown** — `terraform destroy` to remove all billable AWS resources; estimated cost reminder
5. **Cost estimate** — per-hour and per-day breakdown so the reader knows what the stack costs while running

---

## 9. Testing with Postman

### Getting the Ingestor URL

After deploying, get the Ingestor's external address one of two ways:

**Option A — LoadBalancer (deployed to EKS):**
```
kubectl get svc dapr-iot-ingestor -n dapr-iot
```
Copy the value under `EXTERNAL-IP`. Your base URL is `http://<EXTERNAL-IP>`.

**Option B — Port-forward (local dev / cost-free):**
```
kubectl port-forward svc/dapr-iot-ingestor 5000:80 -n dapr-iot
```
Your base URL is `http://localhost:5000`.

---

### Postman Request Setup

| Field | Value |
|---|---|
| Method | `POST` |
| URL | `http://<BASE_URL>/sensors/device-001/readings` |
| Header | `Content-Type: application/json` |

The `deviceId` in the URL path (`device-001`) determines which `DeviceActor` instance is addressed. Use any string — each unique ID gets its own Actor with independent state.

---

### Example Payloads

**Normal reading — below threshold, no workflow triggered:**
```json
{
  "value": 22.5,
  "unit": "celsius",
  "timestamp": "2026-05-26T14:00:00Z"
}
```
Expected response: `200 OK`. The DeviceActor records this reading in its state history. No workflow starts.

---

**Anomalous reading — above threshold, workflow triggered:**
```json
{
  "value": 95.0,
  "unit": "celsius",
  "timestamp": "2026-05-26T14:01:00Z"
}
```
Expected response: `200 OK`. The DeviceActor records the reading, detects it exceeds the `maxTemperature` threshold from AppConfig, and starts `AnomalyDetectionWorkflow`. You will see the three workflow activities log to the Processor pod output:

```
[Validate]  Reading 95.0°C for device-001 is well-formed.
[Analyze]   Average of last 10 readings: 23.1°C. Delta: 71.9°C. Anomaly confirmed.
[Alert]     ANOMALY DETECTED — device: device-001, value: 95.0°C, threshold: 50.0°C
```

To watch live: `kubectl logs -f deployment/dapr-iot-processor -n dapr-iot`

---

**Lock contention test — fire two requests for the same device simultaneously:**

Send two rapid POST requests for the same `deviceId` (e.g., using Postman's "Send" button twice quickly, or the Collection Runner). One will succeed with `200 OK`; the other will receive `409 Conflict` because the Distributed Lock is already held for that device.

---

**Different device — independent Actor instance:**
```json
{
  "value": 88.0,
  "unit": "celsius",
  "timestamp": "2026-05-26T14:02:00Z"
}
```
URL: `http://<BASE_URL>/sensors/device-002/readings`

`device-002` has a fresh Actor with no reading history. Its workflow runs independently of `device-001`.

---

## 10. Out of Scope

The following Dapr building blocks are intentionally excluded from this sample (see `scratch-pad.txt` for the full list):

- State Management (used internally by Actors/Workflow, but not demonstrated as a standalone building block)
- Publish & Subscribe
- Bindings
- Cryptography
- Conversation
- Middleware
