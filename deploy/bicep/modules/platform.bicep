// Shared platform: identity, registry, observability, data services and secrets.

param applicationName string
param environmentName string
param location string
param tags object
param suffix string
param registryName string
param storageName string
param keyVaultName string
param sqlAdminLogin string

@secure()
param sqlAdminPassword string

@secure()
param ipHashPepper string

var sqlServerName = 'sql-${applicationName}-${environmentName}-${suffix}'
var sqlDatabaseName = 'ThreadlineComments'
var storageShareName = 'statefulservices'

// -------------------------------------------------------------------------------------
//  Identity
//
//  One user-assigned identity shared by every workload. It is what pulls images from the
//  registry, reads secrets from Key Vault and writes blobs — so the deployment contains no
//  storage keys, no registry password and no connection string with a secret in it, apart from
//  the SQL password that Azure SQL still requires.
// -------------------------------------------------------------------------------------
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${applicationName}-${environmentName}'
  location: location
  tags: tags
}

// -------------------------------------------------------------------------------------
//  Container registry
// -------------------------------------------------------------------------------------
resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: registryName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    // No admin user: the identity below pulls with RBAC instead, so there is no shared password
    // to leak or rotate.
    adminUserEnabled: false
  }
}

var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource registryPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, acrPullRoleId)
  scope: registry
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
  }
}

// -------------------------------------------------------------------------------------
//  Observability
// -------------------------------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${applicationName}-${environmentName}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    // 30 days is the free allowance. Longer retention is a real cost on a demo deployment.
    retentionInDays: 30
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${applicationName}-${environmentName}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// -------------------------------------------------------------------------------------
//  Azure SQL
// -------------------------------------------------------------------------------------
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Container Apps egress IPs are not fixed, so the usual "allow Azure services" rule is what makes
// this reachable. It is the right trade for a demo; a production deployment would use a private
// endpoint and drop public access entirely (docs/IMPROVEMENTS.md).
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: {
    // Serverless General Purpose. It auto-pauses when idle, which is what keeps a demo deployment
    // near-free between the moments someone is actually looking at it.
    name: 'GP_S_Gen5_1'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 34359738368
    autoPauseDelay: 60
    minCapacity: json('0.5')
    zoneRedundant: false
  }
}

// -------------------------------------------------------------------------------------
//  Storage: blobs for attachments, and a file share the stateful containers mount.
// -------------------------------------------------------------------------------------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource attachmentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'attachments'
  properties: {
    publicAccess: 'None'
  }
}

// The Data Protection key ring, which encrypts the auth cookie. It lives in its own container
// rather than beside the attachments, because the two have nothing in common but a storage
// account: one is user content the site serves, the other is the key material that makes a
// session readable by every API replica instead of just the one that issued it.
resource dataProtectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'data-protection'
  properties: {
    publicAccess: 'None'
  }
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource statefulShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: fileService
  name: storageShareName
  properties: {
    shareQuota: 64
  }
}

var blobContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource blobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, blobContributorRoleId)
  scope: storage
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobContributorRoleId)
  }
}

// -------------------------------------------------------------------------------------
//  Key Vault
// -------------------------------------------------------------------------------------
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenant().tenantId
    // RBAC rather than access policies: one authorisation model for the whole subscription.
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

resource ipPepperSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ip-hash-pepper'
  properties: {
    value: ipHashPepper
  }
}

var secretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource keyVaultAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, secretsUserRoleId)
  scope: keyVault
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsUserRoleId)
  }
}

// -------------------------------------------------------------------------------------
//  Container Apps environment
// -------------------------------------------------------------------------------------
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${applicationName}-${environmentName}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    zoneRedundant: false
  }
}

// Redis, RabbitMQ and Elasticsearch keep their data on this share, so a revision restart does not
// wipe the queue or the search index.
resource environmentStorage 'Microsoft.App/managedEnvironments/storages@2024-03-01' = {
  parent: containerAppsEnvironment
  name: 'statefulservices'
  properties: {
    azureFile: {
      accountName: storage.name
      accountKey: storage.listKeys().keys[0].value
      shareName: statefulShare.name
      accessMode: 'ReadWrite'
    }
  }
}

output identityId string = identity.id
output identityClientId string = identity.properties.clientId
output registryLoginServer string = registry.properties.loginServer
output registryId string = registry.id
output keyVaultName string = keyVault.name
output storageAccountName string = storage.name
output storageShareName string = environmentStorage.name
output containerAppsEnvironmentId string = containerAppsEnvironment.id
output containerAppsEnvironmentDomain string = containerAppsEnvironment.properties.defaultDomain
output applicationInsightsConnectionString string = applicationInsights.properties.ConnectionString

// The connection string is composed by the consumer from these two plus the credentials it already
// holds. Emitting the assembled string here would put the SQL password into the deployment's
// outputs, where it is readable by anyone with reader access to the deployment history.
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
