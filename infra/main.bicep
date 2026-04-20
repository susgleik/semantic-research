// main.bicep — orquestador principal de infraestructura
// Despliega: Blob Storage, Azure AI Search, Container App + Key Vault
// Nota: usa OpenAI directo (api.openai.com), no Azure OpenAI.

targetScope = 'resourceGroup'

@description('Nombre base para todos los recursos')
param baseName string = 'semantic-search'

@description('Región de Azure para los recursos')
param location string = resourceGroup().location

@description('Environment tag (dev, staging, prod)')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'dev'

@description('Nombre del Azure Container Registry (sin .azurecr.io)')
param acrName string

@description('Tag de la imagen del Container App a desplegar')
param imageTag string = 'latest'

@description('Tenant ID de Azure AD')
param tenantId string

@description('Client ID del App Registration')
param clientId string

// Secrets — se pasan via CLI en deploy, NUNCA en params.json
// az deployment group create ... --parameters openAiApiKey=... searchApiKey=...
// openAiApiKey: API Key de api.openai.com (sk-...)

@secure()
param openAiApiKey string

@secure()
param searchApiKey string

@secure()
param blobConnectionString string = ''  // se usa el output de blob si está vacío

// ── Módulos ───────────────────────────────────────────────────────────────────

module blob 'blob.bicep' = {
  name: 'blob'
  params: {
    baseName:    baseName
    location:    location
    env:         environment
  }
}

module search 'search.bicep' = {
  name: 'search'
  params: {
    baseName:    baseName
    location:    location
    environment: environment
  }
}

module containerApp 'container-app.bicep' = {
  name: 'containerApp'
  params: {
    baseName:             baseName
    location:             location
    environment:          environment
    acrName:              acrName
    imageTag:             imageTag
    openAiApiKey:         openAiApiKey
    searchEndpoint:       search.outputs.endpoint
    searchApiKey:         empty(searchApiKey) ? search.outputs.adminKey : searchApiKey
    blobConnectionString: empty(blobConnectionString) ? blob.outputs.connectionString : blobConnectionString
    tenantId:             tenantId
    clientId:             clientId
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output resourceGroupName string = resourceGroup().name
output blobAccountName   string = blob.outputs.accountName
output searchEndpoint    string = search.outputs.endpoint
output containerAppUrl   string = 'https://${containerApp.outputs.containerAppFqdn}'
output keyVaultName      string = containerApp.outputs.keyVaultName

// Nota: el índice vectorial de AI Search (campo 'embedding', dims=3072)
// debe crearse manualmente después del deploy. Ver docs/azure-setup.md.
