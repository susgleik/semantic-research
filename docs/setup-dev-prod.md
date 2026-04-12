# Setup — Development y Production
## SemanticSearch RAG · .NET 10 / ASP.NET Core

---

## Servicios Azure — qué se puede emular y qué no

| Servicio | Emulable local | Herramienta | Necesita credenciales reales |
|---|---|---|---|
| Azure Blob Storage | SI | **Azurite** (Docker) | No en dev |
| Azure AI Search | **NO** | sin emulador oficial | **SI — siempre** |
| Azure OpenAI | **NO** | sin emulador oficial | **SI — siempre** |
| Azure AD (auth JWT) | parcial | deshabilitado en dev | **SI en producción** |

> Para dev necesitás credenciales reales de Azure AI Search y Azure OpenAI.
> Blob Storage corre completamente local con Azurite.

---

## Setup Development

### Prerrequisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (para Azurite)
- Una cuenta Azure con:
  - Azure AI Search creado (tier Free sirve para dev)
  - Azure OpenAI con deployment de `text-embedding-3-large` y `gpt-4o`

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

### Paso 2 — Configurar credenciales reales con user-secrets

`dotnet user-secrets` guarda las credenciales **fuera del repositorio**, en tu sistema local.
Es el equivalente a un archivo `.env` que nunca se commitea.

**Dónde se guardan físicamente:**
```
Linux/Mac:  ~/.microsoft/usersecrets/semantic-search-api-dev/secrets.json
Windows:    %APPDATA%\Microsoft\UserSecrets\semantic-search-api-dev\secrets.json
```

El ID `semantic-search-api-dev` viene del campo `<UserSecretsId>` en el `.csproj`.

**Cargar los secrets** (correlos desde `src/SemanticSearch.Api/`):

```bash
cd src/SemanticSearch.Api

# Azure OpenAI — credenciales reales obligatorias
dotnet user-secrets set "AzureOpenAI:ApiKey"   "tu-api-key-de-azure-openai"
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://tu-resource.openai.azure.com/"

# Azure AI Search — credenciales reales obligatorias
dotnet user-secrets set "AzureSearch:ApiKey"   "tu-api-key-de-azure-search"
dotnet user-secrets set "AzureSearch:Endpoint" "https://tu-resource.search.windows.net"

# Blob Storage — usar Azurite local (no necesita credenciales reales)
dotnet user-secrets set "AzureBlob:ConnectionString" "UseDevelopmentStorage=true"
```

Verificar que se guardaron:
```bash
dotnet user-secrets list
# AzureOpenAI:ApiKey = tu-api-key-de-azure-openai
# AzureSearch:ApiKey = tu-api-key-de-azure-search
# AzureBlob:ConnectionString = UseDevelopmentStorage=true
```

---

### Referencia completa de comandos user-secrets

Todos los comandos se corren desde `src/SemanticSearch.Api/` donde está el `.csproj`
con el `<UserSecretsId>`.

**Guardar / actualizar un secret:**
```bash
dotnet user-secrets set "Seccion:Clave" "valor"

# Ejemplos reales de este proyecto:
dotnet user-secrets set "AzureOpenAI:ApiKey"            "sk-..."
dotnet user-secrets set "AzureOpenAI:Endpoint"          "https://mi-resource.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:EmbeddingDeployment" "text-embedding-3-large"
dotnet user-secrets set "AzureOpenAI:ChatDeployment"    "gpt-4o"
dotnet user-secrets set "AzureSearch:ApiKey"            "abc123..."
dotnet user-secrets set "AzureSearch:Endpoint"          "https://mi-search.search.windows.net"
dotnet user-secrets set "AzureSearch:IndexName"         "documents"
dotnet user-secrets set "AzureBlob:ConnectionString"    "UseDevelopmentStorage=true"
dotnet user-secrets set "AzureBlob:Container"           "docs"
```

**Listar todos los secrets activos:**
```bash
dotnet user-secrets list
```

**Eliminar un secret específico:**
```bash
dotnet user-secrets remove "AzureOpenAI:ApiKey"
```

**Eliminar TODOS los secrets del proyecto:**
```bash
dotnet user-secrets clear
```

**Ver el archivo secrets.json directamente** (es un JSON plano):
```bash
# Linux / Mac
cat ~/.microsoft/usersecrets/semantic-search-api-dev/secrets.json

# Windows
type %APPDATA%\Microsoft\UserSecrets\semantic-search-api-dev\secrets.json
```

El archivo tiene esta forma — es el mismo formato que `appsettings.json`:
```json
{
  "AzureOpenAI:ApiKey": "tu-key-real",
  "AzureOpenAI:Endpoint": "https://mi-resource.openai.azure.com/",
  "AzureSearch:ApiKey": "tu-key-real",
  "AzureSearch:Endpoint": "https://mi-search.search.windows.net",
  "AzureBlob:ConnectionString": "UseDevelopmentStorage=true"
}
```

> Este archivo vive **fuera del repo** y nunca se commitea.
> Si cambiás de máquina tenés que volver a correr los `dotnet user-secrets set`.

---

### Paso 3 — Correr la API

```bash
cd src/SemanticSearch.Api
dotnet run
```

La API arranca en `http://localhost:5000`.
Documentación interactiva (Scalar): `http://localhost:5000/scalar/v1`

---

### Cómo .NET carga la config en Development

.NET apila las fuentes de config en este orden (cada capa sobreescribe la anterior):

```
1. appsettings.json            ← base, valores por defecto
2. appsettings.Development.json ← overrides para dev (placeholder values)
3. user-secrets                ← credenciales reales, fuera del repo ← gana
```

Por eso `appsettings.Development.json` tiene valores `"placeholder"` — los user-secrets
los sobreescriben en tiempo de ejecución. Nunca hay credenciales reales en el repositorio.

**Analogía Python:**
```
.env.example     → appsettings.json
.env.development → appsettings.Development.json
.env             → user-secrets  (ignorado en .gitignore)
```

---

### Resumen del entorno Development

```
tu máquina
├── Docker
│   └── Azurite (puerto 10000) ← Blob Storage real, sin credenciales
├── dotnet run (puerto 5000)
│   ├── lee appsettings.Development.json
│   └── lee ~/.microsoft/usersecrets/semantic-search-api-dev/secrets.json
└── Azure (cloud)
    ├── Azure AI Search  ← credencial real en user-secrets
    └── Azure OpenAI     ← credencial real en user-secrets
```

---

## Setup Production

### Prerequisitos

- Azure CLI (`az`) instalado y autenticado
- Docker
- Acceso al Azure Container Registry (ACR)

---

### Paso 1 — Provisionar infraestructura con Bicep

```bash
az deployment group create \
  --resource-group rg-semantic-search \
  --template-file infra/main.bicep \
  --parameters @infra/params.json
```

---

### Paso 2 — Build y push de la imagen

```bash
az acr build \
  --registry <nombre-acr> \
  --image semantic-search-api:latest \
  ./src/SemanticSearch.Api
```

---

### Paso 3 — Deploy en Azure Container Apps

```bash
az containerapp update \
  --name semantic-search-api \
  --resource-group rg-semantic-search \
  --image <nombre-acr>.azurecr.io/semantic-search-api:latest
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

---

### Agregar un nuevo secret

Si en el futuro necesitás agregar una nueva credencial:

**1. Definirla en la clase Options correspondiente** (`src/SemanticSearch.Core/Options/`)

**2. Agregarla a `appsettings.Development.json`** con valor `"placeholder"`

**3. Cargarla en user-secrets:**
```bash
cd src/SemanticSearch.Api
dotnet user-secrets set "Seccion:NuevaClave" "valor-real"
```

**4. En producción**, agregarla como secret en Key Vault y referenciarla en el Container App.
