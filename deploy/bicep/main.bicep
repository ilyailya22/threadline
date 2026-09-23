// =====================================================================================
//  Threadline Comments — Azure infrastructure
// =====================================================================================
//
//  Deploys the whole system to Azure Container Apps:
//
//      az deployment sub create \
//        --location westeurope \
//        --template-file deploy/bicep/main.bicep \
//        --parameters deploy/bicep/main.parameters.json
//
//  See docs/DEPLOYMENT.md for the full walkthrough, including the Azure Free Trial path.
//
//  SHAPE OF THE DEPLOYMENT
//  -----------------------
//  Managed where Azure's managed service is worth paying for (SQL, Blob, Key Vault, Monitor);
//  self-hosted as container apps where it is not. Redis, RabbitMQ and Elasticsearch each run as a
//  single-replica container app with a persistent volume. That is a deliberate trade for a
//  demonstration deployment: Azure Cache for Redis has no free tier, there is no managed RabbitMQ
//  at all, and Elastic Cloud starts well above what this workload needs. The application talks to
//  all three over standard protocols, so swapping any of them for a managed equivalent is a
//  connection-string change — see docs/IMPROVEMENTS.md.

targetScope = 'subscription'

@description('Short name used as a prefix for every resource.')
@minLength(3)
@maxLength(12)
param applicationName string = 'threadline'

@description('Environment discriminator: dev, test or prod.')
@allowed(['dev', 'test', 'prod'])
param environmentName string = 'dev'

@description('Azure region for every resource.')
param location string = 'canadacentral'

@description('Administrator login for Azure SQL.')
param sqlAdminLogin string = 'tladmin'

@description('Administrator password for Azure SQL. Supply at deploy time; never commit it.')
@secure()
param sqlAdminPassword string

@description('Pepper used to hash client IP addresses. Generate with: openssl rand -base64 32')
@secure()
param ipHashPepper string

@description('Password for the RabbitMQ broker account. Generate with: openssl rand -base64 24')
@secure()
param rabbitPassword string

@description('Container image tag to deploy, normally the commit SHA.')
param imageTag string = 'latest'

@description('Tags applied to every resource.')
param tags object = {
  application: 'threadline-comments'
  environment: environmentName
  managedBy: 'bicep'
}

// -------------------------------------------------------------------------------------
//  Naming. One place, so every resource is predictable and nothing collides globally.
// -------------------------------------------------------------------------------------
var suffix = uniqueString(subscription().subscriptionId, applicationName, environmentName)
var resourceGroupName = 'rg-${applicationName}-${environmentName}'
var registryName = take(replace('cr${applicationName}${environmentName}${suffix}', '-', ''), 50)
var storageName = take(replace('st${applicationName}${environmentName}${suffix}', '-', ''), 24)
var keyVaultName = take('kv-${applicationName}-${environmentName}-${suffix}', 24)

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module platform 'modules/platform.bicep' = {
  name: 'platform'
  scope: resourceGroup
  params: {
    applicationName: applicationName
    environmentName: environmentName
    location: location
    tags: tags
    suffix: suffix
    registryName: registryName
    storageName: storageName
    keyVaultName: keyVaultName
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPassword: sqlAdminPassword
    ipHashPepper: ipHashPepper
  }
}

module workloads 'modules/workloads.bicep' = {
  name: 'workloads'
  scope: resourceGroup
  params: {
    applicationName: applicationName
    environmentName: environmentName
    location: location
    tags: tags
    imageTag: imageTag
    containerAppsEnvironmentId: platform.outputs.containerAppsEnvironmentId
    containerAppsEnvironmentDomain: platform.outputs.containerAppsEnvironmentDomain
    registryLoginServer: platform.outputs.registryLoginServer
    registryId: platform.outputs.registryId
    identityId: platform.outputs.identityId
    identityClientId: platform.outputs.identityClientId
    keyVaultName: platform.outputs.keyVaultName
    storageAccountName: platform.outputs.storageAccountName
    storageShareName: platform.outputs.storageShareName
    sqlServerFqdn: platform.outputs.sqlServerFqdn
    sqlDatabaseName: platform.outputs.sqlDatabaseName
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPassword: sqlAdminPassword
    rabbitPassword: rabbitPassword
    applicationInsightsConnectionString: platform.outputs.applicationInsightsConnectionString
  }
}

output resourceGroupName string = resourceGroup.name
output registryLoginServer string = platform.outputs.registryLoginServer
output webUrl string = workloads.outputs.webUrl
output apiUrl string = workloads.outputs.apiUrl
