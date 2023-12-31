param location string

resource storage 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
}

resource exampleExistingStorage 'Microsoft.Storage/storageAccounts@2021-02-01' existing = {
  name: 'existingStorageName'
}
