# TODO — Proyecto F: Sistema de Búsqueda Semántica RAG
## C# / .NET 8 · ASP.NET Core · Azure Functions v4 · Azure AI Search · Azure OpenAI

---

## Fase 0 — Setup del proyecto

- [x] Crear estructura de carpetas del solution
- [x] Crear `SemanticSearch.sln`
- [x] Crear `SemanticSearch.Core.csproj` (shared library)
- [x] Crear `SemanticSearch.Api.csproj` (ASP.NET Core 8)
- [x] Crear `SemanticSearch.Functions.csproj` (Azure Functions v4)
- [x] Crear `SemanticSearch.McpServer.csproj` (MCP Server)
- [x] Crear proyectos de tests `.csproj`
- [ ] Inicializar repositorio git
- [ ] Configurar `.gitignore` para .NET
- [ ] Configurar `dotnet user-secrets` para credenciales locales
- [ ] Restaurar paquetes NuGet (`dotnet restore`)

---

## Fase 1 — SemanticSearch.Core (modelos y contratos compartidos)

- [x] `Models/DocumentChunk.cs` — modelo de chunk con texto, índice y wordcount
- [x] `Models/IndexedDocument.cs` — documento indexado con metadata
- [x] `Options/OpenAIOptions.cs` — configuración Azure OpenAI (endpoint, key, deployments)
- [x] `Options/SearchOptions.cs` — configuración Azure AI Search
- [x] `Options/BlobOptions.cs` — configuración Azure Blob Storage

---

## Fase 2 — SemanticSearch.Api

### 2a — Configuración base
- [x] `Program.cs` — Minimal API entrypoint con DI, options, middleware y auth
- [x] `appsettings.json` — configuración de Azure services
- [x] `appsettings.Development.json` — overrides para desarrollo local
- [x] `Dockerfile` — multi-stage build para Container Apps

### 2b — Modelos de request/response
- [x] `Models/QueryRequest.cs`
- [x] `Models/QueryResponse.cs`
- [x] `Models/UploadRequest.cs`
- [x] `Models/UploadResponse.cs`
- [x] `Models/SourceChunk.cs`
- [x] `Models/DocumentRecord.cs`

### 2c — Servicios
- [x] `Services/IEmbeddingService.cs` + `EmbeddingService.cs` — Azure OpenAI embeddings
- [x] `Services/ISearchService.cs` + `SearchService.cs` — hybrid search Azure AI Search
- [x] `Services/IBlobService.cs` + `BlobService.cs` — upload a Blob Storage
- [x] `Services/IRagService.cs` + `RagService.cs` — orquestador principal RAG

### 2d — Endpoints (Minimal API)
- [x] `Endpoints/UploadEndpoints.cs` — `POST /upload`
- [x] `Endpoints/QueryEndpoints.cs` — `POST /query`
- [ ] `Endpoints/DocumentEndpoints.cs` — `GET /documents`, `POST /reindex/{docId}`
- [x] `Endpoints/HealthEndpoints.cs` — `GET /health`

### 2e — Middleware
- [x] `Middleware/ExceptionMiddleware.cs` — manejo centralizado de errores
- [ ] `Middleware/AuthMiddleware.cs` — validación Azure AD JWT (ya cubierto por `Microsoft.Identity.Web`)

### 2f — Validaciones y mejoras
- [ ] Agregar validación de tamaño máximo de archivo en `/upload`
- [ ] Agregar soporte de paginación en `GET /documents`
- [ ] Agregar rate limiting con `AspNetCoreRateLimit` o .NET 8 built-in

---

## Fase 3 — SemanticSearch.Functions (Azure Functions v4)

- [x] `Program.cs` — isolated worker host setup con DI
- [x] `host.json` — configuración del host de Azure Functions
- [x] `local.settings.json` — variables de entorno locales (no commitear)
- [x] `Functions/DocumentIndexer.cs` — blob trigger → chunk → embed → index
- [x] `Services/ChunkerService.cs` — sliding window con overlap
- [x] `Services/EmbeddingService.cs` — batch embeddings con Azure OpenAI
- [ ] `Services/SearchIndexerService.cs` — escribe chunks indexados en Azure AI Search
- [ ] Agregar soporte para `.pdf` con **PdfPig**
- [ ] Agregar soporte para `.docx` con **DocumentFormat.OpenXml**
- [ ] Manejar errores de indexación con dead-letter queue o poison blob

---

## Fase 4 — SemanticSearch.McpServer

- [x] `Program.cs` — host del servidor MCP
- [x] `Tools/SearchDocumentsTool.cs` — herramienta `search_documents`
- [ ] `Tools/ListDocumentsTool.cs` — herramienta `list_documents`
- [ ] `Tools/ReindexDocumentTool.cs` — herramienta `reindex_document`
- [ ] Configurar `.vscode/settings.json` con `github.copilot.chat.mcpServers`
- [ ] Probar integración con Copilot Chat (`@doc-search`)

---

## Fase 5 — Tests

### API Tests (`SemanticSearch.Api.Tests`)
- [ ] `Endpoints/QueryEndpointsTests.cs` — test del flujo RAG completo (mock servicios)
- [ ] `Endpoints/UploadEndpointsTests.cs` — test de upload con validaciones
- [ ] `Services/RagServiceTests.cs` — unit test del orquestador RAG

### Functions Tests (`SemanticSearch.Functions.Tests`)
- [ ] `DocumentIndexerTests.cs` — test del indexer con blob simulado
- [ ] Test de `ChunkerService` — validar sliding window y overlap

---

## Fase 6 — Infraestructura (Bicep)

- [ ] `infra/main.bicep` — orquestador principal que llama a los módulos
- [ ] `infra/blob.bicep` — Azure Blob Storage con container `docs`
- [ ] `infra/search.bicep` — Azure AI Search con índice vectorial y semántico
- [ ] `infra/openai.bicep` — Azure OpenAI con deployments de embedding y chat
- [ ] `infra/container-app.bicep` — Container App con autoscaling y secrets de Key Vault
- [ ] `infra/params.json` — parámetros de deploy (sin secrets)
- [ ] Crear índice de AI Search con campo `embedding` vectorial (dims=3072 para `text-embedding-3-large`)

---

## Fase 7 — CI/CD (GitHub Actions)

- [ ] `.github/workflows/deploy-api.yml` — build, test, push imagen y deploy Container App
- [ ] `.github/workflows/deploy-functions.yml` — build y publish Azure Function
- [ ] Configurar secrets en GitHub: `AZURE_CREDENTIALS`, `ACR_NAME`, `RESOURCE_GROUP`
- [ ] Configurar environments en GitHub Actions (staging / production)

---

## Fase 8 — Deploy y configuración en Azure

- [ ] Crear Resource Group en Azure
- [ ] Provisionar infra con `az deployment group create`
- [ ] Configurar Azure AD App Registration para auth JWT
- [ ] Build y push de imagen al Azure Container Registry
- [ ] Crear Container App con variables de entorno y secrets de Key Vault
- [ ] Publicar Azure Function con `func azure functionapp publish`
- [ ] Verificar conectividad con `GET /health`
- [ ] Configurar monitoreo con Application Insights

---

## Checklist de seguridad

- [ ] Nunca commitear credenciales ni connection strings
- [ ] Usar `dotnet user-secrets` en desarrollo
- [ ] Usar Key Vault references en producción
- [ ] Validar tokens JWT con Azure AD en todos los endpoints (excepto `/health`)
- [ ] Configurar CORS correctamente en Container App
- [ ] Revisar permisos mínimos en Managed Identity

---

_Stack: ASP.NET Core 8 · Azure Functions v4 · Azure AI Search · Azure OpenAI · Blob Storage · Container Apps · Bicep · GitHub Actions_
