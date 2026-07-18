// container-app.bicep — Container App (API) + Key Vault + Log Analytics

@description('Nombre base para nombrar los recursos')
param baseName string

@description('Región de Azure')
param location string

@description('Environment tag (dev, staging, prod)')
param environment string

@description('Nombre del Azure Container Registry (sin .azurecr.io)')
param acrName string

@description('Tag de la imagen a desplegar')
param imageTag string = 'latest'

@description('API Key de OpenAI (api.openai.com)')
@secure()
param openAiApiKey string

@description('Deployment de embeddings')
param embeddingDeployment string = 'text-embedding-3-large'

@description('Deployment de chat')
param chatDeployment string = 'gpt-4o'

@description('URL del endpoint de Azure AI Search')
param searchEndpoint string

@description('API Key de Azure AI Search')
@secure()
param searchApiKey string

@description('Nombre del índice de AI Search')
param searchIndexName string = 'documents'

@description('Connection string de Azure Blob Storage')
@secure()
param blobConnectionString string

@description('Nombre del container de Blob Storage')
param blobContainer string = 'docs'

@description('Tenant ID de Azure AD para auth JWT')
param tenantId string

@description('Client ID del App Registration')
param clientId string

// ── Log Analytics ─────────────────────────────────────────────────────────────

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${baseName}-${environment}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ── Container Apps Environment ────────────────────────────────────────────────

resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${baseName}-${environment}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey:  logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// ── Key Vault ─────────────────────────────────────────────────────────────────

var kvName = take(replace('${baseName}${environment}kv', '-', ''), 24)

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  properties: {
    sku: {
      family: 'A'
      name:   'standard'
    }
    tenantId:               subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete:        true
    softDeleteRetentionInDays: 7
  }
}

resource kvSecretOpenAiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'openai-api-key'
  properties: { value: openAiApiKey }
}

resource kvSecretSearchKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'search-api-key'
  properties: { value: searchApiKey }
}

resource kvSecretBlobConn 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'blob-connection-string'
  properties: { value: blobConnectionString }
}

// ── Container App ─────────────────────────────────────────────────────────────

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${baseName}-${environment}-api'
  location: location
  identity: {
    type: 'SystemAssigned'  // managed identity para acceder al ACR y Key Vault
  }
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      ingress: {
        external:    true
        targetPort:  8080
        transport:   'http'
      }
      registries: [
        {
          server:   '${acrName}.azurecr.io'
          identity: 'system'  // usa managed identity para pull del ACR
        }
      ]
      secrets: [
        { name: 'openai-key',    value: openAiApiKey }
        { name: 'search-key',    value: searchApiKey }
        { name: 'blob-conn-str', value: blobConnectionString }
      ]
    }
    template: {
      containers: [
        {
          name:  'api'
          image: '${acrName}.azurecr.io/semantic-search-api:${imageTag}'
          resources: {
            cpu:    json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT',        value: 'Production' }
            { name: 'OpenAI__EmbeddingDeployment',   value: embeddingDeployment }
            { name: 'OpenAI__ChatDeployment',        value: chatDeployment }
            { name: 'AzureSearch__Endpoint',         value: searchEndpoint }
            { name: 'AzureSearch__IndexName',        value: searchIndexName }
            { name: 'AzureBlob__Container',          value: blobContainer }
            { name: 'AzureAd__TenantId',             value: tenantId }
            { name: 'AzureAd__ClientId',             value: clientId }
            { name: 'AzureAd__Audience',             value: 'api://${clientId}' }
            // secrets referenciados por nombre — no se exponen en texto plano
            { name: 'OpenAI__ApiKey',               secretRef: 'openai-key' }
            { name: 'AzureSearch__ApiKey',          secretRef: 'search-key' }
            { name: 'AzureBlob__ConnectionString',  secretRef: 'blob-conn-str' }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '20'  // escala cuando hay >20 requests simultáneos por réplica
              }
            }
          }
        ]
      }
    }
  }
}

// Dar al Container App rol "Key Vault Secrets User" via RBAC
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
resource kvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name:  guid(keyVault.id, containerApp.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId:      containerApp.identity.principalId
    principalType:    'ServicePrincipal'
  }
}

output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output keyVaultName      string = keyVault.name
output containerAppId    string = containerApp.id
