# Helm Chart Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `sed`-based `kubectl apply` deploy step with a Helm chart that templates the two service deployments.

**Architecture:** A single Helm chart at `helm/` containing two templates (`ingestor.yaml`, `processor.yaml`), each holding the ServiceAccount + Deployment + Service for one service. The four deployment-time variables (`ecrBase`, `ingestor.roleArn`, `processor.roleArn`, image tags) move into `values.yaml`. The `k8s/namespace.yaml` stays as a plain `kubectl apply` step — it's Dapr infrastructure, not app workload.

**Tech Stack:** Helm 3, Kubernetes, existing k8s manifests as source of truth.

---

## File Map

| Action | Path |
|---|---|
| Create | `helm/Chart.yaml` |
| Create | `helm/values.yaml` |
| Create | `helm/templates/ingestor.yaml` |
| Create | `helm/templates/processor.yaml` |
| Delete | `k8s/ingestor-deployment.yaml` |
| Delete | `k8s/processor-deployment.yaml` |
| Modify | `README.md` (step 8 deploy command) |

---

### Task 1: Create chart scaffold

**Files:**
- Create: `helm/Chart.yaml`
- Create: `helm/values.yaml`

- [ ] **Step 1: Create `helm/Chart.yaml`**

```yaml
apiVersion: v2
name: dapr-iot
description: Dapr IoT sample app — two-service pipeline demonstrating Dapr building blocks on EKS
type: application
version: 0.1.0
appVersion: "1.0.0"
```

- [ ] **Step 2: Create `helm/values.yaml`**

```yaml
ecrBase: ""        # e.g. 473741442322.dkr.ecr.us-east-1.amazonaws.com
ingestor:
  roleArn: ""      # from: terraform output -raw ingestor_role_arn
  image:
    tag: latest
processor:
  roleArn: ""      # from: terraform output -raw processor_role_arn
  image:
    tag: latest
```

- [ ] **Step 3: Commit**

```bash
git add helm/Chart.yaml helm/values.yaml
git commit -m "feat: add Helm chart scaffold"
```

---

### Task 2: Create ingestor template

**Files:**
- Create: `helm/templates/ingestor.yaml`

- [ ] **Step 1: Create `helm/templates/ingestor.yaml`**

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: dapr-iot-ingestor
  namespace: dapr-iot
  annotations:
    eks.amazonaws.com/role-arn: {{ .Values.ingestor.roleArn }}
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
        dapr.io/app-protocol: "http"
        dapr.io/log-level: "info"
        dapr.io/wait-max-seconds: "30"
    spec:
      serviceAccountName: dapr-iot-ingestor
      containers:
        - name: ingestor
          image: {{ .Values.ecrBase }}/dapr-iot-ingestor:{{ .Values.ingestor.image.tag }}
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

- [ ] **Step 2: Commit**

```bash
git add helm/templates/ingestor.yaml
git commit -m "feat: add ingestor Helm template"
```

---

### Task 3: Create processor template

**Files:**
- Create: `helm/templates/processor.yaml`

- [ ] **Step 1: Create `helm/templates/processor.yaml`**

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: dapr-iot-processor
  namespace: dapr-iot
  annotations:
    eks.amazonaws.com/role-arn: {{ .Values.processor.roleArn }}
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
        dapr.io/app-protocol: "http"
        dapr.io/log-level: "info"
        dapr.io/wait-max-seconds: "30"
    spec:
      serviceAccountName: dapr-iot-processor
      containers:
        - name: processor
          image: {{ .Values.ecrBase }}/dapr-iot-processor:{{ .Values.processor.image.tag }}
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

- [ ] **Step 2: Commit**

```bash
git add helm/templates/processor.yaml
git commit -m "feat: add processor Helm template"
```

---

### Task 4: Lint and validate the chart

**Files:** none (validation only)

- [ ] **Step 1: Run `helm lint`**

```bash
helm lint helm/
```

Expected: `1 chart(s) linted, 0 chart(s) failed`

- [ ] **Step 2: Run `helm template` with realistic values to verify rendered output**

```bash
helm template dapr-iot helm/ \
  --set ecrBase=473741442322.dkr.ecr.us-east-1.amazonaws.com \
  --set ingestor.roleArn=arn:aws:iam::473741442322:role/dapr-iot-ingestor-role \
  --set processor.roleArn=arn:aws:iam::473741442322:role/dapr-iot-processor-role
```

Expected: valid YAML output showing both ServiceAccounts, Deployments, and Services with correct image URIs and role ARNs substituted. Verify:
- `image:` fields contain the full ECR path with `:latest` tag
- `eks.amazonaws.com/role-arn` annotations contain the correct ARNs
- Ingestor Service type is `LoadBalancer`, Processor is `ClusterIP`

---

### Task 5: Remove old k8s deployment files

**Files:**
- Delete: `k8s/ingestor-deployment.yaml`
- Delete: `k8s/processor-deployment.yaml`

- [ ] **Step 1: Delete the old manifests**

```bash
git rm k8s/ingestor-deployment.yaml k8s/processor-deployment.yaml
```

- [ ] **Step 2: Commit**

```bash
git commit -m "chore: remove k8s deployment manifests superseded by Helm chart"
```

---

### Task 6: Update README deploy step

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Replace step 8 in README**

Find the current step 8 block:

```markdown
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
```

Replace with:

```markdown
### 8. Deploy the services

```bash
INGESTOR_ROLE_ARN=$(cd terraform && terraform output -raw ingestor_role_arn)
PROCESSOR_ROLE_ARN=$(cd terraform && terraform output -raw processor_role_arn)

helm upgrade --install dapr-iot ./helm \
  --set ecrBase=$ECR_BASE \
  --set ingestor.roleArn=$INGESTOR_ROLE_ARN \
  --set processor.roleArn=$PROCESSOR_ROLE_ARN \
  -n dapr-iot

kubectl rollout status deployment/dapr-iot-ingestor -n dapr-iot
kubectl rollout status deployment/dapr-iot-processor -n dapr-iot
```
```

- [ ] **Step 2: Commit and push**

```bash
git add README.md
git commit -m "docs: update deploy step to use Helm chart"
git push
```
