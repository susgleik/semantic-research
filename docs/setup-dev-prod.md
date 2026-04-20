# Setup — Development y Production
## SemanticSearch RAG · .NET 10 / ASP.NET Core

---

## Servicios externos — qué se puede emular y qué no

| Servicio | Emulable local | Herramienta | Necesita credenciales reales |
|---|---|---|---|
| Azure Blob Storage | SI | **Azurite** (Docker) | No en dev |
| Azure AI Search | **NO** | sin emulador oficial | **SI — siempre** |
| OpenAI (api.openai.com) | **NO** | sin emulador oficial | **SI — siempre** |
| Azure AD (auth JWT) | parcial | deshabilitado en dev | **SI en producción** |

> Para dev necesitás credenciales reales de Azure AI Search y OpenAI (api.openai.com).
> Blob Storage corre completamente local con Azurite.
>
> **Nota:** Este proyecto usa OpenAI directo (`api.openai.com`) en lugar de Azure OpenAI.
> Azure OpenAI requiere suscripción de pago con aprobación manual de Microsoft.

---

## Setup Development

### Prerrequisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (para Azurite)
- Una cuenta en:
  - [OpenAI Platform](https://platform.openai.com) — API Key (`sk-...`)
  - Azure con Azure AI Search creado (tier Free sirve para dev)

---

### Paso 1 — Levantar Azurite (Blob Storage local)

```bash
# Desde la root del repo
docker compose up -d azurite
```

Verificar que está corriendo:
```bash
docker compose ps
# azurite   Up   0.0.0.0:10000->10000/tcp
```

La connection string para Azurite es siempre esta (no cambia, es el valor por defecto del emulador):
```
UseDevelopmentStorage=true
```

---

### Paso 2 — Configurar credenciales con user-secrets

`dotnet user-secrets` guarda las credenciales **fuera del repositorio**, en tu sistema local.
Es el equivalente a un archivo `.env` que nunca se commitea.

**Dónde se guardan físicamente:**
```
Linux/Mac:  ~/.microsoft/usersecrets/semantic-search-api-dev/secrets.json
Windows:    %APPDATA%\Microsoft\UserSecrets\semantic-search-api-dev\secrets.json
```

El ID `semantic-search-api-dev` viene del campo `<UserSecretsId>` en el `.csproj`.

**Cargar los secrets** (correlos desde `src/SemanticSearch.Api/`):

```powershell
cd src/SemanticSearch.Api

# OpenAI — API Key de api.openai.com
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."

# Azure AI Search — credenciales reales obligatorias
dotnet user-secrets set "AzureSearch:ApiKey"   "tu-api-key-de-azure-search"
dotnet user-secrets set "AzureSearch:Endpoint" "https://tu-resource.search.windows.net"

# Blob Storage — usar Azurite local (no necesita credenciales reales)
dotnet user-secrets set "AzureBlob:ConnectionString" "UseDevelopmentStorage=true"
```

Verificar que se guardaron:
```powershell
dotnet user-secrets list
# OpenAI:ApiKey = sk-...
# AzureSearch:ApiKey = tu-api-key-de-azure-search
# AzureBlob:ConnectionString = UseDevelopmentStorage=true
```

---

### Referencia completa de comandos user-secrets

Todos los comandos se corren desde `src/SemanticSearch.Api/` donde está el `.csproj`
con el `<UserSecretsId>`.

**Guardar / actualizar un secret:**
```powershell
dotnet user-secrets set "Seccion:Clave" "valor"

# Ejemplos reales de este proyecto:
dotnet user-secrets set "OpenAI:ApiKey"              "sk-..."
dotnet user-secrets set "OpenAI:EmbeddingDeployment" "text-embedding-3-large"
dotnet user-secrets set "OpenAI:ChatDeployment"      "gpt-4o"
dotnet user-secrets set "AzureSearch:ApiKey"         "abc123..."
dotnet user-secrets set "AzureSearch:Endpoint"       "https://mi-search.search.windows.net"
dotnet user-secrets set "AzureSearch:IndexName"      "documents"
dotnet user-secrets set "AzureBlob:ConnectionString" "UseDevelopmentStorage=true"
dotnet user-secrets set "AzureBlob:Container"        "docs"
```

**Listar todos los secrets activos:**
```powershell
dotnet user-secrets list
```

**Eliminar un secret específico:**
```powershell
dotnet user-secrets remove "OpenAI:ApiKey"
```

**Eliminar TODOS los secrets del proyecto:**
```powershell
dotnet user-secrets clear
```

El archivo secrets.json tiene esta forma:
```json
{
  "OpenAI:ApiKey": "sk-...",
  "AzureSearch:ApiKey": "tu-key-real",
  "AzureSearch:Endpoint": "https://mi-resource.search.windows.net",
  "AzureBlob:ConnectionString": "UseDevelopmentStorage=true"
}
```

> Este archivo vive **fuera del repo** y nunca se commitea.
> Si cambiás de máquina tenés que volver a correr los `dotnet user-secrets set`.

---

### Paso 3 — Correr la API

```powershell
cd src/SemanticSearch.Api
dotnet run
```

La API arranca en `http://localhost:5000`.
Documentación interactiva (Scalar): `http://localhost:5000/scalar/v1`

---

### Cómo .NET carga la config en Development

.NET apila las fuentes de config en este orden (cada capa sobreescribe la anterior):

```
1. appsettings.json             ← base, valores por defecto
2. appsettings.Development.json ← overrides para dev (placeholder values)
3. user-secrets                 ← credenciales reales, fuera del repo ← gana
```

---

### Resumen del entorno Development

```
tu máquina
├── Docker
│   └── Azurite (puerto 10000) ← Blob Storage real, sin credenciales
├── dotnet run (puerto 5000)
│   ├── lee appsettings.Development.json
│   └── lee %APPDATA%\Microsoft\UserSecrets\...\secrets.json
└── Servicios externos (cloud)
    ├── Azure AI Search  ← credencial real en user-secrets
    └── OpenAI           ← credencial real en user-secrets (sk-...)
```

---

## Setup Production

### Prerequisitos

- Azure CLI (`az`) instalado y autenticado (`az login`)
- Docker
- Acceso al Azure Container Registry (ACR)

---

### Paso 1 — Provisionar infraestructura con Bicep

```powershell
az deployment group create `
  --resource-group rg-semantic-search-dev `
  --template-file infra/main.bicep `
  --parameters @infra/params.json `
  --parameters openAiApiKey="sk-..." searchApiKey="TU_SEARCH_KEY"
```

Los secrets (`openAiApiKey`, `searchApiKey`) se pasan directo en el comando — **nunca en `params.json`**.

---

### Paso 2 — Build y push de la imagen

```powershell
az acr build `
  --registry TU_ACR_NAME `
  --image semantic-search-api:latest `
  ./src/SemanticSearch.Api
```

---

### Paso 3 — Deploy en Azure Container Apps

```powershell
az containerapp update `
  --name semantic-search-dev-api `
  --resource-group rg-semantic-search-dev `
  --image TU_ACR_NAME.azurecr.io/semantic-search-api:latest
```

En producción las credenciales van en **Azure Key Vault** referenciadas desde Container Apps
— nunca como variables de entorno en texto plano.

---

### Diferencias clave entre entornos

| | Development | Production |
|---|---|---|
| Auth Azure AD | deshabilitada | activa (JWT obligatorio) |
| Scalar / Swagger UI | activo en `/scalar/v1` | deshabilitado |
| Blob Storage | Azurite local | Azure Blob real |
| Credenciales | `user-secrets` (local) | Azure Key Vault |
| `ValidateOnStart` | activo | activo |
| Logging | Debug | Information/Warning |
