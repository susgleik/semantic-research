# Proyecto F — Sistema de Búsqueda Semántica RAG
## Blueprint completo en C# / .NET 8 sobre AWS (serverless + microservicios)

**Stack:** AWS Lambda (.NET 8) · API Gateway (HTTP API) · S3 · DynamoDB ·
Amazon Bedrock · Amazon Cognito · CloudFront · React (Vite) · AWS SAM

> Versión migrada desde Azure. La versión anterior (ASP.NET Core en Container Apps +
> Azure AI Search + Azure OpenAI) queda documentada al final de este archivo como
> referencia histórica.

---

## Por qué esta arquitectura

El requisito de la materia es aplicar cómputo en la nube con **serverless y/o
microservicios**. La cuenta de AWS usada es **nueva (2026)**, por lo que aplica el
esquema de Free Tier vigente: $100-200 de crédito durante 6 meses + un set de
servicios **Always Free** permanentes (sin importar antigüedad de la cuenta):
Lambda, DynamoDB, Cognito, CloudWatch, Step Functions, CloudFront.

El diseño se apoya deliberadamente en esos servicios Always Free para que el costo
de infraestructura sea **$0 de forma permanente**. El único costo real es Amazon
Bedrock (embeddings + LLM), que no tiene free tier pero cuesta centavos en el
volumen de uso de un proyecto académico.

**Decisión clave — no hay vector DB gestionada gratis en AWS:**
- OpenSearch (Service o Serverless) no tiene free tier real y puede costar
  $700+/mes si no se administra con cuidado por los OCUs mínimos.
- RDS + pgvector tiene free tier pero solo 12 meses, y es una instancia siempre
  prendida — rompe el modelo serverless.
- **Elegido: DynamoDB** (Always Free, 25GB) para guardar los chunks con su
  embedding, y la búsqueda de similitud (coseno) se calcula en memoria dentro del
  Lambda de query. Es la elección correcta para un corpus académico (decenas/
  cientos de documentos); no escalaría a millones de chunks en producción.

**Decisión clave — Lambdas separados por responsabilidad, no un monolito:**
en vez de un solo Lambda con ASP.NET Core sirviendo todos los endpoints, cada
operación (`upload`, `indexer`, `query`, `documents`) es su propia función Lambda,
desplegable y escalable de forma independiente. Esto es lo que cumple el requisito
de "microservicios": acoplamiento mínimo entre piezas, cada una con un solo motivo
para cambiar.

---

## Arquitectura general

```
┌───────────────────────────────────────────────────────────────────────────┐
│                          PIPELINE A — Ingesta                              │
│                                                                             │
│  Cliente ──POST /upload──► API Gateway ──► Lambda (upload-service)         │
│                                                       │                     │
│                                                       ▼                     │
│                                                  S3 (bucket docs)           │
│                                                       │                     │
│                                          S3 Event Notification              │
│                                                       ▼                     │
│                                          Lambda (indexer-service)           │
│                                                       │                     │
│                             ┌─────────────────────────┤                    │
│                             ▼                         ▼                    │
│                      Amazon Bedrock              DynamoDB                  │
│                      (Titan Embed v2)        (tabla "chunks")              │
└───────────────────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────────────────┐
│                          PIPELINE B — Query RAG                            │
│                                                                             │
│  Cliente ──POST /query──► API Gateway ──► Lambda (query-service)           │
│                                                       │                     │
│                                              embed query text               │
│                                                       ▼                     │
│                                                Amazon Bedrock               │
│                                                (Titan Embed v2)             │
│                                                       │                     │
│                                          similitud coseno en memoria        │
│                                                       ▼                     │
│                                                  DynamoDB                   │
│                                                (top-K chunks)               │
│                                                       │                     │
│                                              build RAG prompt                │
│                                                       ▼                     │
│                                                Amazon Bedrock               │
│                                                (Claude Haiku)               │
│                                                       │                     │
│  Cliente ◄── { answer, sources } ────────────────────┘                     │
└───────────────────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────────────────┐
│                          FRONTEND                                          │
│                                                                             │
│  Navegador ──► CloudFront ──► S3 (bucket frontend, React build estático)   │
│      │                                                                     │
│      └── fetch/axios ──► API Gateway (JWT de Cognito) ──► Lambdas          │
└───────────────────────────────────────────────────────────────────────────┘
```

---

## Estructura de carpetas

```
semantic-search/
│
├── src/
│   │
│   ├── SemanticSearch.Core/                       # Shared library — modelos y contratos
│   │   ├── SemanticSearch.Core.csproj
│   │   ├── Models/
│   │   │   ├── DocumentChunk.cs
│   │   │   ├── IndexedDocument.cs
│   │   │   └── ChunkRecord.cs                      # item de DynamoDB
│   │   └── Options/
│   │       ├── BedrockOptions.cs
│   │       ├── DynamoDbOptions.cs
│   │       └── S3Options.cs
│   │
│   ├── SemanticSearch.Functions.Upload/            # Lambda: POST /upload
│   │   ├── SemanticSearch.Functions.Upload.csproj
│   │   ├── UploadFunction.cs
│   │   └── Services/
│   │       └── S3UploadService.cs
│   │
│   ├── SemanticSearch.Functions.Indexer/           # Lambda: trigger S3
│   │   ├── SemanticSearch.Functions.Indexer.csproj
│   │   ├── IndexerFunction.cs
│   │   └── Services/
│   │       ├── ChunkerService.cs                   # sliding window con overlap
│   │       ├── BedrockEmbeddingService.cs
│   │       └── DynamoChunkWriter.cs
│   │
│   ├── SemanticSearch.Functions.Query/             # Lambda: POST /query
│   │   ├── SemanticSearch.Functions.Query.csproj
│   │   ├── QueryFunction.cs
│   │   └── Services/
│   │       ├── SimilaritySearchService.cs
│   │       └── RagAnswerService.cs
│   │
│   ├── SemanticSearch.Functions.Documents/         # Lambda: GET /documents, /reindex, /health
│   │   ├── SemanticSearch.Functions.Documents.csproj
│   │   ├── DocumentsFunction.cs
│   │   └── Services/
│   │       └── DocumentRegistryService.cs
│   │
│   └── SemanticSearch.McpServer/                   # Servidor MCP para agente @doc-search
│       ├── SemanticSearch.McpServer.csproj
│       ├── Program.cs
│       └── Tools/
│           ├── SearchDocumentsTool.cs
│           ├── ListDocumentsTool.cs
│           └── ReindexDocumentTool.cs
│
├── frontend/                                       # React SPA (Vite + TypeScript)
│   ├── package.json
│   ├── vite.config.ts
│   └── src/
│       ├── api/client.ts
│       ├── pages/
│       │   ├── UploadPage.tsx
│       │   ├── DocumentsPage.tsx
│       │   └── QueryPage.tsx
│       └── App.tsx
│
├── tests/
│   ├── SemanticSearch.Functions.Indexer.Tests/
│   │   └── ChunkerServiceTests.cs
│   └── SemanticSearch.Functions.Query.Tests/
│       └── SimilaritySearchServiceTests.cs
│
├── infra/                                          # Infrastructure as Code (AWS SAM)
│   ├── template.yaml
│   └── samconfig.toml
│
├── .github/
│   └── workflows/
│       ├── deploy.yml
│       └── deploy-frontend.yml
│
└── SemanticSearch.sln
```

---

## DynamoDB — diseño de la tabla `chunks`

| Atributo | Tipo | Rol |
|---|---|---|
| `documentId` | String | Partition Key |
| `chunkId` | String | Sort Key |
| `text` | String | texto del chunk |
| `embedding` | List\<Number\> | vector de embedding (Titan = 1024 dims) |
| `filename` | String | nombre original del documento |
| `page` | Number | página de origen (si aplica) |
| `status` | String | `indexed` / `failed` |
| `createdAt` | String (ISO 8601) | fecha de indexado |

La tabla `documents` (metadata, separada o como ítems con `chunkId = "META"`)
guarda el registro por documento: `documentId`, `filename`, `status`, `chunkCount`,
`uploadedAt`.

> Nota de escala: para un corpus académico, un `Scan` completo de la tabla +
> similitud coseno en memoria dentro del Lambda es rápido y simple. Si el corpus
> creciera mucho, el siguiente paso sería paginar el scan o particionar por
> categoría/documento antes de comparar vectores.

---

## Código clave

### `IndexerFunction.cs` — Lambda disparado por evento S3

```csharp
public class IndexerFunction
{
    private readonly ChunkerService _chunker;
    private readonly BedrockEmbeddingService _embeddings;
    private readonly DynamoChunkWriter _writer;
    private readonly IAmazonS3 _s3;
    private readonly ILambdaLogger _logger;

    public IndexerFunction()
    {
        // Sin contenedor de DI de ASP.NET Core — se arma a mano
        _s3 = new AmazonS3Client();
        _chunker = new ChunkerService();
        _embeddings = new BedrockEmbeddingService(new AmazonBedrockRuntimeClient());
        _writer = new DynamoChunkWriter(new AmazonDynamoDBClient());
    }

    public async Task FunctionHandler(S3Event s3Event, ILambdaContext context)
    {
        foreach (var record in s3Event.Records)
        {
            var bucket = record.S3.Bucket.Name;
            var key = record.S3.Object.Key;
            context.Logger.LogInformation($"Indexing {bucket}/{key}");

            try
            {
                var obj = await _s3.GetObjectAsync(bucket, key);
                var text = await ExtractTextAsync(obj.ResponseStream, key);

                var chunks = _chunker.SlidingWindow(text, windowSize: 512, overlap: 64);
                var vectors = await _embeddings.EmbedBatchAsync(chunks.Select(c => c.Text));

                var documentId = Path.GetFileNameWithoutExtension(key);
                await _writer.WriteChunksAsync(documentId, key, chunks, vectors);

                context.Logger.LogInformation($"Indexed {chunks.Count} chunks for {key}");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Failed to index {key}: {ex.Message}");
                // mover el objeto a un prefijo failed/ en vez de usar una DLQ gestionada
                await _s3.CopyObjectAsync(bucket, key, bucket, $"failed/{key}");
            }
        }
    }

    private static async Task<string> ExtractTextAsync(Stream content, string key) =>
        Path.GetExtension(key).ToLower() switch
        {
            ".txt" => await new StreamReader(content).ReadToEndAsync(),
            ".pdf" => ExtractPdfText(content),    // PdfPig
            ".docx" => ExtractDocxText(content),  // DocumentFormat.OpenXml
            _ => throw new NotSupportedException($"Unsupported file type: {key}")
        };
}
```

### `BedrockEmbeddingService.cs`

```csharp
public class BedrockEmbeddingService(IAmazonBedrockRuntime bedrock)
{
    private const string ModelId = "amazon.titan-embed-text-v2:0";

    public async Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { inputText = text });
        var response = await bedrock.InvokeModelAsync(new InvokeModelRequest
        {
            ModelId = ModelId,
            ContentType = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(payload))
        }, ct);

        var result = await JsonSerializer.DeserializeAsync<TitanEmbeddingResponse>(response.Body, cancellationToken: ct);
        return result!.Embedding.ToArray();
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        // Titan Embed no soporta batch nativo — se invoca en paralelo controlado
        var tasks = texts.Select(t => EmbedAsync(t, ct));
        return await Task.WhenAll(tasks);
    }

    private record TitanEmbeddingResponse(float[] Embedding);
}
```

### `SimilaritySearchService.cs` — búsqueda por similitud coseno

```csharp
public class SimilaritySearchService(IAmazonDynamoDB dynamoDb, IOptions<DynamoDbOptions> opts)
{
    public async Task<IReadOnlyList<SourceChunk>> SearchAsync(
        ReadOnlyMemory<float> queryVector, int topK, CancellationToken ct = default)
    {
        // Scan completo de la tabla de chunks — viable para un corpus académico
        var scan = await dynamoDb.ScanAsync(new ScanRequest
        {
            TableName = opts.Value.ChunksTableName
        }, ct);

        var ranked = scan.Items
            .Select(item => new
            {
                Item = item,
                Score = CosineSimilarity(queryVector.Span, ParseEmbedding(item["embedding"]))
            })
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => new SourceChunk(
                DocId: x.Item["documentId"].S,
                Filename: x.Item["filename"].S,
                Chunk: x.Item["text"].S,
                Score: x.Score,
                Page: int.Parse(x.Item.GetValueOrDefault("page")?.N ?? "0")
            ))
            .ToList();

        return ranked;
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB) + 1e-8f);
    }

    private static float[] ParseEmbedding(AttributeValue attr) =>
        attr.L.Select(v => float.Parse(v.N)).ToArray();
}
```

### `RagAnswerService.cs` — completions con Bedrock (Claude)

```csharp
public class RagAnswerService(IAmazonBedrockRuntime bedrock, IOptions<BedrockOptions> opts)
{
    public async Task<string> GenerateAnswerAsync(
        string query, IReadOnlyList<SourceChunk> chunks, CancellationToken ct = default)
    {
        var context = string.Join("\n\n", chunks.Select((c, i) =>
            $"[Fragmento {i + 1} — {c.Filename}]\n{c.Chunk}"));

        var prompt = $"""
            Sos un asistente que responde preguntas basándose exclusivamente en los
            fragmentos de documentos provistos. Si la respuesta no está en los
            fragmentos, indicalo claramente.

            Pregunta: {query}

            Fragmentos relevantes:
            {context}
            """;

        var payload = JsonSerializer.Serialize(new
        {
            anthropic_version = "bedrock-2023-05-31",
            max_tokens = 1500,
            temperature = 0.1,
            messages = new[] { new { role = "user", content = prompt } }
        });

        var response = await bedrock.InvokeModelAsync(new InvokeModelRequest
        {
            ModelId = opts.Value.ChatModelId, // "anthropic.claude-3-haiku-20240307-v1:0"
            ContentType = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(payload))
        }, ct);

        var result = await JsonSerializer.DeserializeAsync<ClaudeResponse>(response.Body, cancellationToken: ct);
        return result!.Content[0].Text;
    }

    private record ClaudeResponse(ClaudeContent[] Content);
    private record ClaudeContent(string Text);
}
```

### `ChunkerService.cs` — sliding window (sin cambios respecto a la versión Azure)

```csharp
public class ChunkerService
{
    public record Chunk(string Text, int StartIndex, int WordCount);

    public IReadOnlyList<Chunk> SlidingWindow(string text, int windowSize = 512, int overlap = 64)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<Chunk>();
        var step = windowSize - overlap;

        for (int i = 0; i < words.Length; i += step)
        {
            var end = Math.Min(i + windowSize, words.Length);
            var chunkText = string.Join(' ', words[i..end]);
            chunks.Add(new Chunk(chunkText, i, end - i));
            if (end == words.Length) break;
        }

        return chunks;
    }
}
```

---

## Endpoints de la API

| Método | Path | Auth | Lambda | Descripción |
|---|---|---|---|---|
| POST | `/upload` | JWT (Cognito) | `upload-service` | Sube documento a S3, dispara indexación |
| POST | `/query` | JWT (Cognito) | `query-service` | Búsqueda semántica RAG |
| GET | `/documents` | JWT (Cognito) | `documents-service` | Lista documentos con estado y metadata |
| POST | `/reindex/{docId}` | JWT (Cognito) | `documents-service` | Fuerza re-indexación |
| DELETE | `/documents/{docId}` | JWT (Cognito) | `documents-service` | Elimina documento y sus chunks |
| GET | `/health` | none | `documents-service` | Health check |

---

## `infra/template.yaml` — AWS SAM (esqueleto)

```yaml
AWSTemplateFormatVersion: '2010-09-09'
Transform: AWS::Serverless-2016-10-31
Description: SemanticSearch RAG — serverless

Globals:
  Function:
    Runtime: dotnet8
    Timeout: 30
    MemorySize: 512

Resources:
  DocsBucket:
    Type: AWS::S3::Bucket
    Properties:
      BucketName: !Sub semantic-search-docs-${AWS::AccountId}

  ChunksTable:
    Type: AWS::DynamoDB::Table
    Properties:
      TableName: semantic-search-chunks
      BillingMode: PAY_PER_REQUEST
      AttributeDefinitions:
        - AttributeName: documentId
          AttributeType: S
        - AttributeName: chunkId
          AttributeType: S
      KeySchema:
        - AttributeName: documentId
          KeyType: HASH
        - AttributeName: chunkId
          KeyType: RANGE

  UploadFunction:
    Type: AWS::Serverless::Function
    Properties:
      CodeUri: src/SemanticSearch.Functions.Upload/
      Handler: SemanticSearch.Functions.Upload::SemanticSearch.Functions.Upload.UploadFunction::FunctionHandler
      Policies:
        - S3WritePolicy:
            BucketName: !Ref DocsBucket
      Events:
        Api:
          Type: HttpApi
          Properties:
            ApiId: !Ref HttpApi
            Path: /upload
            Method: post

  IndexerFunction:
    Type: AWS::Serverless::Function
    Properties:
      CodeUri: src/SemanticSearch.Functions.Indexer/
      Handler: SemanticSearch.Functions.Indexer::SemanticSearch.Functions.Indexer.IndexerFunction::FunctionHandler
      Policies:
        - S3ReadPolicy:
            BucketName: !Ref DocsBucket
        - DynamoDBWritePolicy:
            TableName: !Ref ChunksTable
        - Statement:
            - Effect: Allow
              Action: bedrock:InvokeModel
              Resource: "*"
      Events:
        S3Event:
          Type: S3
          Properties:
            Bucket: !Ref DocsBucket
            Events: s3:ObjectCreated:*

  HttpApi:
    Type: AWS::Serverless::HttpApi
    Properties:
      Auth:
        Authorizers:
          CognitoAuthorizer:
            JwtConfiguration:
              issuer: !Sub https://cognito-idp.${AWS::Region}.amazonaws.com/${UserPool}
              audience:
                - !Ref UserPoolClient
            IdentitySource: $request.header.Authorization
        DefaultAuthorizer: CognitoAuthorizer

  UserPool:
    Type: AWS::Cognito::UserPool
    Properties:
      UserPoolName: semantic-search-users

  UserPoolClient:
    Type: AWS::Cognito::UserPoolClient
    Properties:
      UserPoolId: !Ref UserPool
      GenerateSecret: false
```

> Faltan en este esqueleto: `QueryFunction`, `DocumentsFunction`, el bucket
> `frontend` + distribución CloudFront, y los permisos IAM específicos por función.
> Ver Fase 10 de [`TODO.md`](../TODO.md).

---

## Pipeline CI/CD — GitHub Actions (OIDC, sin access keys)

```yaml
# .github/workflows/deploy.yml
name: Deploy

on:
  push:
    branches: [main]
    paths: ["src/**", "infra/**"]

permissions:
  id-token: write
  contents: read

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "8.0.x"

      - name: Test
        run: dotnet test

      - name: Configure AWS credentials (OIDC)
        uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: ${{ vars.AWS_DEPLOY_ROLE_ARN }}
          aws-region: us-east-1

      - name: Setup SAM CLI
        uses: aws-actions/setup-sam@v2

      - name: Build and deploy
        run: |
          sam build
          sam deploy --no-confirm-changeset --no-fail-on-empty-changeset
```

El rol `AWS_DEPLOY_ROLE_ARN` se crea una sola vez con confianza hacia el OIDC
provider de GitHub Actions — nunca se guardan access keys de AWS en GitHub Secrets.

---

## Frontend — React (Vite + TypeScript)

```
frontend/
├── src/
│   ├── api/client.ts        # wrapper de fetch con la URL de API Gateway + JWT
│   ├── pages/
│   │   ├── UploadPage.tsx   # drag & drop → upload-service
│   │   ├── DocumentsPage.tsx# lista + estado de indexado → documents-service
│   │   └── QueryPage.tsx    # chat de preguntas → query-service, muestra fuentes
│   └── App.tsx
└── vite.config.ts
```

```ts
// src/api/client.ts
const API_URL = import.meta.env.VITE_API_URL;

export async function query(question: string, token: string) {
  const res = await fetch(`${API_URL}/query`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({ query: question, topK: 5 }),
  });
  if (!res.ok) throw new Error(`Query failed: ${res.status}`);
  return res.json(); // { answer, sources }
}
```

Deploy: `npm run build` → sync del directorio `dist/` al bucket S3 `frontend`
(privado, sin acceso público directo) → invalidación de la distribución
CloudFront que sirve el contenido con Origin Access Control (OAC).

---

## Servidor MCP — agente @doc-search

Sin cambios en el código del servidor MCP; solo cambia la URL a la que apunta
(ahora la URL de API Gateway en vez de Container Apps).

```json
// .vscode/settings.json
{
  "github.copilot.chat.mcpServers": {
    "doc-search": {
      "command": "dotnet",
      "args": ["run", "--project", "src/SemanticSearch.McpServer"],
      "env": {
        "API_URL": "https://xxxxxxxxxx.execute-api.us-east-1.amazonaws.com"
      }
    }
  }
}
```

---

## Equivalencias Azure → AWS

| Concepto | Azure (versión anterior) | AWS (versión actual) |
|---|---|---|
| Storage de documentos | Blob Storage | S3 |
| Cómputo de indexación | Azure Functions (blob trigger) | Lambda (S3 Event Notification) |
| Vector store | Azure AI Search | DynamoDB + similitud coseno en código |
| Embeddings + LLM | Azure OpenAI | Amazon Bedrock (Titan Embed + Claude) |
| API/cómputo principal | ASP.NET Core en Container Apps | API Gateway (HTTP API) + Lambda |
| Auth | Azure AD (JWT) | Amazon Cognito (JWT Authorizer nativo) |
| IaC | Bicep | AWS SAM |
| Observabilidad | Application Insights | CloudWatch |
| Secrets en producción | Azure Key Vault | AWS Secrets Manager / SSM Parameter Store |

---

_Stack: .NET 8 · AWS Lambda · API Gateway (HTTP API) · DynamoDB · Amazon Bedrock ·
S3 · Cognito · CloudFront · AWS SAM · GitHub Actions (OIDC) · React (Vite) + TypeScript_
