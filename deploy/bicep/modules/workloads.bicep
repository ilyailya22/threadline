// The running workloads: three stateful backing services and three application containers.

param applicationName string
param environmentName string
param location string
param tags object
param imageTag string
param containerAppsEnvironmentId string
param registryLoginServer string
param registryId string
param identityId string
param identityClientId string
param keyVaultName string
param storageAccountName string
param storageShareName string
param sqlServerFqdn string
param sqlDatabaseName string
param sqlAdminLogin string

@secure()
param sqlAdminPassword string

@secure()
param rabbitPassword string

@secure()
param applicationInsightsConnectionString string

// Container Apps resolve each other by app name inside the environment.
var redisHost = '${applicationName}-${environmentName}-redis'
var rabbitHost = '${applicationName}-${environmentName}-rabbitmq'
var elasticHost = '${applicationName}-${environmentName}-elasticsearch'

var rabbitUser = 'threadline'

var sqlConnectionString = 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Max Pool Size=200'
var rabbitConnectionString = 'amqp://${rabbitUser}:${rabbitPassword}@${rabbitHost}:5672/'

var commonIdentity = {
  type: 'UserAssigned'
  userAssignedIdentities: {
    '${identityId}': {}
  }
}

var registryConfiguration = [
  {
    server: registryLoginServer
    identity: identityId
  }
]

// -------------------------------------------------------------------------------------
//  Backing services
//
//  Each is a single replica with a volume on the shared Azure Files mount and internal-only
//  ingress, so nothing but the application can reach them. Single replica is correct here: these
//  are not clustered deployments and running two of any of them without proper clustering would be
//  worse than running one.
// -------------------------------------------------------------------------------------
resource redis 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${applicationName}-${environmentName}-redis'
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: false
        targetPort: 6379
        transport: 'tcp'
        exposedPort: 6379
      }
    }
    template: {
      containers: [
        {
          name: 'redis'
          image: 'docker.io/library/redis:7-alpine'
          command: ['redis-server']
          args: ['--appendonly', 'yes', '--dir', '/data', '--maxmemory', '512mb', '--maxmemory-policy', 'allkeys-lru']
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          volumeMounts: [
            {
              volumeName: 'state'
              mountPath: '/data'
              subPath: 'redis'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
      volumes: [
        {
          name: 'state'
          storageType: 'AzureFile'
          storageName: storageShareName
        }
      ]
    }
  }
}

resource rabbitmq 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${applicationName}-${environmentName}-rabbitmq'
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: false
        targetPort: 5672
        transport: 'tcp'
        exposedPort: 5672
      }
      secrets: [
        { name: 'rabbit-password', value: rabbitPassword }
      ]
    }
    template: {
      containers: [
        {
          name: 'rabbitmq'
          image: 'docker.io/library/rabbitmq:3-management-alpine'
          env: [
            // The stock guest/guest account only works from localhost and is a liability anywhere
            // else, so the broker gets a real credential even though it has no external ingress.
            { name: 'RABBITMQ_DEFAULT_USER', value: rabbitUser }
            { name: 'RABBITMQ_DEFAULT_PASS', secretRef: 'rabbit-password' }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          volumeMounts: [
            {
              volumeName: 'state'
              mountPath: '/var/lib/rabbitmq'
              subPath: 'rabbitmq'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
      volumes: [
        {
          name: 'state'
          storageType: 'AzureFile'
          storageName: storageShareName
        }
      ]
    }
  }
}

resource elasticsearch 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${applicationName}-${environmentName}-elasticsearch'
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: false
        targetPort: 9200
        transport: 'http'
      }
    }
    template: {
      containers: [
        {
          name: 'elasticsearch'
          image: 'docker.elastic.co/elasticsearch/elasticsearch:8.15.3'
          env: [
            { name: 'discovery.type', value: 'single-node' }
            { name: 'xpack.security.enabled', value: 'false' }
            { name: 'ES_JAVA_OPTS', value: '-Xms1g -Xmx1g' }
            { name: 'cluster.routing.allocation.disk.threshold_enabled', value: 'false' }
          ]
          resources: {
            // The JVM heap above is 1 GiB, so the container needs headroom beyond it — sizing them
            // equally is the classic way to get a container OOM-killed under load.
            cpu: json('1.0')
            memory: '2Gi'
          }
          volumeMounts: [
            {
              volumeName: 'state'
              mountPath: '/usr/share/elasticsearch/data'
              subPath: 'elasticsearch'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
      volumes: [
        {
          name: 'state'
          storageType: 'AzureFile'
          storageName: storageShareName
        }
      ]
    }
  }
}

var sharedEnvironment = [
  { name: 'ConnectionStrings__Redis', value: '${redisHost}:6379' }
  { name: 'ConnectionStrings__RabbitMq', secretRef: 'rabbitmq-connection-string' }
  { name: 'Elasticsearch__Url', value: 'http://${elasticHost}' }
  { name: 'Storage__AccountUrl', value: 'https://${storageAccountName}.blob.${az.environment().suffixes.storage}' }
  { name: 'Storage__ContainerName', value: 'attachments' }
  { name: 'AZURE_CLIENT_ID', value: identityClientId }
  { name: 'KeyVault__Name', value: keyVaultName }
]

// -------------------------------------------------------------------------------------
//  API
// -------------------------------------------------------------------------------------
resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${applicationName}-${environmentName}-api'
  location: location
  tags: tags
  identity: commonIdentity
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        // SignalR needs a client to keep talking to the replica that owns its connection.
        stickySessions: {
          affinity: 'sticky'
        }
      }
      registries: registryConfiguration
      secrets: [
        { name: 'sql-connection-string', value: sqlConnectionString }
        { name: 'rabbitmq-connection-string', value: rabbitConnectionString }
        { name: 'appinsights-connection-string', value: applicationInsightsConnectionString }
        {
          name: 'ip-hash-pepper'
          keyVaultUrl: 'https://${keyVaultName}${az.environment().suffixes.keyvaultDns}/secrets/ip-hash-pepper'
          identity: identityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${registryLoginServer}/comments-api:${imageTag}'
          env: concat(sharedEnvironment, [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ConnectionStrings__SqlServer', secretRef: 'sql-connection-string' }
            { name: 'ApplicationInsights__ConnectionString', secretRef: 'appinsights-connection-string' }
            { name: 'Privacy__IpHashPepper', secretRef: 'ip-hash-pepper' }
          ])
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              initialDelaySeconds: 20
              periodSeconds: 15
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 10
              failureThreshold: 6
            }
          ]
        }
      ]
      scale: {
        // Two replicas at rest so a rolling revision never drops to zero capacity.
        minReplicas: 2
        maxReplicas: 10
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '80'
              }
            }
          }
        ]
      }
    }
  }
}

// -------------------------------------------------------------------------------------
//  Worker
//
//  Scaled on queue depth rather than CPU. The whole point of moving indexing and image processing
//  off the request path is that a backlog is absorbed by adding consumers — and queue length is
//  what actually measures the backlog. CPU would only react after the damage is done.
// -------------------------------------------------------------------------------------
resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${applicationName}-${environmentName}-worker'
  location: location
  tags: tags
  identity: commonIdentity
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registryConfiguration
      secrets: [
        { name: 'sql-connection-string', value: sqlConnectionString }
        { name: 'rabbitmq-connection-string', value: rabbitConnectionString }
        {
          name: 'ip-hash-pepper'
          keyVaultUrl: 'https://${keyVaultName}${az.environment().suffixes.keyvaultDns}/secrets/ip-hash-pepper'
          identity: identityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: '${registryLoginServer}/comments-worker:${imageTag}'
          env: concat(sharedEnvironment, [
            { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
            { name: 'ConnectionStrings__SqlServer', secretRef: 'sql-connection-string' }
            { name: 'Privacy__IpHashPepper', secretRef: 'ip-hash-pepper' }
          ])
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 8
        rules: [
          {
            name: 'rabbitmq-queue-depth'
            custom: {
              type: 'rabbitmq'
              metadata: {
                protocol: 'amqp'
                queueName: 'comment-created-integration-event'
                mode: 'QueueLength'
                value: '50'
              }
              auth: [
                {
                  secretRef: 'rabbitmq-connection-string'
                  triggerParameter: 'host'
                }
              ]
            }
          }
        ]
      }
    }
  }
}

// -------------------------------------------------------------------------------------
//  Web (nginx + the Angular bundle)
//
//  The only container with external ingress. It proxies /api and /hubs to the API, so the browser
//  makes same-origin requests and the API is never exposed directly.
// -------------------------------------------------------------------------------------
resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${applicationName}-${environmentName}-web'
  location: location
  tags: tags
  identity: commonIdentity
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        stickySessions: {
          affinity: 'sticky'
        }
      }
      registries: registryConfiguration
    }
    template: {
      containers: [
        {
          name: 'web'
          image: '${registryLoginServer}/comments-web:${imageTag}'
          env: [
            // Empty: nginx proxies the API on the same origin, so the SPA needs no absolute URL.
            { name: 'API_BASE_URL', value: '' }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '200'
              }
            }
          }
        ]
      }
    }
  }
  dependsOn: [
    api
  ]
}

output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output webUrl string = 'https://${web.properties.configuration.ingress.fqdn}'
output redisId string = redis.id
output rabbitmqId string = rabbitmq.id
output elasticsearchId string = elasticsearch.id
output registryIdOut string = registryId
