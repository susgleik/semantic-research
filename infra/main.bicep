// main.bicep — orquestador principal de infraestructura
// Despliega: Blob Storage, Azure AI Search, Azure OpenAI, Container App

targetScope = 'resourceGroup'

@description('Nombre base para todos los recursos')
param baseName string = 'semantic-search'

@description('Región de Azure para los recursos')
param location string = resourceGroup().location

@description('Environment tag (dev, staging, prod)')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'dev'

// TODO: agregar módulos por componente
// module blob 'blob.bicep' = { ... }
// module search 'search.bicep' = { ... }
// module openai 'openai.bicep' = { ... }
// module containerApp 'container-app.bicep' = { ... }

output resourceGroupName string = resourceGroup().name
