# DaprIoT Sample App

A minimal two-service IoT sensor pipeline that demonstrates six Dapr building blocks on AWS EKS.

| Dapr Building Block | How It's Used in This Project | AWS Backing |
|---|---|---|
| **Secrets** | Ingestor loads an API key from AWS Secrets Manager at startup. No credentials in code or config — the sidecar handles AWS auth via IRSA. | AWS Secrets Manager |
| **External Configuration** (with hot reload) | `ThresholdService` subscribes to alert thresholds in Redis. When updated, the Ingestor picks them up live without restarting — simulating a fleet operator tuning alert levels in real time. | ElastiCache (Redis) |
| **Service Invocation** | The Ingestor forwards each reading to the Processor via Dapr. Dapr handles service discovery, mTLS, and the resiliency policy transparently. | *(Dapr native)* |
| **Distributed Lock** | The Processor acquires a per-device Redis lock before processing. Concurrent requests for the same device return `409 Conflict`. | ElastiCache (Redis) |
| **Actors** | One `DeviceActor` per device maintains a rolling reading history in DynamoDB and evaluates each reading against the current thresholds. | DynamoDB |
| **Workflow** | On anomaly detection, the Processor launches an `AnomalyDetectionWorkflow` — Validate → Analyze → Alert — with state durably persisted so the pipeline survives pod restarts. | DynamoDB |
| **Resiliency** | All Ingestor → Processor calls are wrapped with 3 exponential retries, a 5s timeout, and a circuit breaker tripping after 5 consecutive failures. | *(Dapr native)* |

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

### Fresh-cluster bootstrap notes

Terraform provisions the following EKS prerequisites:

- EKS access entry and cluster-admin policy for the Terraform caller
- EBS CSI addon installation
- Node role permission for EBS CSI (`AmazonEBSCSIDriverPolicy`)

After `terraform apply`, create the default StorageClass:

```bash
kubectl apply -f - <<EOF
apiVersion: storage.k8s.io/v1
kind: StorageClass
metadata:
  name: ebs-gp3-default
  annotations:
    storageclass.kubernetes.io/is-default-class: "true"
provisioner: ebs.csi.aws.com
reclaimPolicy: Delete
volumeBindingMode: WaitForFirstConsumer
allowVolumeExpansion: true
parameters:
  type: gp3
  fsType: ext4
EOF
```

Wait until EBS CSI is healthy before running Dapr init:

```bash
kubectl get pods -n kube-system | grep -i ebs
kubectl get storageclass
```

Expected: EBS CSI controller/node pods are `Running`, and `ebs-gp3-default` shows as the default StorageClass.

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
  MSET maxTemperature 50 minPressure 10
kubectl delete pod redis-seed -n dapr-iot
```

### 5. Create the Redis host K8s secret (used by Dapr component YAMLs)

```bash
kubectl create secret generic dapr-iot-redis-host \
  --from-literal=host="<REDIS_HOST>:6379" \
  -n dapr-iot
```

### 6. Apply Dapr components

If you changed `var.aws_region` in Terraform to something other than `us-east-1`, update the `region` field in `dapr/components/secretstore.yaml` and `dapr/components/statestore.yaml` to match before applying.

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
