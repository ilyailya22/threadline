# Deploying to Azure

Everything needed to put the system on Azure is in the repository: Bicep templates in
[`deploy/bicep`](../deploy/bicep) and a GitHub Actions pipeline in
[`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml). The template is validated with
`az bicep build`.

> **Status.** The deployment was not executed during development because no Azure subscription was
> available. The steps below are the exact ones to run; the recommended path is an **Azure Free
> Trial** (USD 200 credit for 30 days), which covers this deployment comfortably — see the cost
> table.

---

## What gets deployed

```
Resource group  rg-threadline-dev
├── Container Apps environment  (Log Analytics attached)
│   ├── threadline-dev-web            nginx + Angular      external HTTPS ingress — the only public endpoint
│   ├── threadline-dev-api            ASP.NET Core API     internal, 2–10 replicas, HTTP-concurrency scaling
│   ├── threadline-dev-worker         outbox + consumers   1–8 replicas, RabbitMQ queue-depth scaling
│   ├── threadline-dev-redis          redis:7              internal, 1 replica, Azure Files volume
│   ├── threadline-dev-rabbitmq       rabbitmq:3           internal, 1 replica, Azure Files volume
│   └── threadline-dev-elasticsearch  elasticsearch:9.1    internal, 1 replica, Azure Files volume
├── Azure SQL Database              serverless GP_S_Gen5_1, auto-pause after 60 min
├── Storage account                 blob container "attachments" + file share for stateful apps
├── Key Vault                       RBAC mode, holds the IP-hash pepper
├── Container Registry              Basic, admin user disabled
├── Application Insights            workspace-based
└── User-assigned managed identity  AcrPull, Storage Blob Data Contributor, Key Vault Secrets User
```

Redis, RabbitMQ and Elasticsearch run as container apps because none of them has a free managed
tier on Azure (Azure Cache for Redis starts around USD 16/month; there is no managed RabbitMQ at
all). The application reaches all three over standard protocols, so moving any of them to a managed
service later is a configuration change.

---

## Path A — from your machine (quickest)

### 1. Prerequisites

- Azure CLI 2.60+ (`az version`)
- Docker
- An Azure subscription — a [Free Trial](https://azure.microsoft.com/free/) works

```bash
az login
az account set --subscription "<subscription id>"
```

### 2. Secrets

Generate them once and keep them somewhere safe. None of them is ever committed.

```bash
export SQL_PASSWORD="$(openssl rand -base64 24)Aa1!"
export IP_PEPPER="$(openssl rand -base64 32)"
export RABBIT_PASSWORD="$(openssl rand -base64 24 | tr -d '/+=')"
```

### 3. Infrastructure

```bash
az deployment sub create \
  --name threadline-initial \
  --location westeurope \
  --template-file deploy/bicep/main.bicep \
  --parameters applicationName=threadline environmentName=dev \
               sqlAdminPassword="$SQL_PASSWORD" \
               ipHashPepper="$IP_PEPPER" \
               rabbitPassword="$RABBIT_PASSWORD"
```

The first run creates the container apps pointing at an image tag that does not exist yet, so they
will not start until step 4. That is expected.

### 4. Images

```bash
REGISTRY=$(az deployment sub show --name threadline-initial \
  --query properties.outputs.registryLoginServer.value -o tsv)

az acr login --name "${REGISTRY%%.*}"

for app in api worker web; do
  case $app in
    api)    file=src/Comments.Api/Dockerfile ;;
    worker) file=src/Comments.Worker/Dockerfile ;;
    web)    file=src/Comments.Web/Dockerfile ;;
  esac
  docker build -f "$file" -t "$REGISTRY/comments-$app:latest" .
  docker push "$REGISTRY/comments-$app:latest"
done
```

### 5. Roll out

```bash
for app in api worker web; do
  az containerapp update \
    --name "threadline-dev-$app" \
    --resource-group rg-threadline-dev \
    --image "$REGISTRY/comments-$app:latest"
done
```

### 6. Open it

```bash
az deployment sub show --name threadline-initial --query properties.outputs.webUrl.value -o tsv
```

The first request may take a few seconds while the serverless database resumes.

---

## Path B — GitHub Actions (repeatable)

The pipeline authenticates with **OIDC federated credentials**: GitHub exchanges a short-lived token
for an Azure one at run time, so no client secret exists anywhere.

### One-time setup

```bash
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)

APP_ID=$(az ad app create --display-name threadline-github --query appId -o tsv)
az ad sp create --id "$APP_ID"

# Contributor to create resources, User Access Administrator to create the role assignments.
az role assignment create --assignee "$APP_ID" --role Contributor \
  --scope "/subscriptions/$SUBSCRIPTION_ID"
az role assignment create --assignee "$APP_ID" --role "User Access Administrator" \
  --scope "/subscriptions/$SUBSCRIPTION_ID"

az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<owner>/<repo>:environment:dev",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

In the GitHub repository, create an environment named `dev` with these secrets:

| Secret | Value |
|---|---|
| `AZURE_CLIENT_ID` | `$APP_ID` |
| `AZURE_TENANT_ID` | `$TENANT_ID` |
| `AZURE_SUBSCRIPTION_ID` | `$SUBSCRIPTION_ID` |
| `SQL_ADMIN_PASSWORD` | generated above |
| `IP_HASH_PEPPER` | generated above |
| `RABBIT_PASSWORD` | generated above |

### Deploy

Push to `main`, or run **Deploy to Azure** manually from the Actions tab. The workflow deploys the
infrastructure, builds and pushes the three images tagged with the commit SHA, rolls them out, and
**fails the run if `/health/ready` does not come up within five minutes** — a deployment that
reports success while the site is down is worse than one that fails.

---

## Cost

Approximate, West Europe, pay-as-you-go, for an environment left running continuously.

| Resource | Configuration | ≈ USD / month |
|---|---|---|
| Container Apps — API | 2 × 0.5 vCPU / 1 GiB, always on | 35 |
| Container Apps — worker | 1 × 0.5 vCPU / 1 GiB | 18 |
| Container Apps — web | 1 × 0.25 vCPU / 0.5 GiB | 9 |
| Container Apps — Redis, RabbitMQ | 2 × 0.5 vCPU / 1 GiB | 36 |
| Container Apps — Elasticsearch | 1 × 1 vCPU / 2 GiB | 36 |
| Azure SQL serverless | auto-pauses when idle | 5–40 |
| Storage, Key Vault, ACR Basic | | 7 |
| Log Analytics / App Insights | within the free 5 GB | 0 |
| **Total** | | **≈ 150–180** |

A 30-day Free Trial credit of USD 200 covers a month of this. To stretch it, scale the API to one
replica and let the database pause:

```bash
az containerapp update -n threadline-dev-api -g rg-threadline-dev --min-replicas 1
```

---

## Teardown

Everything lives in one resource group, so one command removes it all and nothing keeps billing:

```bash
az group delete --name rg-threadline-dev --yes --no-wait
```

Key Vault is soft-deleted for seven days. To reuse the same name immediately:

```bash
az keyvault purge --name <vault name>
```

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Container app stuck "Activating" | image tag does not exist yet | run step 4, then step 5 |
| API readiness failing, logs show SQL login errors | firewall or password | check `AllowAllWindowsAzureIps` rule; re-deploy with the right password |
| First request slow | serverless SQL resuming | expected; lasts a few seconds |
| Elasticsearch restarts | too little memory | keep ≥ 2 GiB; the heap is 1 GiB and needs headroom |
| 502 from the web app | API not ready yet | nginx re-resolves the API every 10 s; wait for `/health/ready` |
