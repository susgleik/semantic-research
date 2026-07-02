# TODO — Proyecto F: Sistema de Búsqueda Semántica RAG
## C# / .NET 8 · AWS Lambda · API Gateway (HTTP API) · DynamoDB · Amazon Bedrock · S3 · React (Vite)

> Migrado de Azure a **AWS Free Tier** (cuenta nueva, 2026). Arquitectura serverless +
> microservicios: cada función Lambda tiene una sola responsabilidad, desplegada e
> independiente. Ver [`docs/blueprint-csharp.md`](docs/blueprint-csharp.md) para el
> diagrama completo y el razonamiento detrás de cada decisión de servicio.

---

## Fase 0 — Setup de cuenta AWS y del proyecto

- [ ] Crear cuenta AWS (si no existe) y activar MFA en el usuario root
- [ ] Crear un **AWS Budget Alert** a $5-10 USD (Bedrock no tiene free tier)
- [ ] Crear usuario IAM con permisos de despliegue (no usar root para trabajar)
- [ ] Instalar y configurar AWS CLI (`aws configure`)
- [ ] Instalar AWS SAM CLI (`sam --version`)
- [ ] Decidir y documentar la región (ej. `us-east-1`, donde Bedrock tiene más modelos disponibles)
- [ ] Solicitar acceso a los modelos de Bedrock necesarios (Titan Embed Text v2, Claude Haiku) — requiere aprobación manual en la consola
- [ ] Actualizar `.gitignore` para artefactos de AWS SAM/CDK (`.aws-sam/`, `cdk.out/`)
- [ ] Eliminar/archivar `infra/*.bicep` (quedan como referencia histórica de la versión Azure)

---

## Fase 1 — SemanticSearch.Core (modelos y contratos compartidos)

- [x] `Models/DocumentChunk.cs` — modelo de chunk con texto, índice y wordcount
- [x] `Models/IndexedDocument.cs` — documento indexado con metadata
- [ ] Reemplazar `Options/OpenAIOptions.cs` → `Options/BedrockOptions.cs` (región, modelId de embeddings, modelId de chat)
- [ ] Reemplazar `Options/SearchOptions.cs` → `Options/DynamoDbOptions.cs` (nombre de tabla, región)
- [ ] Reemplazar `Options/BlobOptions.cs` → `Options/S3Options.cs` (bucket name, región)
- [ ] `Models/ChunkRecord.cs` — modelo del item de DynamoDB (PK/SK, embedding como `List<float>`, texto, metadata del doc)

---

## Fase 2 — `upload-service` (Lambda)

- [ ] Crear proyecto `src/SemanticSearch.Functions.Upload` (`Amazon.Lambda.Core` + `Amazon.Lambda.APIGatewayEvents`)
- [ ] Handler `UploadFunction.cs` — recibe `POST /upload` desde API Gateway (HTTP API), valida tamaño/tipo de archivo
- [ ] `Services/IS3UploadService.cs` + `S3UploadService.cs` — sube el archivo al bucket `docs`
- [ ] Modelos `UploadRequest.cs` / `UploadResponse.cs` (reusar de `SemanticSearch.Core` si aplica)
- [ ] Validar tamaño máximo de archivo (igual que la versión actual del endpoint `/upload`)

---

## Fase 3 — `indexer-service` (Lambda, trigger por evento S3)

- [ ] Crear proyecto `src/SemanticSearch.Functions.Indexer`
- [ ] Handler `IndexerFunction.cs` — disparado por **S3 Event Notification** (`s3:ObjectCreated:*` sobre el bucket `docs`)
- [ ] `Services/ChunkerService.cs` — reusar sliding window con overlap de la versión actual
- [ ] Agregar soporte para `.pdf` con **PdfPig**
- [ ] Agregar soporte para `.docx` con **DocumentFormat.OpenXml**
- [ ] `Services/IBedrockEmbeddingService.cs` + `BedrockEmbeddingService.cs` — embeddings batch con Titan Embed Text v2
- [ ] `Services/IDynamoChunkWriter.cs` + `DynamoChunkWriter.cs` — escribe `ChunkRecord` en DynamoDB
- [ ] Manejar errores de indexación: mover el objeto a un prefijo `failed/` en S3 (equivalente a poison blob) en vez de DLQ gestionada (mantiene todo dentro de Always Free)

---

## Fase 4 — Pipeline de consulta RAG (Lambdas)

- [ ] Crear proyecto `src/SemanticSearch.Functions.Query`
- [ ] Handler `QueryFunction.cs` — recibe `POST /query`, orquesta embed → search → answer
- [ ] `Services/IBedrockEmbeddingService.cs` — reusar lógica de embeddings (compartir vía `SemanticSearch.Core` o paquete interno)
- [ ] `Services/ISimilaritySearchService.cs` + `SimilaritySearchService.cs` — lee chunks candidatos de DynamoDB y calcula similitud coseno en memoria, retorna top-K
- [ ] `Services/IRagAnswerService.cs` + `RagAnswerService.cs` — arma el prompt con el contexto y llama a Bedrock (Claude Haiku) para generar la respuesta con fuentes citadas
- [ ] Modelos `QueryRequest.cs` / `QueryResponse.cs` / `SourceChunk.cs` (reusar de `SemanticSearch.Core`)

---

## Fase 5 — `documents-service` (Lambda)

- [ ] Crear proyecto `src/SemanticSearch.Functions.Documents`
- [ ] Handler `DocumentsFunction.cs` — `GET /documents` (con paginación), `POST /reindex/{docId}`, `DELETE /documents/{docId}`
- [ ] `Services/IDocumentRegistryService.cs` + `DocumentRegistryService.cs` — lee/escribe metadata de documentos en DynamoDB
- [ ] `GET /health` — healthcheck simple (puede vivir en la misma función o en una función propia, ultraligera)

---

## Fase 6 — Auth (Amazon Cognito)

- [ ] Crear User Pool de Cognito + App Client
- [ ] Configurar **JWT Authorizer** nativo de API Gateway HTTP API contra el User Pool (sin código de validación manual)
- [ ] Excluir `/health` del authorizer
- [ ] Documentar flujo de login (Cognito Hosted UI o SDK) para el frontend

---

## Fase 7 — Frontend (React SPA)

- [ ] Scaffold `frontend/` con Vite + React + TypeScript
- [ ] Cliente HTTP (`src/api/client.ts`) apuntando a la URL de API Gateway
- [ ] Vista: subir documento (drag & drop) → `upload-service`
- [ ] Vista: lista de documentos + estado de indexado → `documents-service`
- [ ] Vista: buscador/chat de preguntas → `query-service`, mostrando respuesta + fuentes citadas
- [ ] Vista/acción: botón reindexar documento
- [ ] Integración de login con Cognito (Hosted UI o `amazon-cognito-identity-js`)
- [ ] Variables de entorno (`.env`) para URL de API y datos de Cognito (no commitear)
- [ ] Build de producción (`npm run build`) y deploy a S3 (bucket `frontend`) + invalidación de CloudFront

---

## Fase 8 — SemanticSearch.McpServer

- [x] `Program.cs` — host del servidor MCP
- [x] `Tools/SearchDocumentsTool.cs` — herramienta `search_documents`
- [x] `Tools/ListDocumentsTool.cs` — herramienta `list_documents`
- [x] `Tools/ReindexDocumentTool.cs` — herramienta `reindex_document`
- [ ] Apuntar las tools a la nueva URL de API Gateway (en vez de Container Apps)
- [ ] Configurar `.vscode/settings.json` con `github.copilot.chat.mcpServers`
- [ ] Probar integración con Copilot Chat (`@doc-search`)

---

## Fase 9 — Tests

### Functions Tests
- [x] `DocumentIndexerTests.cs` — tests de ChunkerService (sliding window, overlap, edge cases)
- [x] Test de `ChunkerService` — 6 casos: ventana exacta, ventana+1, overlap, StartIndex, texto vacío
- [ ] Migrar tests de `SemanticSearch.Api.Tests` a tests por función Lambda (mockear `IAmazonS3`, `IAmazonDynamoDB`, cliente Bedrock)
- [ ] Tests de `SimilaritySearchService` — ranking correcto por similitud coseno
- [ ] Tests de integración local con `dotnet lambda-test-tool` o invocación directa del handler

---

## Fase 10 — Infraestructura como código (AWS SAM)

- [ ] `infra/template.yaml` — template SAM con todos los recursos (Lambdas, API Gateway, S3, DynamoDB, Cognito)
- [ ] Definir tabla DynamoDB: PK `documentId`, SK `chunkId`, GSI si se necesita listar por fecha
- [ ] Definir bucket S3 `docs` con notificación de evento hacia `indexer-service`
- [ ] Definir bucket S3 `frontend` con static website hosting + distribución CloudFront
- [ ] Definir API Gateway HTTP API con rutas hacia cada Lambda + JWT Authorizer de Cognito
- [ ] `infra/samconfig.toml` — configuración de deploy por entorno (dev/prod)
- [ ] Permisos IAM mínimos por función (cada Lambda solo accede a lo que necesita — least privilege)

---

## Fase 11 — `report-service` (Lambda)

El usuario elige un escenario de análisis y el sistema genera un informe basado
en el corpus completo de documentos indexados — diferente al chat RAG donde se
hace una pregunta puntual. El informe se guarda en S3 y queda disponible para
descarga.

- [ ] Crear proyecto `src/SemanticSearch.Functions.Reports`
- [ ] Handler `ReportFunction.cs` — `POST /reports` (recibe escenario + parámetros) y `GET /reports/{reportId}` (descarga el informe generado)
- [ ] `Models/ReportRequest.cs` — escenario elegido + parámetros opcionales (rango de fechas, categoría de documentos, instrucción personalizada)
- [ ] `Models/ReportResponse.cs` — `reportId`, `status` (`generating` / `ready`), `downloadUrl`
- [ ] `Services/IReportGeneratorService.cs` + `ReportGeneratorService.cs` — lee chunks de DynamoDB por filtro, construye el prompt con todo el contexto y llama a Bedrock (Claude) para generar el informe
- [ ] `Services/IReportStorageService.cs` + `ReportStorageService.cs` — guarda el informe generado (texto o PDF) en S3 bucket `reports` y genera una URL prefirmada de descarga
- [ ] Escenarios predefinidos (plantillas de prompt):
  - `summary` — resumen ejecutivo del corpus completo
  - `risks` — detección de riesgos o inconsistencias entre documentos
  - `compare` — comparativa entre dos documentos específicos (recibe dos `documentId`)
  - `extract` — extracción de datos clave (fechas, nombres, cláusulas)
  - `custom` — el usuario escribe libremente qué quiere analizar
- [ ] Vista en el frontend: selector de escenario + parámetros → botón "Generar informe" → estado `generando...` → botón de descarga cuando esté listo
- [ ] Agregar bucket S3 `reports` en `infra/template.yaml` con política de expiración de objetos (ej. 7 días) para no acumular archivos
- [ ] Permisos IAM: `report-service` necesita `dynamodb:Scan` sobre la tabla `chunks` + `s3:PutObject`/`s3:GetObject` sobre el bucket `reports` + `bedrock:InvokeModel`

---

## Fase 13 — CI/CD (GitHub Actions)

- [ ] `.github/workflows/deploy.yml` — build, test, `sam build` + `sam deploy`
- [ ] `.github/workflows/deploy-frontend.yml` — build de React + sync a S3 + invalidación CloudFront
- [ ] Configurar **OIDC** entre GitHub Actions y AWS (rol IAM asumible, sin access keys estáticas en secrets)
- [ ] Configurar environments en GitHub Actions (dev / prod)

---

## Fase 14 — Deploy y configuración en AWS

- [ ] `sam build && sam deploy --guided` (primer deploy)
- [ ] Verificar conectividad con `GET /health`
- [ ] Probar pipeline de ingesta completo (subir doc → ver chunks en DynamoDB)
- [ ] Probar pipeline de query completo (pregunta → respuesta con fuentes)
- [ ] Deploy del frontend a S3 + CloudFront, verificar la app en el dominio de CloudFront
- [ ] Configurar monitoreo básico con CloudWatch (logs + alarma de errores)
- [ ] Confirmar que el AWS Budget Alert está activo

---

## Checklist de seguridad

- [ ] Nunca commitear credenciales, access keys ni connection strings
- [ ] Usar `dotnet user-secrets` en desarrollo local
- [ ] Usar **Secrets Manager** o **SSM Parameter Store** en producción (no variables de entorno en texto plano para secretos)
- [ ] Validar JWT de Cognito en todos los endpoints excepto `/health`
- [ ] Configurar CORS correctamente en API Gateway (solo el origen de CloudFront)
- [ ] Permisos IAM mínimos por Lambda (least privilege, sin `*` en resources)
- [ ] Bucket S3 `docs` y `frontend` sin acceso público directo (S3 privado + CloudFront con OAC para el frontend)

---

_Stack: .NET 8 · AWS Lambda · API Gateway (HTTP API) · DynamoDB · Amazon Bedrock · S3 · Cognito · CloudWatch · AWS SAM · GitHub Actions (OIDC) · React (Vite) + TypeScript_
