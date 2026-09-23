// The running workloads: three stateful backing services and three application containers.

param applicationName string
param environmentName string
param location string
param tags object
param imageTag string
param containerAppsEnvironmentId string

// The environment default domain. Lets one app name another without referencing it, which Bicep
// would otherwise call a cycle.
param containerAppsEnvironmentDomain string
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

@description('Google OAuth client id. Empty hides the "Continue with Google" button.')
param googleClientId string = ''

@secure()
@description('Google OAuth client secret.')
param googleClientSecret string = ''

@description('SMTP host for confirmation e-mail. Empty writes the message to the log instead.')
param smtpHost string = ''

param smtpPort int = 587

param smtpUsername string = ''

@secure()
param smtpPassword string = ''

@description('Address confirmation e-mail is sent from.')
param emailFromAddress string = 'no-reply@example.com'

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

            // Erlang derives the node name from the hostname, and a Container Apps replica hostname
            // does not resolve to itself — the broker then dies in prelaunch on
            // rabbit_prelaunch_dist:duplicate_node_check. A fixed short node name avoids the lookup
            // entirely, which is safe here because this is a single, non-clustered broker.
            { name: 'RABBITMQ_NODENAME', value: 'rabbit@localhost' }
            { name: 'RABBITMQ_USE_LONGNAME', value: 'false' }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          // Deliberately ephemeral. Erlang keeps its auth cookie in /var/lib/rabbitmq and refuses to
          // start unless the file is owned by the broker and mode 400, which an SMB (Azure Files)
          // mount cannot express — the node then dies in erl_distribution:start_link. Losing the
          // broker's state on a restart is acceptable here: SQL plus the outbox is the source of
          // truth, MassTransit recreates its topology on connect, and every consumer is idempotent.
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
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
          image: 'docker.elastic.co/elasticsearch/elasticsearch:9.1.5'
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

// Accounts sign-in and confirmation e-mail are both optional, and "optional" has to mean absent
// rather than empty: Container Apps rejects a secret declared without a value, and an env var
// pointing at a secret that was not declared is just as invalid. So when a credential is not
// supplied, neither the secret nor the setting that reads it is emitted at all.
var googleConfigured = !empty(googleClientId) && !empty(googleClientSecret)
var smtpConfigured = !empty(smtpHost)

var optionalSecrets = concat(
  googleConfigured ? [ { name: 'google-client-secret', value: googleClientSecret } ] : [],
  smtpConfigured && !empty(smtpPassword) ? [ { name: 'smtp-password', value: smtpPassword } ] : [])

var googleEnvironment = googleConfigured ? [
  { name: 'Authentication__Google__ClientId', value: googleClientId }
  { name: 'Authentication__Google__ClientSecret', secretRef: 'google-client-secret' }
] : []

var emailEnvironment = concat(
  smtpConfigured ? [
    { name: 'Email__Host', value: smtpHost }
    { name: 'Email__Port', value: string(smtpPort) }
    { name: 'Email__UseStartTls', value: 'true' }
    { name: 'Email__Username', value: smtpUsername }
  ] : [],
  smtpConfigured && !empty(smtpPassword) ? [ { name: 'Email__Password', secretRef: 'smtp-password' } ] : [],
  empty(emailFromAddress) ? [] : [ { name: 'Email__FromAddress', value: emailFromAddress } ])

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
      secrets: concat([
        { name: 'sql-connection-string', value: sqlConnectionString }
        { name: 'rabbitmq-connection-string', value: rabbitConnectionString }
        { name: 'appinsights-connection-string', value: applicationInsightsConnectionString }
        {
          name: 'ip-hash-pepper'
          keyVaultUrl: 'https://${keyVaultName}${az.environment().suffixes.keyvaultDns}/secrets/ip-hash-pepper'
          identity: identityId
        }
      ], optionalSecrets)
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

            // Built from the environment's domain rather than from the web app, which would be a
            // cycle: nginx already names the API, and the API now names the site. Always set, as
            // the confirmation link is needed even when the message only reaches the log.
            { name: 'Email__PublicUrl', value: 'https://${applicationName}-${environmentName}-web.${containerAppsEnvironmentDomain}' }
          // Accounts. Both are optional: without Google credentials the button is not shown, and
          // without an SMTP host the confirmation message goes to the log rather than failing a
          // registration that otherwise worked.
          ], googleEnvironment, emailEnvironment)
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
            // The internal FQDN over https, not the short name over http: nginx resolves upstreams
            // itself and ignores the search domains in /etc/resolv.conf, so a short name is "Host not
            // found"; the internal ingress terminates TLS and answers plain http with 426.
            { name: 'API_UPSTREAM', value: 'https://${api.properties.configuration.ingress.fqdn}' }
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
