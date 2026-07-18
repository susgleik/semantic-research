// blob.bicep — Azure Blob Storage con container 'docs'

@description('Nombre base para nombrar los recursos (mínimo 3 caracteres)')
@minLength(3)
param baseName string

@description('Región de Azure')
param location string

@description('Environment tag (dev, staging, prod)')
@minLength(1)
param env string

var storageAccountName = take(replace('${baseName}${env}stor', '-', ''), 24)

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource docsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'docs'
  properties: {
    publicAccess: 'None'
  }
}

output accountName string = storageAccount.name

@secure()
output connectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${az.environment().suffixes.storage}'
