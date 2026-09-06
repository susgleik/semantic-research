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

- [x] Crear cuenta AWS — confirmado, cuenta `491024724951` con infra real desplegada
      - [ ] MFA en el usuario root — no verificable por CLI (requiere consola), confirmar a mano
- [ ] **AWS Budget Alert** — no verificable con el usuario IAM `semantic-search-deploy`
      (`budgets:ViewBudget` da `AccessDeniedException`, esperado por least-privilege).
      Confirmar a mano desde la consola con el usuario root/admin que el umbral bajo
      (~$5 USD) sigue activo
- [x] Usuario IAM de despliegue creado y en uso — `semantic-search-deploy` (confirmado con `aws iam get-user`)
- [x] AWS CLI instalado y configurado (usado en toda la sesión)
- [x] SAM CLI instalado — usado en Fase 12 (`sam build`/`sam local start-api`)
- [x] Región documentada — `us-east-1` (`infra/terraform.tfvars.example`, `CLAUDE.md`, este archivo)
- [x] API key de Gemini generada y en uso (no verificable por CLI si es tier de pago vs.
      gratuito, pero la app funciona con respuestas reales de Gemini en producción)
- [x] API key de Gemini en SSM Parameter Store — confirmado (`/semantic-search/gemini-api-key`, tipo `SecureString`)
- [x] `.gitignore` con `.aws-sam/` (`cdk.out/` no aplica — nunca se usó CDK, se migró SAM → Terraform directo)
- [x] `infra/*.bicep` archivados en `infra/_legacy-azure/` — confirmado
- [x] IaC migrada de AWS SAM a Terraform — completo desde Fase 10
- [x] Terraform CLI + provider `hashicorp/aws` configurado y en uso
- [x] Backend remoto S3 + DynamoDB lock para el state de Terraform — confirmado (Fase 10)

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
- [x] Cache de preguntas repetidas — nueva tabla DynamoDB `query-cache` (`infra/dynamodb.tf`,
      PK `QueryHash`, TTL nativo sobre `ExpiresAt` como limpieza de respaldo async) +
      `Services/IQueryCacheService.cs`/`QueryCacheService.cs` en `SemanticSearch.Functions.Query`:
      hashea `query` normalizado (trim + lowercase) + `topK` con SHA-256, y en cada
      lectura valida `ExpiresAt` contra la hora actual en código (el TTL nativo de
      DynamoDB no expira al instante, no alcanza para un TTL corto tipo 10 min). Si hay
      hit, `QueryFunction` devuelve la respuesta cacheada sin llamar a
      `IGeminiEmbeddingService` ni `IRagAnswerService`; si hay miss, genera la respuesta
      normal y la guarda con `SetAsync`. TTL configurable por env var
      `QUERY_CACHE_TTL_SECONDS` (default 600s = 10 min). Permisos IAM mínimos agregados
      en `lambda.tf` (`GetItem`/`PutItem`/`DescribeTable` solo sobre `query-cache`).
      Tests: 3 de `QueryCacheServiceTests` (normalización de hash, distinto `topK` es
      miss, entrada expirada devuelve null) + 2 de `QueryFunctionTests` (hit no llama a
      Gemini, miss genera y persiste)
- [x] **Bug real encontrado probando `/query` desde el frontend en AWS real** (vía
      integración MCP, Fase 8): Gemini devolvía `503 UNAVAILABLE` ("high demand") en
      `generateContent`, y como nada lo capturaba la excepción subía sin manejar →
      Lambda 500. Fix: `SemanticSearch.Core/Services/GeminiRetryPolicy.cs`, retry con
      backoff corto (1s, 2s — 3 intentos en total) ante `503`/`429` de Gemini, sin
      tocar nada ante errores no transitorios (`400`, etc. — reintentar eso no arregla
      nada). Centralizado en Core porque los 3 servicios que llaman a Gemini
      (`GeminiEmbeddingService`, `RagAnswerService` acá, `ReportChatService` en
      Fase 11) tenían el mismo problema exacto — mismo fix aplicado a los 3. Tests:
      `GeminiRetryPolicyTests.cs` (reintenta y termina en éxito, no reintenta ante
      error no transitorio, se rinde después de agotar los reintentos)

---

## Fase 5 — `documents-service` (Lambda)

- [x] Crear proyecto `src/SemanticSearch.Functions.Documents`
- [x] Handler `DocumentsFunction.cs` — `GET /documents` (con paginación `limit`/`offset`), `POST /reindex/{docId}`, `DELETE /documents/{docId}`; enruta por método HTTP + path sobre `APIGatewayHttpApiV2ProxyRequest`
- [x] `Services/IDocumentRegistryService.cs` + `DocumentRegistryService.cs` — no existe tabla `documents` separada: agrupa chunks por `DocumentId` desde un `Scan` de la tabla `chunks` (mismo trade-off que `query-service`); `GetChunksAsync`/`DeleteDocumentAsync` usan `Query`/`BatchWrite` por `DocumentId`
- [x] `Services/IS3DocumentService.cs` + `S3DocumentService.cs` — `TriggerReindexAsync` hace `CopyObject` del documento sobre sí mismo para re-disparar el evento `s3:ObjectCreated` que consume `indexer-service` (no hay invocación directa Lambda→Lambda); `DeleteObjectAsync` borra el objeto original de S3 al eliminar el documento
- [x] `GET /health` — healthcheck simple, vive en la misma función (sin auth; la exclusión del JWT authorizer para esta ruta es tarea de Fase 6/infra)
- [x] Campo `Category` agregado a `ChunkRecord` (Fase 1) y poblado en `IndexerFunction` (Fase 3) — lo necesitaba `documents-service` para reconstruir la key de S3 (`{category}/{docId}/{filename}`) al reindexar/borrar
- [x] Tests (`tests/SemanticSearch.Functions.Documents.Tests`) — 12 casos: 4 de `DocumentRegistryService.GroupAndPaginate` (agrupado, chunk fallido marca doc como failed, orden por fecha, límite/offset), 8 de `DocumentsFunction` (health, listado con paginación default/custom, reindex encontrado/404, delete encontrado/404, ruta desconocida)
- [x] Bug real encontrado probando el botón "Reindexar" desde el frontend contra AWS
      real: S3 rechaza un `CopyObject` sobre la misma key si no cambia metadata
      (`AmazonS3Exception: illegal because it is trying to copy an object to itself`);
      como `HandleReindexAsync` no lo capturaba, devolvía 500 al frontend. Fix: agregado
      `MetadataDirective = S3MetadataDirective.REPLACE` en `S3DocumentService.TriggerReindexAsync`
      (confirmado con logs de CloudWatch antes/después del fix, desplegado vía CI/CD)

---

## Fase 6 — Auth (Amazon Cognito)

- [x] Crear User Pool de Cognito + App Client — `infra/cognito.tf` (Fase 10): `aws_cognito_user_pool` + `aws_cognito_user_pool_client` (SPA pública, sin secret) + `aws_cognito_user_pool_domain` (Hosted UI)
- [x] Configurar **JWT Authorizer** nativo de API Gateway HTTP API contra el User Pool (sin código de validación manual) — `infra/apigateway.tf`, `aws_apigatewayv2_authorizer` tipo `JWT`
- [x] Excluir `/health` del authorizer — única ruta con `authorization_type = "NONE"` en `infra/apigateway.tf`
- [x] Login del frontend con **`react-oidc-context` + `oidc-client-ts`** (Authorization
      Code flow contra el Hosted UI de Cognito, ya habilitado en `infra/cognito.tf`
      con `allowed_oauth_flows = ["code"]` y scopes `openid email profile`) —
      `frontend/src/auth/config.ts` arma el `authority` OIDC a partir de
      `VITE_COGNITO_USER_POOL_ID` (discovery document expone el `authorization_endpoint`
      del Hosted UI automáticamente) y `cognitoLogoutUrl()` arma a mano la URL de
      `/logout` del dominio (Cognito no implementa RP-initiated logout estándar en
      Hosted UI classic). `App.tsx` (`AuthGate`) gatea las rutas: sin sesión muestra
      botón "Iniciar sesión" (`signinRedirect`), con sesión inyecta el `access_token`
      en `api/client.ts` (`setAuthToken`, header `Authorization: Bearer`) vía `useEffect`
      y muestra el email + botón "Cerrar sesión". Diseñado para degradar sin romper: si
      `VITE_COGNITO_USER_POOL_ID`/`_CLIENT_ID`/`_DOMAIN` están vacías (como en `.env`
      local de Fase 12, que todavía no valida JWT), `authEnabled` da `false`, no se
      monta `AuthProvider` y la app funciona sin login — evita levantar un mock de
      Cognito solo para desarrollo local
- [x] Login probado end-to-end contra el User Pool real (`signinRedirect` → Hosted UI →
      callback a `localhost:5173` con sesión activa, `Authorization: Bearer` llegando a
      la API real). Usuario de prueba creado con `admin-create-user` + `admin-set-user-password --permanent`
      (evita el flujo de verificación por email para pruebas manuales)

---

## Fase 7 — Frontend (React SPA)

- [x] Scaffold `frontend/` con Vite + React + TypeScript
- [x] Cliente HTTP (`src/api/client.ts`) apuntando a la URL de API Gateway
- [x] Vista: subir documento → `upload-service` (`UploadPage.tsx`)
- [x] Vista: lista de documentos + estado de indexado → `documents-service` (`DocumentsPage.tsx`)
- [x] Vista: buscador/chat de preguntas → `query-service`, mostrando respuesta + fuentes citadas (`QueryPage.tsx`)
- [x] Vista/acción: botón reindexar documento (`DocumentsPage.tsx`)
- [x] Integración de login con Cognito (`react-oidc-context`) — ver detalle en Fase 6
- [x] Variables de entorno (`.env`) para URL de API y datos de Cognito (`.env`/`.env.example`, gitignored el real)
- [x] Build de producción (`npm run build`, modo Vite `production` → `.env.production`)
      y deploy a S3 (bucket `frontend`) + invalidación de CloudFront — script
      `infra/scripts/deploy-frontend.ps1` (build → `aws s3 sync --delete` →
      `aws cloudfront create-invalidation`). **Verificado en real**:
      `https://dv3okb4rzqrhb.cloudfront.net/` responde `200`, login contra Cognito
      funciona (el callback URL de CloudFront ya está registrado en el App Client vía
      `cognito.tf`). Pendiente: automatizar en `deploy-frontend.yml` (Fase 13) — hoy
      es manual

---

## Fase 8 — SemanticSearch.McpServer

> **Hallazgo real al retomar esta fase:** lo que estaba tildado como hecho (`Program.cs`
> + las 3 tools) en realidad **no funcionaba como servidor MCP** — no había ningún
> paquete `ModelContextProtocol` referenciado, `Program.cs` solo levantaba un `Host`
> genérico sin transporte stdio ni JSON-RPC, y las tools eran clases sueltas
> (`Name`/`Description`/`ExecuteAsync(JsonElement)`) que nadie invocaba. "Apuntar la
> URL" no alcanzaba para que esto funcionara con Copilot Chat.

- [x] Agregado el SDK oficial **`ModelContextProtocol`** (NuGet, v1.4.1) — `Program.cs`
      reescrito con `Host.CreateApplicationBuilder` + `AddMcpServer().WithStdioServerTransport()`;
      logs redirigidos a stderr (`LogToStandardErrorThreshold`), porque stdout está
      reservado para los mensajes JSON-RPC del protocolo — si los logs se mezclan ahí,
      Copilot Chat no puede parsear la salida
- [x] Las 3 tools reescritas con `[McpServerToolType]`/`[McpServerTool]` + parámetros
      tipados con `[Description]` (en vez de `JsonElement` parseado a mano), registradas
      vía `WithTools<T>()`. De paso, un bug real que tenía `SearchDocumentsTool`: mandaba
      `top_k` (snake_case) en el body, pero `QueryFunction` deserializa con
      `JsonSerializerDefaults.Web` (camelCase) — el parámetro nunca llegaba, `topK`
      siempre caía al default de 5 sin importar lo que pidiera la tool
- [x] Apuntar las tools a la URL real de API Gateway — `.vscode/settings.json`,
      `API_URL` ahora apunta a `https://is1exk1is3.execute-api.us-east-1.amazonaws.com`
      (antes tenía `http://localhost:8080`, resabio de Container Apps/local viejo)
- [x] Agregado soporte de auth — todas las rutas exigen JWT de Cognito salvo `/health`
      (Fase 6), así que sin esto cada llamada de una tool devolvía 401. Nueva env var
      `MCP_ACCESS_TOKEN` (Bearer, adjuntado por `Program.cs` a los 3 `HttpClient`
      tipados); se consigue a mano con `aws cognito-idp admin-initiate-auth` (mismo
      patrón que el usuario de prueba creado en Fase 10) — **no se refresca solo**,
      expira a la hora, limitación conocida y documentada, fuera de alcance para esta
      sesión
- [x] `.vscode/settings.json` con `github.copilot.chat.mcpServers` — ya existía pero
      con la URL vieja; actualizado
- [x] **Probado con un smoke test real por stdio** (sin Copilot Chat, invocando el
      binario compilado directo): handshake `initialize` responde bien, `tools/list`
      devuelve las 3 tools con su JSON schema correcto (`search_documents` con
      `query`/`topK`, `list_documents` con `limit`/`offset`, `reindex_document` con
      `docId`), stdout queda limpio (solo JSON-RPC)
- [x] **Probado de verdad con Copilot Chat en VS Code 1.129** — dos hallazgos reales:
  - El setting `github.copilot.chat.mcpServers` en `.vscode/settings.json` **no existe
    en VS Code moderno** (era una key experimental vieja) — el MCP nativo del editor se
    configura con `.vscode/mcp.json` (`servers` + `inputs` para secretos con prompt
    seguro, en vez de guardar el token en texto plano). Se migró a ese formato.
  - `tools/call` de `list_documents` tiraba `System.Net.Http.HttpRequestException` /
    `InvalidOperationException: ... BaseAddress must be set` — el SDK de MCP construye
    las instancias de las tools sin pasar por el mecanismo de *typed client* de
    `AddHttpClient<T>()` (usa `ActivatorUtilities` directo), así que terminaba
    inyectando un `HttpClient` default sin configurar. Fix: un único `HttpClient`
    configurado a mano y registrado como singleton (`AddSingleton(apiHttpClient)`) en
    vez de 3 typed clients — sin ambigüedad posible en cómo se resuelve el parámetro
  - **De paso, este mismo debugging destapó un bug real en `/query` en producción**
    (ver Fase 4: 503 de Gemini sin manejar → 500)

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

> IaC migrado de AWS SAM y Bicep (Azure) a **Terraform**. Reemplaza `infra/template.yaml` y los archivos `*.bicep`
> (archivados en `infra/_legacy-azure/`, Fase 0). Archivos planos en `infra/` (Terraform
> carga todos los `.tf` de un directorio juntos, no hace falta un solo `main.tf`
> monolítico). Ver `docs/terraform-setup.md` para el flujo completo de bootstrap/deploy.

- [x] Backend remoto S3 (`semantic-search-tfstate-<account_id>`) + tabla DynamoDB de
      lock (`semantic-search-tfstate-lock`) — bootstrapeados una sola vez con AWS CLI
      (no con Terraform, problema de huevo-y-gallina), documentado en `docs/terraform-setup.md`
- [x] `infra/providers.tf` + `infra/backend.tf` — provider `hashicorp/aws` ~>5.0, backend `s3`
- [x] `infra/variables.tf` — `aws_region`, `project_prefix`, `gemini_ssm_parameter_name`, capacidad de DynamoDB, orígenes CORS, callback/logout URLs de Cognito
- [x] `infra/dynamodb.tf` — tabla `chunks`: hash key `DocumentId`, range key `ChunkId` (coincide con `ChunkRecord.cs`), `PROVISIONED` 5/5 RCU-WCU (dentro del Always Free de 25/25)
- [x] `infra/s3.tf` — buckets `docs`/`reports`/`frontend` (nombre con account id para unicidad global), block public access en los 3, CORS en `docs`/`reports`, lifecycle de 7 días en `reports`, notificación `docs → indexer-service` (todo el bucket; el guard de código contra `failed/` evita el loop, ver abajo)
- [x] `infra/cloudfront.tf` — distribución + `aws_cloudfront_origin_access_control` sobre el bucket `frontend`, error responses 403/404 → `/index.html` (SPA routing)
- [x] `infra/cognito.tf` — User Pool + App Client (SPA, sin secret) + Hosted UI domain (cierra la parte de infra de Fase 6)
- [x] `infra/apigateway.tf` — HTTP API, `aws_apigatewayv2_authorizer` JWT contra Cognito, rutas de `docs/architecture.md` (todas con authorizer excepto `GET /health`), integraciones `AWS_PROXY` + permisos de invocación
- [x] `infra/lambda.tf` — 5 `aws_lambda_function` (runtime `dotnet8`, handlers iguales a `template.local.yaml`) + un `aws_iam_role`/policy inline **least-privilege** por función (tabla de permisos en `docs/terraform-setup.md`/plan de Fase 10)
- [x] `infra/outputs.tf` — URL de API Gateway, dominio CloudFront, IDs de Cognito, nombres de buckets/tabla
- [x] `infra/terraform.tfvars.example` — plantilla de variables (el `.tfvars` real gitignored)
- [x] `infra/scripts/build-lambdas.ps1` — `dotnet publish` + zip de los 5 Lambdas a `infra/publish/*.zip` (gitignored), referenciados por `source_code_hash` en `lambda.tf`; correr antes de cada `plan`/`apply` que cambie código
- [x] Permisos IAM mínimos por función — ver tabla en `docs/terraform-setup.md` (cada Lambda solo con los verbos/recursos que usa, sin `*`)
- [x] `terraform fmt`/`validate`/`init`/`plan`/**`apply`** corridos — **57 recursos creados en AWS real** (`us-east-1`), `terraform plan` posterior confirma 0 drift. `GET /health` responde `200`, rutas con JWT devuelven `401` sin token (comportamiento esperado)
- [x] Fix real encontrado en el primer `apply`: los 5 Lambdas fallaban en cold start (`Runtime.ExitError` — falta `.runtimeconfig.json` en el zip). Causa: son proyectos de librería de clases (sin `Main`), y `dotnet publish` solo genera ese archivo para `OutputType=Exe` a menos que se fuerce. Se agregó `<GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>` a los 5 `.csproj` de Functions.*; re-publicado y re-aplicado, confirmado con `/health` en `200`
- [x] **Pipeline completo probado en AWS real** (usuario de prueba creado a mano en Cognito vía `admin-create-user`/`admin-initiate-auth`, no vía frontend): `POST /upload` → PUT a S3 → `indexer-service` dispara solo por el evento S3 → 2 chunks indexados en DynamoDB con embeddings de Gemini → `POST /query` devuelve respuesta con fuentes citadas. Dos bugs reales más encontrados y arreglados en el camino, invisibles en local:
  - Faltaba `dynamodb:DescribeTable` en las 4 policies IAM que usan `IDynamoDBContext` (Indexer/Query/Documents/Reports) — el SDK de alto nivel lo llama internamente antes de leer/escribir, no solo `PutItem`/`Scan`/etc
  - `gemini-2.5-flash` pasó a "no disponible para usuarios nuevos" del lado de Google — se cambió el default a `gemini-flash-latest` (alias que sigue siempre al Flash estable vigente) en `GeminiOptions.cs`, los 3 `LoadGeminiOptions()`, `infra/lambda.tf` y `template.local.yaml`
- [x] Reemplazar `sam build && sam deploy` por Terraform en los workflows de CI/CD —
      hecho en Fase 13 (`deploy.yml` corre `terraform plan`/`apply`), esta línea quedó desactualizada
- [x] **Cambios de código descubiertos al diseñar el Terraform** (necesarios para desplegar de forma segura/correcta en AWS real, no específicos de Terraform):
  - `GEMINI_API_KEY` ya no se lee como variable de entorno plana en Indexer/Query/Reports — nuevo `SemanticSearch.Core/Options/GeminiSecretLoader.cs` la busca en SSM (`GEMINI_API_KEY_SSM_PARAM`, `WithDecryption`) y la cachea por contenedor; en local (sin esa variable) cae al `GEMINI_API_KEY` de siempre, no rompe Fase 12
  - Guard de una línea en `IndexerFunction.ProcessRecordAsync`: ignora eventos S3 sobre keys que ya empiezan con `failed/` — sin esto, la notificación S3→Lambda (necesaria sobre todo el bucket `docs`, las categorías las elige el usuario) reprocesaría en loop cada objeto movido a `failed/` por un fallo previo (invisible en local porque ahí `indexer-service` se invoca a mano, nunca automático)

---

## Fase 11 — `report-service` (Lambda)

El usuario elige un escenario de análisis y el sistema genera un informe basado
en el corpus completo de documentos indexados — diferente al chat RAG donde se
hace una pregunta puntual. El informe se guarda en S3 y queda disponible para
descarga.

- [x] Crear proyecto `src/SemanticSearch.Functions.Reports`
- [x] Handler `ReportFunction.cs` — `POST /reports` (recibe escenario + parámetros) y `GET /reports/{reportId}` (descarga el informe generado); ambos endpoints reusan `IReportStorageService.GetDownloadUrlAsync` para (re)generar la URL prefirmada — no hay un paso `generating` real: la generación es síncrona dentro del propio `POST /reports` (por eso el `ReportsFunction` tiene `Timeout: 120` en vez de los 30s default de `Globals`, ver `template.local.yaml`)
- [x] `Models/ReportRequest.cs` — escenario elegido + parámetros opcionales (`category`, `documentIds`, `instruction`, `dateFrom`/`dateTo`) en `SemanticSearch.Core.Models`
- [x] `Models/ReportResponse.cs` — `reportId`, `status` (`ready`), `downloadUrl` en `SemanticSearch.Core.Models`; `Models/ReportScenarios.cs` centraliza los 5 escenarios válidos para que el handler los valide
- [x] `Services/IReportGeneratorService.cs` + `ReportGeneratorService.cs` — filtra chunks (categoría/documentIds/rango de fechas, método estático `FilterChunks` testeable sin SDK) y hace **map-reduce** con Gemini vía `IReportChatService`: 1 llamada por documento (map) + 1 de combinación final (reduce), en vez de un solo prompt con el corpus completo
- [x] `Services/IReportChatService.cs` + `ReportChatService.cs` — wrapper de `generateContent` de Gemini (mismo patrón que `RagAnswerService` de `query-service`, pero genérico para prompts de reporte)
- [x] `Services/IReportChunkReader.cs` + `ReportChunkReader.cs` — `Scan` completo de la tabla `chunks` (mismo trade-off ya documentado en `query-service`/`documents-service`)
- [x] `Services/IReportStorageService.cs` + `ReportStorageService.cs` — guarda el informe generado (Markdown) en S3 bucket `reports` (`{reportId}.md`) y genera una URL prefirmada de descarga (15 min TTL); devuelve `null` si el objeto no existe (usado para el 404 de `GET /reports/{reportId}`)
- [x] Escenarios predefinidos (plantillas de prompt húmedas en `ReportGeneratorService`, distintas para el paso map y el paso reduce):
  - `summary` — resumen ejecutivo del corpus completo
  - `risks` — detección de riesgos o inconsistencias entre documentos
  - `compare` — comparativa entre dos documentos específicos (recibe dos `documentId`, validado en el handler)
  - `extract` — extracción de datos clave (fechas, nombres, cláusulas)
  - `custom` — el usuario escribe libremente qué quiere analizar (`instruction` obligatorio, validado en el handler)
- [x] Vista en el frontend: selector de escenario (tarjetas) + parámetros → botón
      "Generar informe" → preview del markdown renderizado en la página (`react-markdown`
      + `remark-gfm`, fetch del `downloadUrl`) → botones "Descargar .md" y "Descargar PDF"
      (PDF vía `window.print()` en una ventana con estilos propios, sin librería pesada
      ni endpoint nuevo). Historial local (`localStorage`, últimos 10) con botón "Ver"
      que llama `GET /reports/{reportId}` para refrescar la URL firmada (expira a los 15 min)
- [x] Bucket S3 `reports` — ya lo crea el contenedor `setup` de `docker-compose.yml` (Fase 12); la política de expiración de objetos (7 días) y el bucket de producción quedan para Terraform (Fase 10), ya que no existe `infra/template.yaml` de AWS real en este repo (solo `template.local.yaml` para el entorno local)
- [x] Lambda `ReportsFunction` agregada a `template.local.yaml` (`POST /reports`, `GET /reports/{reportId}`) + `S3_BUCKET_REPORTS` en `Globals.Function.Environment.Variables`
- [x] Tests (`tests/SemanticSearch.Functions.Reports.Tests`) — 18 casos: 5 de `ReportGeneratorService.FilterChunks`/`GenerateReportAsync` (filtros por categoría/documentIds/fecha, map-reduce con N+1 llamadas, orden de chunks dentro de un documento), 3 de `ReportChatService` (forma del request/response y error body contra `HttpMessageHandler` falso), 10 de `ReportFunction` (creación válida, escenario faltante/desconocido, `compare` sin 2 `documentIds`, `custom` sin `instruction`, JSON inválido, GET encontrado/404, ruta desconocida)
- [x] Permisos IAM de `report-service` — confirmado en `infra/lambda.tf`
      (`aws_iam_role_policy.reports`): `dynamodb:Scan`+`DescribeTable` sobre `chunks`,
      `s3:PutObject`/`GetObject` sobre `reports`, `ssm:GetParameter` sobre la API key de Gemini

---

## Fase 12 — Entorno local con Docker Compose + SAM CLI (sin AWS)

> Réplica local de la topología de microservicios + red interna mientras se espera
> la aprobación para tocar la cuenta de AWS. Usa el **mismo código de Lambda** que se
> despliega después (sin reescribir a ASP.NET Core) y llama a la **API real de Gemini**
> (la IA no se mockea). **Probado end-to-end** (upload → indexer → query → documents),
> con respuesta real de Gemini citando la fuente correcta. Referencia completa de
> comandos, configuración y problemas encontrados en
> [`docs/local-development.md`](docs/local-development.md).

> **Cambio de diseño respecto al plan original:** en vez de un gateway custom
> (Nginx/Traefik) traduciendo HTTP → evento Lambda a mano, se usa **AWS SAM CLI**
> (`sam local start-api` / `sam local invoke`), la herramienta oficial de AWS para
> esto — ya iba a instalarse para el deploy real (Fase 0), evita reimplementar lo que
> hace API Gateway, y los contenedores RIE (`public.ecr.aws/lambda/dotnet:8-rapid`)
> los maneja SAM automáticamente vía `sam build`, sin necesidad de un `Dockerfile`
> propio por función.

- [x] `docker-compose.yml` raíz con red Docker dedicada (`semantic-search-net`) — LocalStack (S3) + DynamoDB Local + un contenedor `setup` que crea el bucket `docs`/`reports` y la tabla `chunks` al levantar
- [x] LocalStack pineado a `3.8` (no `latest` — la versión `latest` actual intenta activar licencia Pro y falla sin `LOCALSTACK_AUTH_TOKEN`) con `LOCALSTACK_AUTH_TOKEN: ""` explícito
- [x] DynamoDB Local corre con `-inMemory` (no con volumen persistente — la imagen corre como usuario no-root y no puede escribir en un volumen nombrado de Docker Desktop/Windows; sin persistencia entre reinicios, igual que LocalStack para desarrollo)
- [x] `template.local.yaml` — define los 4 Lambdas HTTP (`UploadFunction`, `QueryFunction`, `DocumentsFunction`) + `IndexerFunction` sin ruta HTTP (se invoca manual, ver abajo), con `Globals.Function.Environment.Variables` apuntando a `http://localstack:4566` / `http://dynamodb-local:8000` (nombres de contenedor, no `localhost`)
- [x] `env.local.example.json` (commiteado) + `env.local.json` (gitignored) con `GEMINI_API_KEY` por función — **requiere un placeholder `GEMINI_API_KEY: ""` en `template.local.yaml`**, ya que `sam local --env-vars` solo sobrescribe variables que ya existen en el template, no inyecta nombres nuevos
- [x] `sam build --template template.local.yaml` empaqueta las 4 Lambdas contra el runtime `dotnet8`
- [x] `sam local start-api --template .aws-sam/build/template.yaml --docker-network semantic-search-net --env-vars env.local.json` — **ojo:** el `--template` debe apuntar al template ya compilado (`.aws-sam/build/template.yaml`), no al fuente (`template.local.yaml`), o monta el código sin publicar y tira "missing .deps.json"
- [x] `events/s3-put-event.json` + `events/query-event.json` + `events/documents-list-event.json` — eventos de ejemplo para invocar cada Lambda a mano
- [x] Confirmado el flujo de upload: `POST /upload` → URL prefirmada de S3 con host `localstack` (nombre interno de red) → subir con `curl.exe --resolve localstack:4566:127.0.0.1 -k` desde el host (fuera de la red Docker, `localstack` no resuelve por DNS y el cliente S3 firma en `https://` aunque LocalStack sirve HTTP plano)
- [x] `indexer-service` se invoca manual con `sam local invoke IndexerFunction --event events/s3-put-event.json` (upload-service **no** la invoca automáticamente en local — la key/tamaño del evento hay que actualizarlos a mano por cada prueba)
- [x] **Bugs reales encontrados y corregidos durante la prueba end-to-end** (no específicos de Docker, afectaban también a producción):
  - `text-embedding-004` no está disponible para la cuenta de Gemini en uso → default cambiado a `gemini-embedding-001` en `GeminiOptions.cs`, `IndexerFunction.cs`, `QueryFunction.cs`
  - `gemini-2.0-flash` fue dado de baja por Google ("no longer available") → default cambiado a `gemini-2.5-flash`
  - `GeminiEmbeddingService`/`RagAnswerService` solo hacían `EnsureSuccessStatusCode()` sin capturar el body del error — se agregó captura del body de Gemini en la excepción (crítico para poder diagnosticar los dos puntos anteriores)
  - `ChunkRecord.Embedding` (`List<float>`) se guardaba en DynamoDB como **Number Set (NS)** por el converter default del SDK — los Sets no garantizan orden y descartan valores duplicados, corrompiendo el vector silenciosamente. Se agregó `FloatListConverter : IPropertyConverter` + `[DynamoDBProperty(typeof(FloatListConverter))]` para forzar almacenamiento como **List (L)** ordenada
- [ ] Mock de Cognito para probar rutas con auth **en local** — sigue pendiente: las
      rutas reales en AWS ya exigen JWT (Fase 6 resuelta), pero el entorno local
      (`sam local start-api`) no valida token, así que no se puede probar el flujo de
      auth completo sin pegarle a AWS real (`npm run dev:cloud`)
- [x] Frontend `npm run dev` apuntando al gateway local — resuelto vía `.env.development`
      (modo dev de Vite, Fase 7), sin tocar nada a mano
- [x] Sección "Entorno local (Docker Compose)" en `docs/architecture.md` — **desactualizada**: describe el gateway Nginx/Traefik del plan original, hay que reescribirla para reflejar SAM CLI

---

## Fase 13 — CI/CD (GitHub Actions)

- [x] Se eliminaron los workflows viejos de Azure (`deploy-api.yml`,
      `deploy-functions.yml`) — referenciaban `SemanticSearch.Api`/`SemanticSearch.Functions`
      (monolítico), sacados del `.sln` desde Fase 1/2, y Azure Container Apps/Functions
- [x] `.github/workflows/ci.yml` — `dotnet build`/`test` de `SemanticSearch.sln` en cada
      push/PR de cualquier rama, sin credenciales AWS (no las necesita)
- [x] `.github/workflows/deploy.yml` — en push a `main`: job `build-and-plan` (build,
      test, `infra/scripts/build-lambdas.ps1` vía `shell: pwsh`, `terraform plan`) +
      job `apply` que corre `terraform apply` gateado por el Environment `production`
      de GitHub (aprobación manual, ver `docs/terraform-setup.md#6`). Reemplaza al
      `sam build && sam deploy` original (ya migrado a Terraform en Fase 10)
- [ ] `deploy-frontend.yml` — automatizar en CI/CD lo que hoy es manual (Fase 7):
      `infra/scripts/deploy-frontend.ps1` ya hace build + `s3 sync` + invalidación de
      CloudFront y quedó probado corriéndolo a mano; falta engancharlo a un workflow
      de GitHub Actions (mismo patrón OIDC que `deploy.yml`) para que corra solo en
      push a `main`
    - Bug real confirmado 2026-07-21: `deploy.yml` mergeó y deployó un fix de
      `frontend/src/App.tsx` (logout no limpiaba la sesión OIDC) sin tocar el
      frontend en absoluto — el bundle servido por CloudFront quedó desactualizado
      hasta correr `deploy-frontend.ps1` a mano. Sin este workflow, todo cambio de
      frontend que se mergea a `main` queda silenciosamente sin desplegar.
- [x] Configurar **OIDC** entre GitHub Actions y AWS — `infra/oidc.tf`:
      `aws_iam_openid_connect_provider` + rol `semantic-search-github-actions-deploy`
      con trust policy limitada a `repo:susgleik/semantic-research` en `main` (ni PRs
      ni otras ramas pueden asumirlo); aplicado en AWS real (`github_actions_role_arn`
      en outputs)
- [x] **Pipeline de CI/CD probado de punta a punta en GitHub real** (push a `main` vía
      PR → `build-and-plan` automático → aprobación manual del reviewer → `terraform
      apply` real desde el runner de GitHub, sin ninguna credencial guardada en el
      repo) — `GET /health` sigue en `200` después del deploy hecho por CI. Tres bugs
      reales de configuración encontrados y arreglados en el camino:
  - Faltaba `iam:GetOpenIDConnectProvider` (+ create/delete/tag/list) en la policy del
    rol de CI — Terraform necesita leer su propio `aws_iam_openid_connect_provider`
    en cada plan, no solo los recursos de la app
  - El trust policy solo aceptaba el `sub` claim de rama
    (`repo:...:ref:refs/heads/main`); el job `apply` usa `environment: production`, y
    GitHub manda un `sub` distinto en ese caso (`repo:...:environment:production`) —
    se agregaron ambos patrones a la condición
  - `actions/upload-artifact` le saca el directorio común (`infra/`) a los paths
    subidos (`infra/tfplan` + `infra/publish/*.zip` → guardados como `tfplan` +
    `publish/*.zip`); el `download-artifact` del job `apply` bajaba a la raíz del
    workspace en vez de `infra/`, y `terraform apply tfplan` no encontraba el archivo
- [x] Configurar environments en GitHub Actions — `production` con reviewer requerido
      antes del `apply` (setup manual documentado, no se puede hacer con Terraform);
      no se armó un environment `dev` separado porque no hay una segunda cuenta/stack
      AWS de desarrollo en este proyecto académico
- [ ] Setup manual pendiente del lado del usuario en la UI de GitHub (documentado en
      `docs/terraform-setup.md#6`): variable `AWS_DEPLOY_ROLE_ARN` + Environment
      `production` con required reviewer

---

## Fase 14 — Deploy y configuración en AWS

- [x] ~~`sam build && sam deploy --guided`~~ — obsoleto, reemplazado por
      `terraform apply` vía CI/CD desde Fase 10/13
- [x] Verificar conectividad con `GET /health` — confirmado en `200` post-deploy
- [x] Probar pipeline de ingesta completo (subir doc → ver chunks en DynamoDB) — Fase 10
- [x] Probar pipeline de query completo (pregunta → respuesta con fuentes) — Fase 10
- [x] Deploy del frontend a S3 + CloudFront — Fase 7, `https://dv3okb4rzqrhb.cloudfront.net/` en `200`
- [x] Configurar monitoreo básico con CloudWatch — `infra/cloudwatch.tf`: una
      `aws_cloudwatch_metric_alarm` por Lambda (`Errors` ≥1 en 5 min, `for_each` sobre
      las 5 funciones), dentro del Always Free (10 alarm metrics permanentes). Topic
      SNS + suscripción por email opcional vía `var.alarm_email` (vacío por defecto —
      no crea SNS, las alarmas quedan igual visibles en la consola). **Aplicado y
      verificado en AWS real**: las 5 alarmas existen en estado `OK`
      (`aws cloudwatch describe-alarms`). **Pendiente**: setear `alarm_email` en el
      `.tfvars` real y confirmar la suscripción por email tras el próximo `apply` si se
      quiere notificación activa (hoy nadie la seteó)
      - Dos bugs reales de CI/CD encontrados y arreglados al desplegar esto (mismo
        patrón que los de Fase 10/13 — permisos incompletos del propio pipeline, no de
        la app): (1) el `push` a `main` del merge que agregó esto disparó `CI` pero no
        `Deploy` (falla puntual de GitHub Actions, sin causa visible del lado del
        repo) — se agregó `workflow_dispatch` a `deploy.yml` para poder re-lanzarlo a
        mano sin depender de otro commit; (2) el primer `apply` que sí corrió falló
        con `AccessDenied` en `cloudwatch:PutMetricAlarm` — el rol
        `semantic-search-github-actions-deploy` nunca había tenido permisos de
        CloudWatch/SNS, se agregaron en `infra/oidc.tf` (scoped a `semantic-search-*`)
- [ ] Confirmar que el AWS Budget Alert está activo — no verificable con el usuario IAM
      de despliegue (sin permiso `budgets:ViewBudget`, ver Fase 0), confirmar a mano

---

## Fase 15 — Backlog post-lanzamiento (UX, costos, runtime)

> Hallazgos de uso real de la app ya desplegada (2026-08-15), no bugs de una sesión de
> desarrollo puntual — quedan como backlog priorizable.

- [x] **Aislamiento de documentos por usuario (multi-tenancy)** — implementado. Root
      cause confirmado: `ChunkRecord` no tenía atributo de owner, y aunque el JWT de
      Cognito ya llegaba validado a cada Lambda (Fase 6), ninguno lo leía —
      `documents-service`/`query-service`/`report-service` hacían `Scan` completo sin
      filtrar por identidad.
      - `ChunkRecord.OwnerId` (Core) poblado al indexar con el `sub` del JWT del
        usuario que sube el archivo. Ownership viaja Upload → S3 (metadata del objeto,
        `x-amz-meta-owner-id`, no la key) → Indexer, que lo estampa en cada chunk.
        Helper compartido `SemanticSearch.Core.Auth.CallerIdentity.GetOwnerId` (4
        Lambdas HTTP: Upload, Documents, Query, Reports).
      - **Sin GSI nuevo** — se mantiene el `Scan` + filtro en memoria ya documentado
        como trade-off aceptado (Fases 4/5/11); filtro aplicado en
        `documents-service`/`query-service`/`report-service` antes de agrupar/rankear.
      - **Decisión de producto**: chunks legacy (`OwnerId` vacío, indexados antes de
        este cambio) se tratan como **compartidos** — visibles, reindexables y
        borrables por cualquier usuario autenticado. Sin pérdida de datos, sin
        necesidad de reindexar a mano.
      - Dos bugs reales encontrados durante el diseño, corregidos en el mismo cambio:
        (1) `S3DocumentService.TriggerReindexAsync` usaba
        `MetadataDirective = REPLACE` sin volver a setear `.Metadata` — cada reindex
        hubiera borrado el `owner-id` del objeto; (2) `QueryCacheService` hasheaba
        `query + topK` sin dimensión de usuario — sin el fix, la respuesta cacheada de
        un usuario se filtraba a cualquier otro que hiciera la misma pregunta.
      - Gap aceptado, fuera de alcance: `GET /reports/{reportId}` sigue sin ACL
        (protegido solo por lo impredecible del GUID) — el contenido del reporte ya se
        genera con el corpus filtrado por owner, pero la descarga en sí no valida quién
        pide el `reportId`.
      - Pendiente de verificación manual: round-trip completo contra LocalStack (Fase
        12) con dos usuarios de Cognito reales, confirmando que la firma de la URL
        prefirmada con metadata funciona end-to-end (`POST /upload` → PUT con header
        `x-amz-meta-owner-id` → evento S3 → `indexer-service` → chunk con `OwnerId` en
        DynamoDB Local).
- [ ] **Preview de documento en `DocumentsPage.tsx`** — agregar un botón "Ver" junto a
      "Reindexar"/"Eliminar" en la lista de documentos que abra el archivo original
      (PDF/Word) en vez de solo mostrar metadata. Evaluar: URL prefirmada de S3 GET
      (mismo patrón que `upload-service`, TTL corto) servida en una nueva pestaña o un
      visor embebido (`<iframe>` para PDF; `.docx` necesitaría conversión o descarga
      directa, no hay visor nativo de navegador para Word).
- [ ] **Cachear la vista de documentos en el frontend** — hoy `DocumentsPage.tsx` vuelve
      a pegarle a `GET /documents` (y por lo tanto a `documents-service`, que hace un
      `Scan` completo de `chunks`, Fase 5) cada vez que el usuario visita el tab, aunque
      la lista no haya cambiado. Evaluar `@tanstack/react-query` (cache + staleTime) o
      un cache simple en memoria/`sessionStorage` con invalidación manual tras
      subir/reindexar/borrar un documento. Reduce Scans innecesarios — relevante para
      costo real de DynamoDB ahora que no hay margen de crédito gratis (ver
      [`CLAUDE.md`](CLAUDE.md#por-qué-esta-arquitectura-decisiones-no-obvias)).
- [ ] **CI/CD del frontend** — mismo pendiente ya anotado en Fase 13
      (`deploy-frontend.yml`), subido de prioridad: hoy todo merge a `main` que toca
      `frontend/` no se despliega solo, y ya causó un incidente real (bundle
      desactualizado en CloudFront tras el fix de logout, ver Fase 13). Automatizar
      `infra/scripts/deploy-frontend.ps1` en un workflow nuevo con el mismo patrón OIDC
      de `deploy.yml`, disparado solo cuando el diff toca `frontend/**`.
- [ ] **Migrar las 5 Lambdas de `dotnet8` a un runtime soportado** — AWS avisó
      (AWS Health, 2026-08-15) que .NET 8 en Lambda deja de tener soporte el
      **2026-11-10** (sigue el EOL de .NET 8 en esa misma fecha), sin poder crear
      funciones nuevas en ese runtime desde 2027-02-01 ni actualizar las existentes
      desde 2027-03-03. No es urgente en lo inmediato (las funciones siguen
      ejecutando), pero hay que planearlo con tiempo:
      - Runtime managed más nuevo que soporta Lambda nativamente hoy es `dotnet10`
        (confirmar versión exacta disponible al momento de migrar). Cambiar
        `runtime = "dotnet8"` en `infra/lambda.tf` (5 recursos `aws_lambda_function`) +
        el target framework de los 5 `.csproj` de `Functions.*` (ver nota de
        [`CLAUDE.md`](CLAUDE.md#stack) sobre por qué se fijó `net8.0` en su momento —
        era el runtime nativo más nuevo disponible, ya no lo es).
      - Correr `dotnet test` completo tras el bump de target framework antes de tocar
        Terraform — riesgo principal es algún paquete (`PdfPig`,
        `DocumentFormat.OpenXml`, `AWSSDK.*`) sin compatibilidad todavía con el nuevo
        TFM.

---

## Checklist de seguridad

- [x] Nunca commitear credenciales, access keys ni connection strings — regla activa,
      respetada hasta ahora (ej. `.env.production` del frontend gitignored)
- [x] ~~Usar `dotnet user-secrets` en desarrollo local~~ — obsoleto para las Lambdas
      actuales: el entorno local (Fase 12) inyecta `GEMINI_API_KEY` vía
      `env.local.json` + `docker-compose`, no `user-secrets` (ese mecanismo solo
      lo usa `SemanticSearch.Api`, el proyecto legacy de Azure fuera del build activo)
- [x] Usar Secrets Manager/SSM en producción — confirmado, `GeminiSecretLoader` lee de
      SSM Parameter Store (`/semantic-search/gemini-api-key`, `SecureString`)
- [x] Validar JWT de Cognito en todos los endpoints excepto `/health` — confirmado en
      `infra/apigateway.tf` (todas las rutas tienen `authorization_type = "JWT"` menos `GET /health`)
- [x] Configurar CORS en API Gateway — configurado (incluye el dominio de CloudFront +
      `localhost:5173` para desarrollo, intencional)
- [x] Permisos IAM mínimos por Lambda — confirmado, cada policy en `lambda.tf` lista
      acciones y recursos específicos, sin `*`
- [x] Bucket S3 `docs`/`reports`/`frontend` sin acceso público — confirmado, los 3
      tienen `aws_s3_bucket_public_access_block` con las 4 flags en `true`

---

_Stack: .NET 8 · AWS Lambda · API Gateway (HTTP API) · DynamoDB · Google Gemini API · S3 · Cognito · CloudWatch · Terraform · GitHub Actions (OIDC) · React (Vite) + TypeScript_
