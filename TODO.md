# TODO — Proyecto F: Sistema de Búsqueda Semántica RAG
## C# / .NET 8 · AWS Lambda · API Gateway (HTTP API) · DynamoDB · Google Gemini API · S3 · React (Vite)

> Migrado de Azure a AWS. Arquitectura serverless + microservicios: cada función Lambda
> tiene una sola responsabilidad, desplegada e independiente. **El crédito promocional
> inicial ya se agotó** — solo el tier Always Free (Lambda, DynamoDB, Cognito,
> CloudFront, CloudWatch) sigue siendo $0 permanente; S3 y API Gateway ya se facturan
> desde el primer uso, y Bedrock siempre fue pay-per-token. Ver
> [`docs/architecture.md`](docs/architecture.md#cuenta-aws-y-costos) para el detalle
> por servicio y [`docs/blueprint-csharp.md`](docs/blueprint-csharp.md) para el
> diagrama completo y el razonamiento detrás de cada decisión de servicio.

---

## Fase 0 — Setup de cuenta AWS y del proyecto

- [ ] Crear cuenta AWS (si no existe) y activar MFA en el usuario root
- [ ] Crear/revisar el **AWS Budget Alert** (sin crédito de respaldo ya activo, poner
      el umbral bajo — ej. $5 USD — ya que S3 y API Gateway se facturan desde el primer
      uso). Monitorear por separado el consumo de créditos de Gemini en Google AI
      Studio / Cloud Console (no aparece en el Budget Alert de AWS)
- [ ] Crear usuario IAM con permisos de despliegue (no usar root para trabajar)
- [ ] Instalar y configurar AWS CLI (`aws configure`)
- [ ] Instalar AWS SAM CLI (`sam --version`)
- [ ] Decidir y documentar la región (ej. `us-east-1`)
- [ ] Generar API key de Google Gemini en **tier de pago** (Google AI Studio / Cloud
      Console) contra los $25 USD de créditos comprados — no usar la API key gratuita
      por las garantías de no-entrenamiento del tier de pago
- [ ] Guardar la API key de Gemini en SSM Parameter Store (SecureString)
- [ ] Actualizar `.gitignore` para artefactos de AWS SAM/CDK (`.aws-sam/`, `cdk.out/`)
- [ ] Eliminar/archivar `infra/*.bicep` (quedan como referencia histórica de la versión Azure)
- [ ] Migrar IaC de AWS SAM a **Terraform** — reemplaza `infra/template.yaml` por módulos `main.tf` / `variables.tf` / `outputs.tf`
- [ ] Instalar Terraform CLI y configurar el provider `hashicorp/aws`
- [ ] Configurar backend remoto S3 + tabla DynamoDB para locking del estado de Terraform (`terraform { backend "s3" { ... } }`)

---

## Fase 1 — SemanticSearch.Core (modelos y contratos compartidos)

- [x] `Models/DocumentChunk.cs` — modelo de chunk con texto, índice y wordcount
- [x] `Models/IndexedDocument.cs` — documento indexado con metadata
- [x] Reemplazar `Options/OpenAIOptions.cs` → `Options/GeminiOptions.cs` (API key, modelId de embeddings, modelId de chat, dimensión del vector)
- [x] Reemplazar `Options/SearchOptions.cs` → `Options/DynamoDbOptions.cs` (nombre de tabla, región, `ServiceUrl` opcional para DynamoDB Local/LocalStack)
- [x] Reemplazar `Options/BlobOptions.cs` → `Options/S3Options.cs` (bucket name, región, `ServiceUrl` opcional para LocalStack)
- [x] `Models/ChunkRecord.cs` — modelo del item de DynamoDB (PK `DocumentId`/SK `ChunkId`, embedding como `List<float>`, texto, metadata del doc)
- [x] `.csproj` corregido a `net8.0` (estaba en `net10.0`, no soportado nativamente por Lambda) + paquete `AWSSDK.DynamoDBv2`

---

## Fase 2 — `upload-service` (Lambda)

> **Decisión de diseño:** `POST /upload` ya **no** recibe el archivo directo. API
> Gateway HTTP API + Lambda tienen un límite de payload síncrono de ~6MB, y un PDF de
> 10MB en base64 pesa ~13MB — lo supera. En su lugar, el Lambda solo recibe metadata
> (`filename`, `category`, `contentType`) y devuelve una **URL prefirmada de S3** para
> que el cliente suba el archivo directo a S3 (sin límite práctico de tamaño, sin
> ocupar tiempo de Lambda transfiriendo bytes). La validación de tamaño máximo se
> mueve a `indexer-service` (Fase 3), que sí ve el objeto ya subido en S3.

- [x] Crear proyecto `src/SemanticSearch.Functions.Upload` (`Amazon.Lambda.Core` + `Amazon.Lambda.APIGatewayEvents` + `AWSSDK.S3`)
- [x] Handler `UploadFunction.cs` — recibe `POST /upload` (JSON con metadata), valida `filename`/`category`/extensión permitida, genera `docId` y devuelve `{ docId, filename, status: "pending", uploadUrl }`
- [x] `Services/IS3UploadService.cs` + `S3UploadService.cs` — genera la URL prefirmada de S3 (`GetPreSignedURLAsync`, PUT, TTL 15 min), con soporte de `S3Options.ServiceUrl` para LocalStack (Fase 12)
- [x] Modelos `UploadRequest.cs` / `UploadResponse.cs` en `SemanticSearch.Core.Models` (reusados)
- [x] Validación de extensión permitida (`.pdf`, `.docx` — las que soporta `indexer-service` hasta ahora); la validación de **tamaño máximo** del archivo queda pendiente para `indexer-service` (Fase 3), ya que el Lambda de upload no ve los bytes
- [x] Tests (`tests/SemanticSearch.Functions.Upload.Tests`) — 8 casos: request válido, filename/category faltante, extensión no soportada, JSON inválido
- [x] Se sacaron del `.sln` los proyectos legacy de Azure (`SemanticSearch.Api`, `SemanticSearch.Functions` y sus tests) — dejaron de compilar tras la Fase 1 al perder `OpenAIOptions`/`SearchOptions`/`BlobOptions`; el código queda en disco como referencia histórica, pero fuera del build activo

---

## Fase 3 — `indexer-service` (Lambda, trigger por evento S3)

- [x] Crear proyecto `src/SemanticSearch.Functions.Indexer` (`Amazon.Lambda.S3Events` + `AWSSDK.S3` + `AWSSDK.DynamoDBv2` + `PdfPig` + `DocumentFormat.OpenXml`)
- [x] Handler `IndexerFunction.cs` — recibe `S3Event` (mismo tipo que dispara `s3:ObjectCreated:*` en AWS real); parsea la key con la convención `{category}/{docId}/{filename}` fijada por `upload-service`
- [x] `Services/ChunkerService.cs` — sliding window con overlap (512/64) portado de la versión anterior, devuelve `DocumentChunk` de `SemanticSearch.Core`
- [x] `Services/ITextExtractorService.cs` + `TextExtractorService.cs` — soporte para `.pdf` (**PdfPig**) y `.docx` (**DocumentFormat.OpenXml**), portado de la versión anterior
- [x] `Services/IGeminiEmbeddingService.cs` + `GeminiEmbeddingService.cs` — embeddings batch (`batchEmbedContents`, `taskType=RETRIEVAL_DOCUMENT`) contra la API de Gemini vía `HttpClient` estático (reutilizado entre invocaciones)
- [x] `Services/IDynamoChunkWriter.cs` + `DynamoChunkWriter.cs` — escribe `ChunkRecord` en DynamoDB vía `IDynamoDBContext`, con `OverrideTableName` desde `DynamoDbOptions.TableName`
- [x] `Services/IS3ObjectService.cs` + `S3ObjectService.cs` — descarga el objeto y maneja el movimiento a `failed/` (abstrae el SDK de S3 para que el handler sea mockeable en tests)
- [x] Validación de **tamaño máximo** (10MB, la que quedó pendiente de Fase 2): si el objeto la supera, se mueve a `failed/` sin descargar ni llamar a Gemini
- [x] Manejar errores de indexación (extracción, embeddings, escritura): mover el objeto a `failed/` en S3 en vez de DLQ gestionada (mantiene todo dentro de Always Free); un registro fallido no interrumpe el resto del batch del evento S3
- [x] Confirmado (por diseño): no se referencia ningún recurso de VPC — el Lambda mantiene salida a internet gratis hacia la API de Gemini
- [x] Tests (`tests/SemanticSearch.Functions.Indexer.Tests`) — 12 casos: 7 de `ChunkerService` (incluye propagación de metadata), 4 de `IndexerFunction` (documento válido, objeto sobredimensionado, fallo de extracción, key con formato inesperado), 1 de `GeminiEmbeddingService` (forma del request/response contra un `HttpMessageHandler` falso, sin llamar a la API real)

---

## Fase 4 — Pipeline de consulta RAG (Lambdas)

- [x] Crear proyecto `src/SemanticSearch.Functions.Query`
- [x] Handler `QueryFunction.cs` — recibe `POST /query`, orquesta embed → search → answer
- [x] `Services/IGeminiEmbeddingService.cs` — movido a `SemanticSearch.Core.Services` (compartido con `indexer-service`), embeddea la pregunta con `task_type=RETRIEVAL_QUERY`
- [x] `Services/ISimilaritySearchService.cs` + `SimilaritySearchService.cs` — lee chunks candidatos de DynamoDB (vía `IDynamoChunkReader`, scan completo) y calcula similitud coseno en memoria, retorna top-K
- [x] `Services/IRagAnswerService.cs` + `RagAnswerService.cs` — arma el prompt con el contexto y llama a Gemini (`gemini-2.0-flash`, `generateContent`) para generar la respuesta con fuentes citadas
- [x] Modelos `QueryRequest.cs` / `QueryResponse.cs` / `SourceChunk.cs` en `SemanticSearch.Core.Models`
- [x] Tests (`tests/SemanticSearch.Functions.Query.Tests`) — 9 casos: 3 de `SimilaritySearchService` (ranking por coseno, topK, sin chunks), 2 de `RagAnswerService` (forma del prompt/respuesta contra `HttpMessageHandler` falso, fallback sin candidatos), 4 de `QueryFunction` (request válido, query faltante, JSON inválido, topK forwardeado)
- [ ] Cachear (TTL corto en DynamoDB) preguntas repetidas para evitar re-embeddear y re-generar con Gemini en cada request

---

## Fase 5 — `documents-service` (Lambda)

- [x] Crear proyecto `src/SemanticSearch.Functions.Documents`
- [x] Handler `DocumentsFunction.cs` — `GET /documents` (con paginación `limit`/`offset`), `POST /reindex/{docId}`, `DELETE /documents/{docId}`; enruta por método HTTP + path sobre `APIGatewayHttpApiV2ProxyRequest`
- [x] `Services/IDocumentRegistryService.cs` + `DocumentRegistryService.cs` — no existe tabla `documents` separada: agrupa chunks por `DocumentId` desde un `Scan` de la tabla `chunks` (mismo trade-off que `query-service`); `GetChunksAsync`/`DeleteDocumentAsync` usan `Query`/`BatchWrite` por `DocumentId`
- [x] `Services/IS3DocumentService.cs` + `S3DocumentService.cs` — `TriggerReindexAsync` hace `CopyObject` del documento sobre sí mismo para re-disparar el evento `s3:ObjectCreated` que consume `indexer-service` (no hay invocación directa Lambda→Lambda); `DeleteObjectAsync` borra el objeto original de S3 al eliminar el documento
- [x] `GET /health` — healthcheck simple, vive en la misma función (sin auth; la exclusión del JWT authorizer para esta ruta es tarea de Fase 6/infra)
- [x] Campo `Category` agregado a `ChunkRecord` (Fase 1) y poblado en `IndexerFunction` (Fase 3) — lo necesitaba `documents-service` para reconstruir la key de S3 (`{category}/{docId}/{filename}`) al reindexar/borrar
- [x] Tests (`tests/SemanticSearch.Functions.Documents.Tests`) — 12 casos: 4 de `DocumentRegistryService.GroupAndPaginate` (agrupado, chunk fallido marca doc como failed, orden por fecha, límite/offset), 8 de `DocumentsFunction` (health, listado con paginación default/custom, reindex encontrado/404, delete encontrado/404, ruta desconocida)

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
- [ ] Migrar tests de `SemanticSearch.Api.Tests` a tests por función Lambda (mockear `IAmazonS3`, `IAmazonDynamoDB`, `HttpClient`/`IGeminiEmbeddingService` de Gemini)
- [ ] Tests de `SimilaritySearchService` — ranking correcto por similitud coseno
- [ ] Tests de integración local con `dotnet lambda-test-tool` o invocación directa del handler

---

## Fase 10 — Infraestructura como código (Terraform)

> IaC migrado de AWS SAM y Bicep (Azure) a **Terraform**. Reemplaza `infra/template.yaml` y los archivos `*.bicep`.

- [ ] `infra/main.tf` — recursos principales: Lambdas, API Gateway HTTP API, S3 (docs/frontend/reports), DynamoDB, Cognito User Pool
- [ ] `infra/variables.tf` — variables de entorno y región (`aws_region`, nombres de bucket, tabla DynamoDB, etc.)
- [ ] `infra/outputs.tf` — outputs del despliegue (URL de API Gateway, nombre de distribución CloudFront, ARNs)
- [ ] `infra/backend.tf` — backend remoto S3 + locking con DynamoDB para el estado de Terraform
- [ ] Definir tabla DynamoDB: PK `documentId`, SK `chunkId`, GSI si se necesita listar por fecha
- [ ] Definir bucket S3 `docs` con notificación de evento (`aws_s3_bucket_notification`) hacia `indexer-service`
- [ ] Definir bucket S3 `frontend` + distribución CloudFront con OAC (`aws_cloudfront_distribution`)
- [ ] Definir bucket S3 `reports` con política de expiración de objetos (7 días)
- [ ] Definir API Gateway HTTP API con rutas hacia cada Lambda + JWT Authorizer de Cognito
- [ ] `infra/terraform.tfvars.example` — plantilla de variables (nunca commitear el `.tfvars` real)
- [ ] Permisos IAM mínimos por función (cada Lambda solo accede a lo que necesita — least privilege)
- [ ] Reemplazar `sam build && sam deploy` por `terraform init && terraform plan && terraform apply` en los workflows de CI/CD

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
- [ ] `Services/IReportGeneratorService.cs` + `ReportGeneratorService.cs` — lee chunks de DynamoDB por filtro y llama a Gemini (`gemini-2.0-flash`) para generar el informe; usar map-reduce (resumir por documento y luego combinar) en vez de meter el corpus completo en un solo prompt, para controlar el consumo de créditos
- [ ] `Services/IReportStorageService.cs` + `ReportStorageService.cs` — guarda el informe generado (texto o PDF) en S3 bucket `reports` y genera una URL prefirmada de descarga
- [ ] Escenarios predefinidos (plantillas de prompt):
  - `summary` — resumen ejecutivo del corpus completo
  - `risks` — detección de riesgos o inconsistencias entre documentos
  - `compare` — comparativa entre dos documentos específicos (recibe dos `documentId`)
  - `extract` — extracción de datos clave (fechas, nombres, cláusulas)
  - `custom` — el usuario escribe libremente qué quiere analizar
- [ ] Vista en el frontend: selector de escenario + parámetros → botón "Generar informe" → estado `generando...` → botón de descarga cuando esté listo
- [ ] Agregar bucket S3 `reports` en `infra/template.yaml` con política de expiración de objetos (ej. 7 días) para no acumular archivos
- [ ] Permisos IAM: `report-service` necesita `dynamodb:Scan` sobre la tabla `chunks` + `s3:PutObject`/`s3:GetObject` sobre el bucket `reports` + `ssm:GetParameter` sobre el parámetro de la API key de Gemini (no requiere permisos de Bedrock)

---

## Fase 12 — Entorno local con Docker Compose (sin AWS)

> Réplica local de la topología de microservicios + red interna mientras se espera
> la aprobación para tocar la cuenta de AWS. Usa el **mismo código de Lambda** que se
> despliega después (sin reescribir a ASP.NET Core) y llama a la **API real de Gemini**
> (la IA no se mockea). Diferencias conocidas vs. producción quedan documentadas en
> `docs/architecture.md`.

- [ ] `docker-compose.yml` raíz con una red Docker dedicada (`bridge`) que simula la
      segmentación de red interna entre servicios
- [ ] Contenedor **LocalStack** (Community, gratis) para S3 (`docs`, `reports`, `frontend`) — mismo `IAmazonS3`, solo cambia el endpoint a `localhost`
- [ ] Contenedor **DynamoDB Local** (imagen oficial `amazon/dynamodb-local`) o LocalStack — mismo `IAmazonDynamoDB`, sin cambios de código
- [ ] Un `Dockerfile` por Lambda basado en `public.ecr.aws/lambda/dotnet:8` + **Runtime
      Interface Emulator (RIE)** — expone cada función como HTTP interno
      (`upload-service`, `indexer-service`, `query-service`, `documents-service`, `report-service`)
- [ ] Contenedor **gateway** (Nginx o Traefik) que enruta `/upload`, `/query`,
      `/documents`, `/reports` hacia el RIE de cada Lambda — simula API Gateway
- [ ] Mock de Cognito: contenedor ligero que emite un JWT de prueba fijo, o middleware
      que acepta ese JWT — no intentar emular Cognito real
- [ ] Documentar la diferencia del trigger S3→Indexer: en local, `upload-service` invoca
      directamente el endpoint HTTP de `indexer-service` tras el `PutObject` (en vez del
      evento asíncrono `s3:ObjectCreated:*`, que LocalStack Community no encadena bien
      a Lambda sin la versión Pro)
- [ ] `.env` (no commiteado) con la API key real de Gemini + endpoints de LocalStack/DynamoDB Local, inyectados a cada contenedor
- [ ] Frontend: `npm run dev` apuntando al gateway local en vez de a CloudFront/API Gateway real
- [ ] Script único `docker-compose up` (o `Makefile`/`package.json` script) para levantar toda la topología con un comando
- [ ] Sección "Entorno local (Docker Compose)" en `docs/architecture.md` con diagrama equivalente y lista de diferencias conocidas vs. AWS real

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
- [ ] Usar **Secrets Manager** o **SSM Parameter Store** en producción (no variables de entorno en texto plano para secretos) — incluye la API key de Gemini
- [ ] Validar JWT de Cognito en todos los endpoints excepto `/health`
- [ ] Configurar CORS correctamente en API Gateway (solo el origen de CloudFront)
- [ ] Permisos IAM mínimos por Lambda (least privilege, sin `*` en resources)
- [ ] Bucket S3 `docs` y `frontend` sin acceso público directo (S3 privado + CloudFront con OAC para el frontend)

---

_Stack: .NET 8 · AWS Lambda · API Gateway (HTTP API) · DynamoDB · Google Gemini API · S3 · Cognito · CloudWatch · Terraform · GitHub Actions (OIDC) · React (Vite) + TypeScript_
