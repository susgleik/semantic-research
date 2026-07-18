// search.bicep — Azure AI Search (SKU Basic, búsqueda semántica incluida)

@description('Nombre base para nombrar los recursos')
param baseName string

@description('Región de Azure')
param location string

@description('Environment tag (dev, staging, prod)')
param environment string

resource searchService 'Microsoft.Search/searchServices@2023-11-01' = {
  name: '${baseName}-${environment}-search'
  location: location
  sku: {
    name: 'basic'
  }
  properties: {
    replicaCount:   1
    partitionCount: 1
    hostingMode:    'default'
    semanticSearch: 'free'  // búsqueda semántica habilitada sin costo extra en Basic
  }
}

// Nota: el índice vectorial con campo 'embedding' (dims=3072, text-embedding-3-large)
// debe crearse por separado via REST API o SDK — no es posible via Bicep/ARM.
// Usar: scripts/create-index.sh  o el portal de Azure AI Search.

output endpoint string = 'https://${searchService.name}.search.windows.net'
output adminKey string  = searchService.listAdminKeys().primaryKey
