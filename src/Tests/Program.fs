﻿open System
open System.Text
open Converter.PulumiTypes
open Expecto
open Converter
open Converter.BicepParser

let parameterParsingTests = testList "Parsing" [
    test "basic string parameter" {
        let program = BicepParser.parseOrFail "param storageName string"
        let param = BicepProgram.findParameter "storageName" program
        Expect.equal param.name "storageName" "Name is correct"
        Expect.equal param.parameterType (Some "string") "Type is correct"
        Expect.equal param.defaultValue None "Default value is correct"
        Expect.equal param.decorators [ ] "There are no decorators"
        Expect.equal param.description None "There is no description"
    }

    test "basic string parameter with default value" {
        let program = BicepParser.parseOrFail "param storageName string = 'admin'"
        let param = BicepProgram.findParameter "storageName" program
        Expect.equal param.name "storageName" "Name is correct"
        Expect.equal param.parameterType (Some "string") "Type is correct"
        Expect.equal param.defaultValue (Some (BicepSyntax.String "admin")) "Default value is correct"
        Expect.equal param.decorators [ ] "There are no decorators"
        Expect.equal param.description None "There is no description"
    }

    test "basic int parameter with default value" {
        let program = BicepParser.parseOrFail "param count int = 42"
        let param = BicepProgram.findParameter "count" program
        Expect.equal param.name "count" "Name is correct"
        Expect.equal param.parameterType (Some "int") "Type is correct"
        Expect.equal param.defaultValue (Some (BicepSyntax.Integer 42)) "Default value is correct"
        Expect.equal param.decorators [ ] "There are no decorators"
        Expect.equal param.description None "There is no description"
    }
    
    test "basic bool parameter with default value" {
        let program = BicepParser.parseOrFail "param create bool = false"
        let param = BicepProgram.findParameter "create" program
        Expect.equal param.name "create" "Name is correct"
        Expect.equal param.parameterType (Some "bool") "Type is correct"
        Expect.equal param.defaultValue (Some (BicepSyntax.Boolean false)) "Default value is correct"
        Expect.equal param.decorators [ ] "There are no decorators"
        Expect.equal param.description None "There is no description"
    }
    
    test "description can be extracted from the parameter" {
        let program = parseOrFail """
@description('example description')
param storageAccount string
"""
        let param = BicepProgram.findParameter "storageAccount" program
        Expect.equal param.name "storageAccount" "Name is correct"
        Expect.equal param.parameterType (Some "string") "Type is correct"
        Expect.equal param.defaultValue None "Default value is correct"
        let expectedDecorators = [
            BicepSyntax.FunctionCall("description", [
                BicepSyntax.String "example description"
            ])
        ]
        
        Expect.equal param.decorators expectedDecorators "There are decorators"
        Expect.equal param.description (Some "example description") "description extracted correctly"
    }

    test "parameter can use current resource group location as default" {
        let program = parseOrFail "param location string = resourceGroup().location"
        let param = BicepProgram.findParameter "location" program
        Expect.equal param.name "location" "Name is correct"
        Expect.equal param.parameterType (Some "string") "Type is correct"
        let defaultValue = BicepSyntax.PropertyAccess(
            BicepSyntax.FunctionCall("resourceGroup", [ ]),
            "location")
        Expect.equal param.defaultValue (Some defaultValue) "Default value is correct"
    }
    
    test "parameter can use current resource group from variable location as default" {
        let program = parseOrFail """
var currentResourceGroup = resourceGroup()       
param location string = currentResourceGroup.location
"""
        let param = BicepProgram.findParameter "location" program
        Expect.equal param.name "location" "Name is correct"
        Expect.equal param.parameterType (Some "string") "Type is correct"
        let defaultValue = BicepSyntax.PropertyAccess(BicepSyntax.VariableAccess "currentResourceGroup", "location")
        Expect.equal param.defaultValue (Some defaultValue) "Default value is correct"
    }
    
    test "nested property access can be parsed" {
        let expr = parseExpression "first.second.third"
        let expected = BicepSyntax.PropertyAccess(
            BicepSyntax.PropertyAccess (BicepSyntax.VariableAccess "first", "second"),"third")
        Expect.equal expected expr "Nested property access is correctly parsed"
    }
   
    
    test "parsing basic resource works" {
        let program = parseOrFail """
param storageName string
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = {
  name: storageName
  location: 'eastus'
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
}
"""
        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        Expect.equal exampleStorage.name "exampleStorage" "Name is correct"
        Expect.equal exampleStorage.token "Microsoft.Storage/storageAccounts@2021-02-01" "Token is correct"
        let expectedResourceBody = BicepSyntax.Object (Map.ofList [
            BicepSyntax.Identifier "name", BicepSyntax.VariableAccess "storageName"
            BicepSyntax.Identifier "location", BicepSyntax.String "eastus"
            BicepSyntax.Identifier "kind", BicepSyntax.String "StorageV2"
            BicepSyntax.Identifier "sku", BicepSyntax.Object (Map.ofList [
                BicepSyntax.Identifier "name", BicepSyntax.String "Standard_LRS"
            ])
        ])

        Expect.equal exampleStorage.value expectedResourceBody "Resource body is correctly parsed"
    }
    
    test "parsing existing resource works" {
        let program = parseOrFail """
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' existing = {
  name: 'existingStorageName'
}
"""
        let exampleStorage = BicepProgram.findVariable "exampleStorage" program
        let expectedValue = BicepSyntax.FunctionCall("getExistingResource", [
            BicepSyntax.String "Microsoft.Storage/storageAccounts@2021-02-01"
            BicepSyntax.Object (Map.ofList [
                BicepSyntax.Identifier "name", BicepSyntax.String "existingStorageName"
            ])
        ])

        Expect.equal exampleStorage.value expectedValue "Value is transformed correctly"
    }
    
    test "existing resource can apply resource group scoping" {
        let program = parseOrFail """
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' existing = {
  name: 'existingStorageName'
}
"""
        
        let exampleStorage =
            program
            |> BicepProgram.reduceScopeParameter
            |> BicepProgram.findVariable "exampleStorage" 
        let expectedValue = BicepSyntax.FunctionCall("getExistingResource", [
            BicepSyntax.String "Microsoft.Storage/storageAccounts@2021-02-01"
            BicepSyntax.Object (Map.ofList [
                BicepSyntax.Identifier "name", BicepSyntax.String "existingStorageName"
                BicepSyntax.Identifier "resourceGroupName", BicepSyntax.PropertyAccess(
                    BicepSyntax.FunctionCall("resourceGroup", [  ]), "name")
            ])
        ])

        Expect.equal exampleStorage.value expectedValue "Value is transformed correctly"
    }
    
    test "existing resource can apply resource group scoping inside module" {
        let program = parseOrFail """
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' existing = {
  name: 'existingStorageName'
}
"""
        
        let exampleStorage =
            { program with programKind = ProgramKind.Module }
            |> BicepProgram.reduceScopeParameter
            |> BicepProgram.findVariable "exampleStorage"

        let expectedValue = BicepSyntax.FunctionCall("getExistingResource", [
            BicepSyntax.String "Microsoft.Storage/storageAccounts@2021-02-01"
            BicepSyntax.Object (Map.ofList [
                BicepSyntax.Identifier "name", BicepSyntax.String "existingStorageName"
                BicepSyntax.Identifier "resourceGroupName", BicepSyntax.VariableAccess "resourceGroupName"
            ])
        ])

        Expect.equal exampleStorage.value expectedValue "Value is transformed correctly"
    }  
    
    test "parsing conditional resource works" {
        let program = parseOrFail """
param storageName string
param createStorage bool = false
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = if (createStorage) {
  name: storageName
  location: 'eastus'
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
}
"""
        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        Expect.equal exampleStorage.name "exampleStorage" "Name is correct"
        Expect.equal exampleStorage.token "Microsoft.Storage/storageAccounts@2021-02-01" "Token is correct"

        let conditionalResourceBody = BicepSyntax.IfCondition (
            BicepSyntax.VariableAccess "createStorage",
            BicepSyntax.Object (Map.ofList [
                BicepSyntax.Identifier "name", BicepSyntax.VariableAccess "storageName"
                BicepSyntax.Identifier "location", BicepSyntax.String "eastus"
                BicepSyntax.Identifier "kind", BicepSyntax.String "StorageV2"
                BicepSyntax.Identifier "sku", BicepSyntax.Object (Map.ofList [
                    BicepSyntax.Identifier "name", BicepSyntax.String "Standard_LRS"
                ])
            ])
        )

        Expect.equal exampleStorage.value conditionalResourceBody "Resource body is correctly parsed"
    }
    
    test "parsing resource with for loop works" {
        let program = parseOrFail """
param networkSecurityGroupName string
param location string = resourceGroup().location
param orgNames array = ['Contoso', 'Fabrikam','Coho'] 

resource networkSecurityGroups 'Microsoft.Network/networkSecurityGroups@2020-06-01' = [for orgName in orgNames: {
  name: 'nsg-${orgName}'
  location: location
}]
"""

        let networkSecurityGroups = BicepProgram.findResource "networkSecurityGroups" program
        Expect.equal networkSecurityGroups.name "networkSecurityGroups" "Name is correct"

        let expectedForLoop = BicepSyntax.For {
            expression = BicepSyntax.VariableAccess "orgNames"
            variableSection = BicepSyntax.LocalVariable "orgName"
            body = BicepSyntax.Object(Map.ofList [
                BicepSyntax.Identifier "location", BicepSyntax.VariableAccess "location"
                BicepSyntax.Identifier "name", BicepSyntax.InterpolatedString (
                    [BicepSyntax.VariableAccess "orgName"], ["nsg-"; ""])
            ])
        }
        
        Expect.equal expectedForLoop networkSecurityGroups.value "The resource body is parsed correctly"
    }
    
    test "parsing resource with for loop using index works" {
        let program = parseOrFail """
param networkSecurityGroupName string
param location string = resourceGroup().location
param orgNames array = ['Contoso', 'Fabrikam','Coho']

resource networkSecurityGroups 'Microsoft.Network/networkSecurityGroups@2020-06-01' = [for (orgName, i) in orgNames: {
  name: 'nsg-${orgName}-${i}'
  location: location
}]
"""
        let networkSecurityGroups = BicepProgram.findResource "networkSecurityGroups" program
        Expect.equal networkSecurityGroups.name "networkSecurityGroups" "Name is correct"

        let expectedForLoop = BicepSyntax.For {
            expression = BicepSyntax.VariableAccess "orgNames"
            variableSection = BicepSyntax.LocalVariableBlock ["orgName"; "i"]
            body = BicepSyntax.Object(Map.ofList [
                BicepSyntax.Identifier "location", BicepSyntax.VariableAccess "location"
                BicepSyntax.Identifier "name", BicepSyntax.InterpolatedString (
                    [BicepSyntax.VariableAccess "orgName"; BicepSyntax.VariableAccess "i"], ["nsg-"; "-"; ""])
            ])
        }
        
        Expect.equal expectedForLoop networkSecurityGroups.value "The resource body is parsed correctly"
    }
    
    test "parsing nested resources works" {
        let program = parseOrFail """
resource storage 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  name: 'example-storage'
  location: 'west'
  
  resource service 'fileServices' = {
    name: 'default'

    resource share 'shares' = {
      name: 'exampleshare'
    }
  }
}
"""
        Expect.equal program.declarations.Length 3 "There are top-level declarations"
        let storage = BicepProgram.findResource "storage" program
        let expectedStorageValue = BicepSyntax.Object(Map.ofList [
            BicepSyntax.Identifier "name", BicepSyntax.String "example-storage"
            BicepSyntax.Identifier "location", BicepSyntax.String "west"
        ])
        Expect.equal storage.value expectedStorageValue "storage value is correct"
        
        let service = BicepProgram.findResource "service" program
        Expect.equal service.token "Microsoft.Storage/storageAccounts/fileServices@2022-09-01" "token is merged correctly"
        let expectedServiceValue = BicepSyntax.Object(Map.ofList [
            BicepSyntax.Identifier "name", BicepSyntax.String "default"
            BicepSyntax.Identifier "parent", BicepSyntax.VariableAccess "storage"
        ])
        Expect.equal service.value expectedServiceValue "service value is correct"

        let share = BicepProgram.findResource "share" program
        Expect.equal share.token "Microsoft.Storage/storageAccounts/fileServices/shares@2022-09-01" "token is merged correctly"
        let expectedShareValue = BicepSyntax.Object(Map.ofList [
            BicepSyntax.Identifier "name", BicepSyntax.String "exampleshare"
            BicepSyntax.Identifier "parent", BicepSyntax.VariableAccess "service"
        ])
        Expect.equal share.value expectedShareValue "share value is correct"
    }

    test "parameterizing bicep program by resource group works" {
        let program = parseOrFail """
param location string = resourceGroup().location
resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  location: resourceGroup().location
}

resource anotherStorageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
   location: location
   scope: resourceGroup()
}
"""
        let parameterizedProgram =
            program
            |> BicepProgram.reduceScopeParameter
            |> BicepProgram.parameterizeByResourceGroup

        Expect.equal parameterizedProgram.declarations.Length 5 "There are 5 declarations"
        
        let resourceGroupName = BicepProgram.findParameter "resourceGroupName" parameterizedProgram
        Expect.equal resourceGroupName.parameterType (Some "string") "parameter type is correct"
        
        let param = BicepProgram.findParameter "location" parameterizedProgram
        Expect.equal param.name "location" "Name is correct"
        Expect.equal param.parameterType (Some "string") "Type is correct"
        let defaultValue = BicepSyntax.PropertyAccess(BicepSyntax.VariableAccess "currentResourceGroup", "location")
        Expect.equal param.defaultValue (Some defaultValue) "Default value is correct"
        
        let storageAccount = BicepProgram.findResource "storageAccount" parameterizedProgram
        let expectedStorageAccountInputs = BicepSyntax.Object(Map.ofList [
            BicepSyntax.Identifier "location", BicepSyntax.PropertyAccess(
                BicepSyntax.VariableAccess "currentResourceGroup", "location")
            BicepSyntax.Identifier "resourceGroupName", BicepSyntax.PropertyAccess(
                BicepSyntax.VariableAccess "currentResourceGroup", "name")
        ])
        
        Expect.equal storageAccount.value expectedStorageAccountInputs "Inputs are transformed correctly"
        
        let anotherStorageAccount = BicepProgram.findResource "anotherStorageAccount" parameterizedProgram
        let expectedInputs = BicepSyntax.Object(Map.ofList [
            BicepSyntax.Identifier "location", BicepSyntax.VariableAccess "location"
            BicepSyntax.Identifier "resourceGroupName", BicepSyntax.PropertyAccess(
                BicepSyntax.VariableAccess "currentResourceGroup", "name")
        ])
        Expect.equal anotherStorageAccount.value expectedInputs "Inputs are transformed correctly"
    }
    
    test "parsing module declaration should work" {
        let program = parseOrFail """
module storageModule '../storageAccount.bicep' = {
  name: 'storageDeploy'
  params: {
    storagePrefix: 'examplestg1'
  }
}
"""

        let storageModule = BicepProgram.findModule "storageModule" program
        Expect.equal storageModule.path "../storageAccount.bicep" "path is parsed correctly"
        let expectedBody = BicepSyntax.Object(Map.ofList [
            BicepSyntax.Identifier "name", BicepSyntax.String "storageDeploy"
            BicepSyntax.Identifier "params", BicepSyntax.Object(Map.ofList [
                BicepSyntax.Identifier "storagePrefix", BicepSyntax.String "examplestg1"
            ])
        ])
        
        Expect.equal storageModule.value expectedBody "module body is parsed correctly"
    }

    test "dropping unknown properties from azure resources works" {
        let program = parseOrFail """
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = {
  name: 'storageaccount'
  location: 'eastus'
  kind: 'StorageV2'
  tags: {
    environment: 'dev'
    whatever: 'value'
  }
  sku: {
    name: 'Standard_LRS'
    whatever: 'hello'
  }
  
  what_is_this: {}
}
"""

        let exampleStorage =
            program
            |> BicepProgram.dropResourceUnknowns
            |> BicepProgram.findResource "exampleStorage"

        let expectedBody = BicepSyntax.Object(Map.ofList [
            BicepSyntax.Identifier "name", BicepSyntax.String "storageaccount"
            BicepSyntax.Identifier "location", BicepSyntax.String "eastus"
            BicepSyntax.Identifier "kind", BicepSyntax.String "StorageV2"
            BicepSyntax.Identifier "sku", BicepSyntax.Object(Map.ofList [
                BicepSyntax.Identifier "name", BicepSyntax.String "Standard_LRS"
            ])
            BicepSyntax.Identifier "tags", BicepSyntax.Object(Map.ofList [
                BicepSyntax.Identifier "environment", BicepSyntax.String "dev"
                BicepSyntax.Identifier "whatever", BicepSyntax.String "value"
            ])
        ])
        
        Expect.equal exampleStorage.value expectedBody "module body is parsed correctly"
    }
]

let resourceTokenMappingTests = testList "Resource token mapping" [
    test "Microsoft.Network/networkSecurityGroups@2020-06-01" {
        let azureSpecToken = "Microsoft.Network/networkSecurityGroups@2020-06-01"
        let expectedPulumiToken = "azure-native:network/v20200601:NetworkSecurityGroup"
        let token = ResourceTokens.fromAzureSpecToPulumi azureSpecToken
        Expect.equal expectedPulumiToken token "Token is correctly mapped"
    }

    test "Microsoft.Storage/storageAccounts@2021-02-01" {
        let azureSpecToken = "Microsoft.Storage/storageAccounts@2021-02-01"
        // the default version is selected because 2020-02-01 is not available
        let expectedPulumiToken = "azure-native:storage:StorageAccount"
        let token = ResourceTokens.fromAzureSpecToPulumi azureSpecToken
        Expect.equal token expectedPulumiToken  "Token is correctly mapped"
    }
    
    test "Microsoft.Storage/storageAccounts" {
        let azureSpecToken = "Microsoft.Storage/storageAccounts"
        let expectedPulumiToken = "azure-native:storage:StorageAccount"
        let token = ResourceTokens.fromAzureSpecToPulumi azureSpecToken
        Expect.equal expectedPulumiToken token "Token is correctly mapped"
    }
    
    test "Microsoft.Network/dnsResolvers/inboundEndpoints@2022-07-01" {
        let azureSpecToken = "Microsoft.Network/dnsResolvers/inboundEndpoints@2022-07-01"
        let expectedPulumiToken = "azure-native:network/v20220701:InboundEndpoint"
        let token = ResourceTokens.fromAzureSpecToPulumi azureSpecToken
        Expect.equal expectedPulumiToken token "Token is correctly mapped"
    }
    
    test "Microsoft.Network/dnsForwardingRulesets/virtualNetworkLinks@2022-07-01" {
        let azureSpecToken = "Microsoft.Network/dnsForwardingRulesets/virtualNetworkLinks@2022-07-01"
        let expectedPulumiToken = "azure-native:network/v20220701:VirtualNetworkLink"
        let token = ResourceTokens.fromAzureSpecToPulumi azureSpecToken
        Expect.equal expectedPulumiToken token "Token is correctly mapped"
    }
    
    test "Without version Microsoft.Network/networkSecurityGroups@2020-06-01" {
        let azureSpecToken = "Microsoft.Network/networkSecurityGroups@2020-06-01"
        let expectedPulumiToken = "azure-native:network:NetworkSecurityGroup"
        let token = ResourceTokens.fromAzureSpecToPulumiWithoutVersion azureSpecToken
        Expect.equal expectedPulumiToken token "Token is correctly mapped"
    }

    test "Without version Microsoft.Storage/storageAccounts@2021-02-01" {
        let azureSpecToken = "Microsoft.Storage/storageAccounts@2021-02-01"
        let expectedPulumiToken = "azure-native:storage:StorageAccount"
        let token = ResourceTokens.fromAzureSpecToPulumiWithoutVersion azureSpecToken
        Expect.equal expectedPulumiToken token "Token is correctly mapped"
    }
    
    test "Without version Microsoft.Network/dnsResolvers/inboundEndpoints@2022-07-01" {
        let azureSpecToken = "Microsoft.Network/dnsResolvers/inboundEndpoints@2022-07-01"
        let expectedPulumiToken = "azure-native:network:InboundEndpoint"
        let token = ResourceTokens.fromAzureSpecToPulumiWithoutVersion azureSpecToken
        Expect.equal expectedPulumiToken token "Token is correctly mapped"
    }
    
    test "Without version Microsoft.Network/dnsForwardingRulesets/virtualNetworkLinks@2022-07-01" {
        let azureSpecToken = "Microsoft.Network/dnsForwardingRulesets/virtualNetworkLinks@2022-07-01"
        let expectedPulumiToken = "azure-native:network:VirtualNetworkLink"
        let token = ResourceTokens.fromAzureSpecToPulumiWithoutVersion azureSpecToken
        Expect.equal expectedPulumiToken token "Token is correctly mapped"
    }
]

let computingVersions = testList "Computing available versions" [
    test "finding exact version works" {
        let input = ResourceTokens.AzureVersion.Stable (DateOnly(2020, 01, 01))
        let available = [
            ResourceTokens.AzureVersion.Stable (DateOnly(2020, 01, 01))
            ResourceTokens.AzureVersion.Stable (DateOnly(2021, 01, 01))
            ResourceTokens.AzureVersion.Stable (DateOnly(2022, 01, 01))
        ]
        
        let chosen = ResourceTokens.chooseVersion input available
        Expect.equal chosen (Some input) "The input was chosen because it was available"
    }
    
    test "finding next available version works" {
        let input = ResourceTokens.AzureVersion.Stable (DateOnly(2023, 01, 01))
        let available = [
            ResourceTokens.AzureVersion.Stable (DateOnly(2020, 01, 01))
            ResourceTokens.AzureVersion.Stable (DateOnly(2022, 01, 01))
            ResourceTokens.AzureVersion.Stable (DateOnly(2024, 01, 01))
            ResourceTokens.AzureVersion.Stable (DateOnly(2025, 01, 01))
        ]
        
        let chosen = ResourceTokens.chooseVersion input available
        let expected = ResourceTokens.AzureVersion.Stable (DateOnly(2024, 01, 01))
        Expect.equal chosen (Some expected) "Next available version is chosen"
    }
    
    test "finding next available version including previews works" {
        let input = ResourceTokens.AzureVersion.Preview (DateOnly(2020, 01, 01))
        let available = [
            ResourceTokens.AzureVersion.Stable (DateOnly(2021, 01, 01))
            ResourceTokens.AzureVersion.Preview (DateOnly(2021, 01, 01))
        ]
        
        let chosen = ResourceTokens.chooseVersion input available
        let expected = ResourceTokens.AzureVersion.Preview (DateOnly(2021, 01, 01))
        Expect.equal chosen (Some expected) "Next available version is chosen to be preview"
    }
]

let configTypeInference = testList "Config type inference" [
    test "example with list(string)" {
        let program = parseOrFail "param names array = ['Contoso', 'Fabrikam','Coho']"
        let names = BicepProgram.findParameter "names" program
        let expectedType = "list(string)"
        let actualType = Transform.inferConfigType names
        Expect.equal expectedType actualType "Type is correctly inferred"
    }
    
    test "empty array returns list(any)" {
        let program = parseOrFail "param names array = []"
        let names = BicepProgram.findParameter "names" program
        let expectedType = "list(any)"
        let actualType = Transform.inferConfigType names
        Expect.equal expectedType actualType "Type is correctly inferred"
    }
    
    test "object with string properties becomes map(string)" {
        let program = parseOrFail "param names object = { a: 'b', c: 'd' }"
        let names = BicepProgram.findParameter "names" program
        let expectedType = "map(string)"
        let actualType = Transform.inferConfigType names
        Expect.equal expectedType actualType "Type is correctly inferred"
    }
    
    test "empty object becomes map(any)" {
        let program = parseOrFail "param names object = { }"
        let names = BicepProgram.findParameter "names" program
        let expectedType = "map(any)"
        let actualType = Transform.inferConfigType names
        Expect.equal expectedType actualType "Type is correctly inferred"
    }

    test "array of objects can be inferred" {
        let program = parseOrFail "param names array = [ { a: 'b', c: 'd' } ]"
        let names = BicepProgram.findParameter "names" program
        let expectedType = "list(map(string))"
        let actualType = Transform.inferConfigType names
        Expect.equal expectedType actualType "Type is correctly inferred"
    }
    
    test "object with properties of different types becomes an object type" {
        let program = parseOrFail "param people array = [ { name: 'John', age: 42 } ]"
        let people = BicepProgram.findParameter "people" program
        let expectedType = "list(object({age=int, name=string}))"
        let actualType = Transform.inferConfigType people
        Expect.equal actualType expectedType "Type is correctly inferred"
    }
]

let resourceTransforms = testList "resource transformations" [
    test "extracting empty resource options" {
        let properties = Map.empty
        let emptyProgram = { declarations = [ ]; programKind = ProgramKind.EntryPoint }
        let options = Transform.extractResourceOptions properties emptyProgram
        Expect.isNone options "resource options are None"
    }
    
    test "extracting parent from resource options" {
        let properties = Map.ofList [
            BicepSyntax.Identifier "parent", BicepSyntax.VariableAccess "rg"
        ]
        
        let emptyProgram = { declarations = [ ]; programKind = ProgramKind.EntryPoint }
        let options = Transform.extractResourceOptions properties emptyProgram
        Expect.isSome options "resource options are not empty"
        let options = Option.get options
        let expected = { ResourceOptions.Empty with parent = Some (PulumiSyntax.VariableAccess "rg") }
        Expect.equal options expected "parent is extracted correctly"
    }
    
    test "extracting dependsOn from resource options" {
        let properties = Map.ofList [
            BicepSyntax.Identifier "parent", BicepSyntax.VariableAccess "rg"
            BicepSyntax.Identifier "dependsOn", BicepSyntax.Array [
                BicepSyntax.VariableAccess "rg"
            ]
        ]

        let emptyProgram = { declarations = [ ]; programKind = ProgramKind.EntryPoint }
        let options = Transform.extractResourceOptions properties emptyProgram
        Expect.isSome options "resource options are not empty"
        let options = Option.get options
        let expected = {
            ResourceOptions.Empty with
                parent = Some (PulumiSyntax.VariableAccess "rg")
                dependsOn = Some (PulumiSyntax.Array [ PulumiSyntax.VariableAccess "rg" ])
        }
        Expect.equal options expected "parent is extracted correctly"
    }
    
    test "simple resource can be transformed"  {
        let program = parseOrFail """
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = {
  name: 'storage'
  location: 'eastus'
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
}
"""
        
        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        match Transform.bicepResource exampleStorage program with
        | Some pulumiResource ->
            Expect.equal pulumiResource.name "exampleStorage" "The name is correct"
            Expect.equal pulumiResource.token "azure-native:storage:StorageAccount" "token is correct"
            let expectedResourceInputs = Map.ofList [
                "accountName", PulumiSyntax.String "storage"
                "location", PulumiSyntax.String "eastus"
                "kind", PulumiSyntax.String "StorageV2"
                "sku", PulumiSyntax.Object (Map.ofList [
                    PulumiSyntax.Identifier "name", PulumiSyntax.String "Standard_LRS"
                ])
            ]
            Expect.equal pulumiResource.inputs expectedResourceInputs "resource inputs are correct"
            Expect.isNone pulumiResource.options "There were no resource options"
            Expect.equal pulumiResource.logicalName (Some "storage") "logical name is extracted correctly"
        | None ->
            failwith "Couldn't transform resource 'exampleStorage'"
    }

    test "resource group scoping is modified to resourceGroupName"  {
        let program = parseOrFail """
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = {
  location: 'eastus'
  scope: resourceGroup()
}
"""
        
        let simplifiedProgram = program |> BicepProgram.reduceScopeParameter 
        let exampleStorage = BicepProgram.findResource "exampleStorage" simplifiedProgram
        let expectedResourceBody = BicepSyntax.Object(Map.ofList [
            BicepSyntax.Identifier "location", BicepSyntax.String "eastus"
            BicepSyntax.Identifier "resourceGroupName", BicepSyntax.PropertyAccess(
                BicepSyntax.FunctionCall("resourceGroup", [  ]),
                "name")
        ])
        
        Expect.equal exampleStorage.value expectedResourceBody "The resource body is simplified correctly"
    }
    
    test "resource group scoping is modified to resourceGroupName when referencing a resource group"  {
        let program = parseOrFail """
param resourceGroupName string
var currentResourceGroup = resourceGroup(resourceGroupName)
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = {
  location: 'eastus'
  scope: currentResourceGroup
}
"""
        
        let simplifiedProgram = program |> BicepProgram.reduceScopeParameter 
        let exampleStorage = BicepProgram.findResource "exampleStorage" simplifiedProgram
        let expectedResourceBody = BicepSyntax.Object(Map.ofList [
            BicepSyntax.Identifier "location", BicepSyntax.String "eastus"
            BicepSyntax.Identifier "resourceGroupName", BicepSyntax.PropertyAccess(
                BicepSyntax.VariableAccess "currentResourceGroup", "name")
        ])
        
        Expect.equal exampleStorage.value expectedResourceBody "The resource body is simplified correctly"
    }
    
    test "resource group scoping is modified to plain resourceGroupName when referencing a resource group call"  {
        let program = parseOrFail """
param rg string
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = {
  location: 'eastus'
  scope: resourceGroup(rg)
}
"""
        
        let simplifiedProgram = program |> BicepProgram.reduceScopeParameter 
        let exampleStorage = BicepProgram.findResource "exampleStorage" simplifiedProgram
        let expectedResourceBody = BicepSyntax.Object(Map.ofList [
            BicepSyntax.Identifier "location", BicepSyntax.String "eastus"
            BicepSyntax.Identifier "resourceGroupName", BicepSyntax.VariableAccess "rg"
        ])
        
        Expect.equal exampleStorage.value expectedResourceBody "The resource body is simplified correctly"
    }
    
    test "resource group scoping is modified to remove scope when using subscription"  {
        let program = parseOrFail """
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = {
  location: 'eastus'
  scope: subscription().id
}
"""

        let simplifiedProgram = program |> BicepProgram.reduceScopeParameter 
        let exampleStorage = BicepProgram.findResource "exampleStorage" simplifiedProgram
        let expectedResourceBody = BicepSyntax.Object(Map.ofList [
            BicepSyntax.Identifier "location", BicepSyntax.String "eastus"
            BicepSyntax.Identifier "resourceGroupName", BicepSyntax.PropertyAccess(
                BicepSyntax.FunctionCall("resourceGroup", [  ]), "name")
        ])
        
        Expect.equal exampleStorage.value expectedResourceBody "The resource body is simplified correctly"
    }
    
    test "resource input odata.type is normalized to odataType"  {
        let program = parseOrFail """
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = {
  name: 'storage'
  'odata.type': 'eastus'
  sku: {
    'odata.type': 'Standard_LRS'
  }
}
"""

        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        match Transform.bicepResource exampleStorage program with
        | Some pulumiResource ->
            let expectedResourceInputs = Map.ofList [
                "accountName", PulumiSyntax.String "storage"
                "odataType", PulumiSyntax.String "eastus"
                "sku", PulumiSyntax.Object (Map.ofList [
                    PulumiSyntax.Identifier "odataType", PulumiSyntax.String "Standard_LRS"
                ])
            ]
            Expect.equal pulumiResource.inputs expectedResourceInputs "resource inputs are correct"
        | None ->
            failwith "Couldn't transform resource 'exampleStorage'"
    }

    test "resource with property mappings applies them correctly"  {
        let program = parseOrFail """
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = {
  name: 'storage'
  properties: {
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}
"""

        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        match Transform.bicepResource exampleStorage program with
        | Some pulumiResource ->
            let expectedResourceInputs = Map.ofList [
                "accountName", PulumiSyntax.String "storage"
                "networkRuleSet", PulumiSyntax.Object (Map.ofList [
                    PulumiSyntax.Identifier "defaultAction", PulumiSyntax.String "Allow"
                ])
            ]
            Expect.equal pulumiResource.inputs expectedResourceInputs "resource inputs are correct"
        | None ->
            failwith "Couldn't transform resource 'exampleStorage'"
    }
    
    test "conditional resource can be transformed" {
        let program = parseOrFail """
param createStorage bool = false
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = if (createStorage) {
  name: 'storage'
  location: 'eastus'
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
}
"""
        
        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        match Transform.bicepResource exampleStorage program with
        | Some pulumiResource ->
            Expect.equal pulumiResource.name "exampleStorage" "The name is correct"
            Expect.equal pulumiResource.token "azure-native:storage:StorageAccount" "token is correct"
            let expectedResourceInputs = Map.ofList [
                "accountName", PulumiSyntax.String "storage"
                "location", PulumiSyntax.String "eastus"
                "kind", PulumiSyntax.String "StorageV2"
                "sku", PulumiSyntax.Object (Map.ofList [
                    PulumiSyntax.Identifier "name", PulumiSyntax.String "Standard_LRS"
                ])
            ]
            Expect.equal pulumiResource.inputs expectedResourceInputs "resource inputs are correct"
            match pulumiResource.options with
            | None -> failwith "expected resource options to have a value"
            | Some resourceOptions ->
                let expectedOptions = {
                    ResourceOptions.Empty
                        with range = Some (PulumiSyntax.TernaryExpression(
                            PulumiSyntax.VariableAccess "createStorage",
                            PulumiSyntax.Integer 1,
                            PulumiSyntax.Integer 0))
                }

                Expect.equal resourceOptions expectedOptions "resource options are parsed correctly"
            Expect.equal pulumiResource.logicalName (Some "storage") "logical name is extracted correctly"
        | None ->
            failwith "Couldn't transform resource 'exampleStorage'"
    }
    
    test "transforming module to component declaration works" {
        let program = parseOrFail """
module storageModule '../storageAccount.bicep' = {
  name: 'storageDeploy'
  params: {
    storagePrefix: 'examplestg1'
  }
}
"""
        let storageModule = BicepProgram.findModule "storageModule" program
        match Transform.bicepModule storageModule program with
        | None -> failwith "failed to transform storageModule"
        | Some componentDecl ->
            Expect.equal componentDecl.name "storageModule" "Name is correct"
            Expect.equal componentDecl.path "../storageAccount.bicep" "path is correct"
            Expect.equal componentDecl.logicalName (Some "storageDeploy") "logical name is correct"
            let expectedInputs = Map.ofList [
                PulumiSyntax.Identifier "storagePrefix", PulumiSyntax.String "examplestg1"
            ]
            Expect.equal componentDecl.inputs expectedInputs "inputs are correct"
    }
    
    test "accessing resource properties is reduced" {
        let program = parseOrFail """
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = { 
  name: 'storage'
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
}

output storageEndpoint object = exampleStorage.properties.primaryEndpoints
"""
        let storageEndpoint = BicepProgram.findOutput "storageEndpoint" program
        let pulumiOutput = Transform.bicepOutput storageEndpoint program
        Expect.equal pulumiOutput.name "storageEndpoint" "name is correct"
        let expectedValue = PulumiSyntax.PropertyAccess(
            PulumiSyntax.VariableAccess "exampleStorage", "primaryEndpoints")
        
        Expect.equal pulumiOutput.value expectedValue "value is correct"
    }
    
    test "accessing module outputs is reduced" {
        let program = parseOrFail """
module exampleStorage '../storageAccount.bicep' = { 
  params: {
    name: 'Standard_LRS'
  }
}

output storageData object = exampleStorage.outputs.data
"""
        let storageData = BicepProgram.findOutput "storageData" program
        let pulumiOutput = Transform.bicepOutput storageData program
        Expect.equal pulumiOutput.name "storageData" "name is correct"
        let expectedValue = PulumiSyntax.PropertyAccess(
            PulumiSyntax.VariableAccess "exampleStorage", "data")
        
        Expect.equal pulumiOutput.value expectedValue "value is correct"
    }
    
    test "resource with range(0, x) can be transformed into a simple count" {
        let program = parseOrFail """
param locations array = ['eastus', 'westus']
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = [for index in range(0, length(locations)) : { 
  name: 'storage'
  location: locations[index]
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
}]
"""
        
        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        match Transform.bicepResource exampleStorage program with
        | Some pulumiResource ->
            Expect.equal pulumiResource.name "exampleStorage" "The name is correct"
            Expect.equal pulumiResource.token "azure-native:storage:StorageAccount" "token is correct"
            let expectedResourceInputs = Map.ofList [
                // location = locations[index] is converted to location = locations[range.value]
                "location", PulumiSyntax.IndexExpression(
                    PulumiSyntax.VariableAccess "locations",
                    PulumiSyntax.PropertyAccess (PulumiSyntax.VariableAccess "range", "value"))
                
                "kind", PulumiSyntax.String "StorageV2"
                "accountName", PulumiSyntax.String "storage"
                "sku", PulumiSyntax.Object (Map.ofList [
                    PulumiSyntax.Identifier "name", PulumiSyntax.String "Standard_LRS"
                ])
            ]
            Expect.equal pulumiResource.inputs expectedResourceInputs "resource inputs are correct"
            match pulumiResource.options with
            | None -> failwith "expected resource options to have a value"
            | Some resourceOptions ->
                let expectedOptions = {
                    ResourceOptions.Empty
                        with range = Some (
                            PulumiSyntax.FunctionCall("length", [ PulumiSyntax.VariableAccess "locations"  ])
                        )
                }

                Expect.equal resourceOptions expectedOptions "resource options are parsed correctly"
            Expect.equal pulumiResource.logicalName (Some "storage") "logical name is extracted correctly"
        | None ->
            failwith "Couldn't transform resource 'exampleStorage'"
    }
    
    test "transform existing resource to invoke works" {
        let program = parseOrFail """
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' existing = {
  name: 'existingStorageName'
}
"""
        let pulumiProgram =
            program
            |> BicepProgram.reduceScopeParameter
            |> BicepProgram.parameterizeByResourceGroup
            |> Transform.bicepProgramToPulumi
        
        
        let exampleStorage = Transform.findPulumiVariable "exampleStorage" pulumiProgram

        let expectedValue = PulumiSyntax.FunctionCall("invoke", [
            PulumiSyntax.String "azure-native:storage:getStorageAccount"
            PulumiSyntax.Object (Map.ofList [
                PulumiSyntax.Identifier "accountName", PulumiSyntax.String "existingStorageName"
                PulumiSyntax.Identifier "resourceGroupName", PulumiSyntax.PropertyAccess(
                    PulumiSyntax.VariableAccess "currentResourceGroup", "name")
            ])
        ])
        
        Expect.equal exampleStorage.value expectedValue "invoke is correctly translated"
    }
    
    test "resource properties are collapsed with top-level attributes" {
        let program = parseOrFail """
resource storage 'Microsoft.Storage/storageAccounts@2021-04-01' = {
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    nested: {
      data: 42
    }
  }
}
"""
    
        let storage = BicepProgram.findResource "storage" program
        match Transform.bicepResource storage program with
        | None -> failwith "failed to transform resource"
        | Some resource ->
            let expectedInputs = Map.ofList [
                "kind", PulumiSyntax.String "StorageV2"
                "supportsHttpsTrafficOnly", PulumiSyntax.Boolean true
                "nested", PulumiSyntax.Object (Map.ofList [
                    PulumiSyntax.Identifier "data", PulumiSyntax.Integer 42L
                ])
            ]
            
            Expect.equal resource.inputs expectedInputs "Properties are collapsed correctly"
    }
    
    test "resource iterating over generic collection can be transformed into range" {
        let program = parseOrFail """
param locations array = ['eastus', 'westus']
resource exampleStorage 'Microsoft.Storage/storageAccounts@2021-02-01' = [for (location, index) in locations : { 
  location: locations[index]
  tag: location
}]
"""

        let exampleStorage = BicepProgram.findResource "exampleStorage" program
        match Transform.bicepResource exampleStorage program with
        | Some pulumiResource ->
            Expect.equal pulumiResource.name "exampleStorage" "The name is correct"
            Expect.equal pulumiResource.token "azure-native:storage:StorageAccount" "token is correct"
            let expectedResourceInputs = Map.ofList [
                // location = locations[index] is converted to location = locations[range.key]
                "location", PulumiSyntax.IndexExpression(
                    PulumiSyntax.VariableAccess "locations",
                    PulumiSyntax.PropertyAccess (PulumiSyntax.VariableAccess "range", "key"))
                
                // location reference becomes range.value
                "tag", PulumiSyntax.PropertyAccess (PulumiSyntax.VariableAccess "range", "value")
            ]
            
            Expect.equal pulumiResource.inputs expectedResourceInputs "resource inputs are correct"
            match pulumiResource.options with
            | None -> failwith "expected resource options to have a value"
            | Some resourceOptions ->
                let expectedOptions = {
                    ResourceOptions.Empty with range = Some (PulumiSyntax.VariableAccess "locations")
                }

                Expect.equal resourceOptions expectedOptions "resource options are parsed correctly"
        | None ->
            failwith "Couldn't transform resource 'exampleStorage'"
    }
]

let printExpr (expr: BicepSyntax) =
    let emptyProgram = { declarations = [ ]; programKind = ProgramKind.EntryPoint }
    let builder = StringBuilder()
    Printer.print (Transform.fromBicep expr emptyProgram) 0 builder
    builder.ToString()

let printTests = testList "Printing" [
    test "printing interpolated strings" {
        let exprAfter = printExpr(parseExpression "'storage-${1}'")
        Expect.equal exprAfter "\"storage-${1}\"" "interpolated string printed"
        
        let exprBefore = printExpr(parseExpression "'${1}-storage'")
        Expect.equal exprBefore "\"${1}-storage\"" "interpolated string printed"
        
        let exprInBetween = printExpr(parseExpression "'example-${1}-storage'")
        Expect.equal exprInBetween "\"example-${1}-storage\"" "interpolated string printed"
        
        let exprMixed = printExpr (parseExpression "'hello-${1}-from-${2}-until-${3}'")
        Expect.equal exprMixed "\"hello-${1}-from-${2}-until-${3}\"" "interpolated string printed"
    }
    
    test "printing comparison operators work" {
        let printed = printExpr(parseExpression "this > that")
        Expect.equal printed "this > that" "printed correctly"
        
        let printed = printExpr(parseExpression "this >= that")
        Expect.equal printed "this >= that" "printed correctly"
        
        let printed = printExpr(parseExpression "this < that")
        Expect.equal printed "this < that" "printed correctly"
        
        let printed = printExpr(parseExpression "this <= that")
        Expect.equal printed "this <= that" "printed correctly"
        
        let printed = printExpr(parseExpression "this == that")
        Expect.equal printed "this == that" "printed correctly"
        
        let printed = printExpr(parseExpression "this != that")
        Expect.equal printed "this != that" "printed correctly"
    }

    test "printing for expressions works and compiling to entries function" {
        let printed =
            "[ for number in numbers : number + 1 ]"
            |> parseExpression
            |> printExpr
       
        let expected = "[for entry in entries(numbers) : entry.value + 1]"
        
        Expect.equal printed expected "printed correctly"
    }

    test "printing for expressions works with key and value" {
        let printed =
            "[ for (number, i) in numbers : number + i ]"
            |> parseExpression
            |> printExpr
       
        let expected = "[for entry in entries(numbers) : entry.value + entry.key]"
        
        Expect.equal printed expected "printed correctly"
    }
]

let allTests = testList "All tests" [
    parameterParsingTests
    resourceTokenMappingTests
    configTypeInference
    resourceTransforms
    printTests
    computingVersions
]

[<EntryPoint>]
let main (args:string[]) = runTestsWithCLIArgs [  ] args allTests

