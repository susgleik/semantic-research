# Proyecto F — Sistema de Búsqueda Semántica RAG
## Blueprint completo en C# / .NET 8

**Stack:** ASP.NET Core 8 · Azure Functions v4 (isolated worker) · Azure AI Search · Azure OpenAI · Blob Storage · Container Apps

---

## Arquitectura general

```
┌─────────────────────────────────────────────────────────────────┐
│                        PIPELINE A — Ingesta                      │
│                                                                   │
│  Cliente ──POST /upload──► ASP.NET Core API ──► Blob Storage     │
│                                                       │           │
│                                              blob trigger         │
│                                                       ▼           │
│                                            Azure Function         │
│                                            (indexer)             │
│                                                 │                 │
│                             ┌───────────────────┤                 │
│                             ▼                   ▼                 │
│                       Azure OpenAI        Azure AI Search         │
│                       (embeddings)        (vector index)          │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                        PIPELINE B — Query RAG                    │
│                                                                   │
│  Cliente ──POST /query──► ASP.NET Core API                       │
│                                    │                              │
│                           embed query text                        │
│                                    │                              │
│                                    ▼                              │
│                             Azure OpenAI                          │
│                             (embeddings)                          │
│                                    │                              │
│                           hybrid search                           │
│                                    │                              │
│                                    ▼                              │
│                            Azure AI Search                        │
│                            (top-K chunks)                         │
│                                    │                              │
│                           build RAG prompt                        │
│                                    │                              │
│                                    ▼                              │
│                             Azure OpenAI                          │
│                             (GPT-4o completions)                  │
│                                    │                              │
│                                    ▼                              │
│  Cliente ◄── { answer, sources } ──┘                             │
└─────────────────────────────────────────────────────────────────┘
```

---

## Estructura de carpetas

```
semantic-search/
│
├── src/
│   │
│   ├── SemanticSearch.Api/                   # ASP.NET Core 8 — Container Apps
│   │   ├── SemanticSearch.Api.csproj
│   │   ├── Program.cs                        # Minimal API entrypoint + DI setup
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   │
│   │   ├── Endpoints/                        # Minimal API endpoints
│   │   │   ├── UploadEndpoints.cs            # POST /upload
│   │   │   ├── QueryEndpoints.cs             # POST /query
│   │   │   ├── DocumentEndpoints.cs          # GET  /documents, POST /reindex/:id
│   │   │   └── HealthEndpoints.cs            # GET  /health
│   │   │
│   │   ├── Services/
│   │   │   ├── IRagService.cs
│   │   │   ├── RagService.cs                 # Orquesta embed + search + completions
│   │   │   ├── IEmbeddingService.cs
│   │   │   ├── EmbeddingService.cs           # Azure OpenAI embeddings
│   │   │   ├── ISearchService.cs
│   │   │   ├── SearchService.cs              # Azure AI Search client
│   │   │   ├── IBlobService.cs
│   │   │   └── BlobService.cs                # Azure Blob Storage client
│   │   │
│   │   ├── Models/
│   │   │   ├── QueryRequest.cs
│   │   │   ├── QueryResponse.cs
│   │   │   ├── UploadRequest.cs
│   │   │   ├── UploadResponse.cs
│   │   │   ├── SourceChunk.cs
│   │   │   └── DocumentRecord.cs
│   │   │
│   │   ├── Middleware/
│   │   │   ├── AuthMiddleware.cs             # Validación Azure AD JWT
│   │   │   └── ExceptionMiddleware.cs        # Manejo centralizado de errores
│   │   │
│   │   └── Dockerfile
│   │
│   ├── SemanticSearch.Functions/             # Azure Functions v4 — blob indexer
│   │   ├── SemanticSearch.Functions.csproj
│   │   ├── Program.cs                        # Isolated worker host setup
│   │   ├── host.json
│   │   ├── local.settings.json
│   │   │
│   │   ├── Functions/
│   │   │   └── DocumentIndexer.cs            # Blob trigger → chunk → embed → index
│   │   │
│   │   └── Services/
│   │       ├── ChunkerService.cs             # Sliding window con overlap
│   │       ├── EmbeddingService.cs           # Reutiliza lógica de embeddings
│   │       └── SearchIndexerService.cs       # Escribe chunks en AI Search
│   │
│   ├── SemanticSearch.Core/                  # Shared library — modelos y contratos
│   │   ├── SemanticSearch.Core.csproj
│   │   ├── Models/
│   │   │   ├── DocumentChunk.cs
│   │   │   └── IndexedDocument.cs
│   │   └── Options/
│   │       ├── OpenAIOptions.cs
│   │       ├── SearchOptions.cs
│   │       └── BlobOptions.cs
│   │
│   └── SemanticSearch.McpServer/             # Servidor MCP para agente @doc-search
│       ├── SemanticSearch.McpServer.csproj
│       ├── Program.cs
│       └── Tools/
│           ├── SearchDocumentsTool.cs
│           ├── ListDocumentsTool.cs
│           └── ReindexDocumentTool.cs
│
├── tests/
│   ├── SemanticSearch.Api.Tests/
│   │   ├── Endpoints/
│   │   │   ├── QueryEndpointsTests.cs
│   │   │   └── UploadEndpointsTests.cs
│   │   └── Services/
│   │       └── RagServiceTests.cs
│   └── SemanticSearch.Functions.Tests/
│       └── DocumentIndexerTests.cs
│
├── infra/                                    # Infrastructure as Code (Bicep)
│   ├── main.bicep
│   ├── blob.bicep
│   ├── search.bicep
│   ├── openai.bicep
│   └── container-app.bicep
│
├── .github/
│   ├── workflows/
│   │   ├── deploy-api.yml
│   │   └── deploy-functions.yml
│   └── copilot-instructions.md
│
└── SemanticSearch.sln                        # Solution file — agrupa todos los proyectos
```

---

## NuGet packages por proyecto

### SemanticSearch.Api.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <!-- Azure OpenAI -->
    <PackageReference Include="Azure.AI.OpenAI" Version="2.*" />

    <!-- Azure AI Search -->
    <PackageReference Include="Azure.Search.Documents" Version="11.*" />

    <!-- Azure Blob Storage -->
    <PackageReference Include="Azure.Storage.Blobs" Version="12.*" />

    <!-- Azure AD auth -->
    <PackageReference Include="Microsoft.Identity.Web" Version="2.*" />

    <!-- Options pattern + validation -->
    <PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" Version="8.*" />

    <!-- Shared models -->
    <ProjectReference Include="../SemanticSearch.Core/SemanticSearch.Core.csproj" />
  </ItemGroup>
</Project>
```

### SemanticSearch.Functions.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AzureFunctionsVersion>v4</AzureFunctionsVersion>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Azure.Functions.Worker" Version="1.*" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Storage.Blobs" Version="6.*" />
    <PackageReference Include="Azure.AI.OpenAI" Version="2.*" />
    <PackageReference Include="Azure.Search.Documents" Version="11.*" />
    <PackageReference Include="Azure.Storage.Blobs" Version="12.*" />
    <ProjectReference Include="../SemanticSearch.Core/SemanticSearch.Core.csproj" />
  </ItemGroup>
</Project>
```

---

## Código clave

### Program.cs — ASP.NET Core 8 (Minimal API)

```csharp
using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using Microsoft.Identity.Web;
using SemanticSearch.Api.Middleware;
using SemanticSearch.Api.Services;
using SemanticSearch.Core.Options;

var builder = WebApplication.CreateBuilder(args);

// ── Options (strongly-typed, validados al arrancar) ──────────────────────────
builder.Services
    .AddOptions<OpenAIOptions>()
    .BindConfiguration("AzureOpenAI")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<SearchOptions>()
    .BindConfiguration("AzureSearch")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<BlobOptions>()
    .BindConfiguration("AzureBlob")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── Azure clients (singleton — reusan connection pool) ───────────────────────
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;
    return new AzureOpenAIClient(new Uri(opts.Endpoint), new AzureKeyCredential(opts.ApiKey));
});

builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<SearchOptions>>().Value;
    return new SearchClient(new Uri(opts.Endpoint), opts.IndexName, new AzureKeyCredential(opts.ApiKey));
});

builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<BlobOptions>>().Value;
    return new BlobServiceClient(opts.ConnectionString);
});

// ── Servicios de dominio ──────────────────────────────────────────────────────
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IBlobService, BlobService>();
builder.Services.AddScoped<IRagService, RagService>();

// ── Auth Azure AD ─────────────────────────────────────────────────────────────
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// ── Logging estructurado ──────────────────────────────────────────────────────
builder.Logging.AddConsole();

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapUploadEndpoints();
app.MapQueryEndpoints();
app.MapDocumentEndpoints();
app.MapHealthEndpoints();

app.Run();
```

---

### Models — request y response

```csharp
// Models/QueryRequest.cs
public record QueryRequest(
    [Required] string Query,
    int TopK = 5,
    string? Filter = null,
    string Language = "es"
);

// Models/QueryResponse.cs
public record QueryResponse(
    string Answer,
    IReadOnlyList<SourceChunk> Sources
);

// Models/SourceChunk.cs
public record SourceChunk(
    string DocId,
    string Filename,
    string Chunk,
    double Score,
    int Page
);

// Models/UploadResponse.cs
public record UploadResponse(
    string DocId,
    string Filename,
    string Status,
    string BlobUrl
);
```

---

### RagService.cs — orquestador principal

```csharp
public class RagService(
    IEmbeddingService embeddings,
    ISearchService search,
    AzureOpenAIClient openAiClient,
    IOptions<OpenAIOptions> opts,
    ILogger<RagService> logger) : IRagService
{
    private readonly string _chatDeployment = opts.Value.ChatDeployment;

    public async Task<QueryResponse> QueryAsync(QueryRequest request, CancellationToken ct = default)
    {
        // 1. Embed la pregunta del usuario
        logger.LogInformation("Generating embedding for query");
        var queryVector = await embeddings.EmbedAsync(request.Query, ct);

        // 2. Búsqueda híbrida: vector + keyword
        logger.LogInformation("Running hybrid search, top_k={TopK}", request.TopK);
        var chunks = await search.HybridSearchAsync(request.Query, queryVector, request.TopK, request.Filter, ct);

        // 3. Construir prompt RAG con el contexto recuperado
        var ragPrompt = BuildRagPrompt(request.Query, chunks);

        // 4. Completions con GPT-4o
        logger.LogInformation("Calling GPT-4o completions");
        var chatClient = openAiClient.GetChatClient(_chatDeployment);

        var completion = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage("""
                    Sos un asistente que responde preguntas basándose exclusivamente
                    en los fragmentos de documentos provistos. Si la respuesta no está
                    en los fragmentos, indicalo claramente. Respondé en el mismo idioma
                    que la pregunta.
                    """),
                new UserChatMessage(ragPrompt)
            ],
            new ChatCompletionOptions { Temperature = 0.1f, MaxOutputTokenCount = 1500 },
            ct
        );

        var answer = completion.Value.Content[0].Text;
        return new QueryResponse(answer, chunks);
    }

    private static string BuildRagPrompt(string query, IReadOnlyList<SourceChunk> chunks)
    {
        var context = string.Join("\n\n", chunks.Select((c, i) =>
            $"[Fragmento {i + 1} — {c.Filename}, página {c.Page}]\n{c.Chunk}"));

        return $"""
            Pregunta: {query}

            Fragmentos de documentos relevantes:
            {context}

            Respondé la pregunta basándote en los fragmentos anteriores.
            """;
    }
}
```

---

### EmbeddingService.cs

```csharp
public class EmbeddingService(
    AzureOpenAIClient client,
    IOptions<OpenAIOptions> opts) : IEmbeddingService
{
    private readonly EmbeddingClient _embeddingClient =
        client.GetEmbeddingClient(opts.Value.EmbeddingDeployment);

    public async Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default)
    {
        var result = await _embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats();
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        var inputs = texts.Select(t => new EmbeddingGenerationOptions()).ToList();
        var result = await _embeddingClient.GenerateEmbeddingsAsync(texts, cancellationToken: ct);
        return result.Value.Select(e => e.ToFloats()).ToList();
    }
}
```

---

### SearchService.cs — búsqueda híbrida

```csharp
public class SearchService(
    SearchClient searchClient,
    ILogger<SearchService> logger) : ISearchService
{
    public async Task<IReadOnlyList<SourceChunk>> HybridSearchAsync(
        string query,
        ReadOnlyMemory<float> vector,
        int topK,
        string? filter,
        CancellationToken ct = default)
    {
        var options = new SearchOptions
        {
            Size = topK,
            Filter = filter,
            Select = { "doc_id", "filename", "text", "page" },
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(vector)
                    {
                        KNearestNeighborsCount = topK,
                        Fields = { "embedding" }
                    }
                }
            },
            // Búsqueda semántica sobre los resultados vectoriales
            SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = "default",
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive)
            }
        };

        var response = await searchClient.SearchAsync<SearchDocument>(query, options, ct);

        var chunks = new List<SourceChunk>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            chunks.Add(new SourceChunk(
                DocId:    result.Document["doc_id"].ToString()!,
                Filename: result.Document["filename"].ToString()!,
                Chunk:    result.Document["text"].ToString()!,
                Score:    result.Score ?? 0,
                Page:     Convert.ToInt32(result.Document["page"])
            ));
        }

        logger.LogInformation("Hybrid search returned {Count} chunks", chunks.Count);
        return chunks;
    }
}
```

---

### Endpoints — Minimal API

```csharp
// Endpoints/QueryEndpoints.cs
public static class QueryEndpoints
{
    public static IEndpointRouteBuilder MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/").RequireAuthorization();

        group.MapPost("/query", async (
            QueryRequest request,
            IRagService ragService,
            CancellationToken ct) =>
        {
            var result = await ragService.QueryAsync(request, ct);
            return Results.Ok(result);
        })
        .WithName("Query")
        .Produces<QueryResponse>()
        .ProducesProblem(400)
        .ProducesProblem(500);

        return app;
    }
}

// Endpoints/UploadEndpoints.cs
public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/").RequireAuthorization();

        group.MapPost("/upload", async (
            IFormFile file,
            [FromForm] string category,
            IBlobService blobService,
            CancellationToken ct) =>
        {
            var docId   = Guid.NewGuid().ToString();
            var blobUrl = await blobService.UploadAsync(file, docId, category, ct);

            return Results.Accepted("/upload", new UploadResponse(
                DocId:    docId,
                Filename: file.FileName,
                Status:   "indexing",
                BlobUrl:  blobUrl
            ));
        })
        .WithName("Upload")
        .DisableAntiforgery()
        .Produces<UploadResponse>(202)
        .ProducesProblem(400);

        return app;
    }
}
```

---

### Azure Function — DocumentIndexer.cs

```csharp
public class DocumentIndexer(
    ChunkerService chunker,
    EmbeddingService embeddings,
    SearchIndexerService indexer,
    ILogger<DocumentIndexer> logger)
{
    // El binding de Blob trigger detecta automáticamente archivos nuevos en el container
    [Function("DocumentIndexer")]
    public async Task RunAsync(
        [BlobTrigger("docs/{name}", Connection = "AzureStorageConnection")] BlobClient blobClient,
        string name,
        CancellationToken ct)
    {
        logger.LogInformation("Indexing document: {Name}", name);

        // 1. Descargar y extraer texto del blob
        var content = await blobClient.DownloadContentAsync(ct);
        var text = ExtractText(content.Value.Content.ToArray(), name);

        // 2. Dividir en chunks con sliding window
        var chunks = chunker.SlidingWindow(text, windowSize: 512, overlap: 64);
        logger.LogInformation("Created {Count} chunks for {Name}", chunks.Count, name);

        // 3. Generar embeddings en batch
        var vectors = await embeddings.EmbedBatchAsync(chunks.Select(c => c.Text), ct);

        // 4. Indexar en Azure AI Search
        var docId = Path.GetFileNameWithoutExtension(name);
        await indexer.IndexChunksAsync(docId, name, chunks, vectors, ct);

        logger.LogInformation("Document {Name} indexed successfully", name);
    }

    private static string ExtractText(byte[] content, string filename) =>
        Path.GetExtension(filename).ToLower() switch
        {
            ".txt"  => System.Text.Encoding.UTF8.GetString(content),
            ".pdf"  => ExtractPdfText(content),   // usar PdfPig
            ".docx" => ExtractDocxText(content),   // usar DocumentFormat.OpenXml
            _       => throw new NotSupportedException($"Unsupported file type: {filename}")
        };
}
```

---

### ChunkerService.cs — sliding window

```csharp
public class ChunkerService
{
    public record Chunk(string Text, int StartIndex, int WordCount);

    // Sliding window con overlap para no perder contexto entre chunks
    public IReadOnlyList<Chunk> SlidingWindow(string text, int windowSize = 512, int overlap = 64)
    {
        var words  = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<Chunk>();
        var step   = windowSize - overlap;

        for (int i = 0; i < words.Length; i += step)
        {
            var end       = Math.Min(i + windowSize, words.Length);
            var chunkText = string.Join(' ', words[i..end]);
            chunks.Add(new Chunk(chunkText, i, end - i));

            if (end == words.Length) break;
        }

        return chunks;
    }
}
```

---

### ExceptionMiddleware.cs — manejo centralizado de errores

```csharp
public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            context.Response.StatusCode  = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(new
            {
                error   = "Internal server error",
                traceId = Activity.Current?.Id ?? context.TraceIdentifier
            });
        }
    }
}
```

---

### Options — strongly-typed configuration

```csharp
// Core/Options/OpenAIOptions.cs
public class OpenAIOptions
{
    [Required] public string Endpoint            { get; set; } = "";
    [Required] public string ApiKey              { get; set; } = "";
    [Required] public string EmbeddingDeployment { get; set; } = "text-embedding-3-large";
    [Required] public string ChatDeployment      { get; set; } = "gpt-4o";
}

// Core/Options/SearchOptions.cs
public class SearchOptions
{
    [Required] public string Endpoint  { get; set; } = "";
    [Required] public string ApiKey    { get; set; } = "";
    [Required] public string IndexName { get; set; } = "documents";
}

// Core/Options/BlobOptions.cs
public class BlobOptions
{
    [Required] public string ConnectionString { get; set; } = "";
    [Required] public string Container        { get; set; } = "docs";
}
```

---

### appsettings.json

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://my-openai.openai.azure.com/",
    "ApiKey": "",
    "EmbeddingDeployment": "text-embedding-3-large",
    "ChatDeployment": "gpt-4o"
  },
  "AzureSearch": {
    "Endpoint": "https://my-search.search.windows.net",
    "ApiKey": "",
    "IndexName": "documents"
  },
  "AzureBlob": {
    "ConnectionString": "",
    "Container": "docs"
  },
  "AzureAd": {
    "TenantId": "",
    "ClientId": "",
    "Audience": "api://my-app-id"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

> En desarrollo local usar `appsettings.Development.json` con `dotnet user-secrets`
> para los valores sensibles. Nunca commitear credenciales.

```bash
dotnet user-secrets set "AzureOpenAI:ApiKey" "tu-key-aqui"
dotnet user-secrets set "AzureSearch:ApiKey" "tu-key-aqui"
dotnet user-secrets set "AzureBlob:ConnectionString" "tu-connection-string"
```

---

## Endpoints de la API

| Método | Path                    | Auth | Descripción                                       |
|--------|-------------------------|------|---------------------------------------------------|
| POST   | `/upload`               | JWT  | Sube documento, dispara indexación                |
| POST   | `/query`                | JWT  | Búsqueda semántica RAG                            |
| POST   | `/reindex/{docId}`      | JWT  | Fuerza re-indexación de un documento              |
| GET    | `/documents`            | JWT  | Lista documentos con estado y metadata            |
| GET    | `/health`               | none | Health check de conectividad con Azure services   |

---

## Dockerfile — ASP.NET Core

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/SemanticSearch.Api/SemanticSearch.Api.csproj",   "SemanticSearch.Api/"]
COPY ["src/SemanticSearch.Core/SemanticSearch.Core.csproj", "SemanticSearch.Core/"]
RUN dotnet restore "SemanticSearch.Api/SemanticSearch.Api.csproj"

COPY src/ .
WORKDIR "/src/SemanticSearch.Api"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SemanticSearch.Api.dll"]
```

---

## Deploy — Azure Container Apps

```bash
# 1. Provisionar infra con Bicep
az deployment group create \
  --resource-group rg-semantic-search \
  --template-file infra/main.bicep \
  --parameters @infra/params.json

# 2. Build y push de la imagen al Azure Container Registry
az acr build \
  --registry myregistry \
  --image semantic-search-api:latest \
  ./src/SemanticSearch.Api

# 3. Crear la Container App
az containerapp create \
  --name semantic-search-api \
  --resource-group rg-semantic-search \
  --environment my-env \
  --image myregistry.azurecr.io/semantic-search-api:latest \
  --target-port 8080 \
  --ingress external \
  --min-replicas 1 \
  --max-replicas 10 \
  --env-vars \
    AzureOpenAI__Endpoint=https://my-openai.openai.azure.com/ \
    AzureSearch__Endpoint=https://my-search.search.windows.net \
    AzureSearch__IndexName=documents \
  --secrets \
    openai-key=keyvaultref:... \
    search-key=keyvaultref:...

# 4. Desplegar la Azure Function
cd src/SemanticSearch.Functions
func azure functionapp publish semantic-search-indexer
```

---

## Pipeline CI/CD — GitHub Actions

```yaml
# .github/workflows/deploy-api.yml
name: Deploy API

on:
  push:
    branches: [main]
    paths: ["src/SemanticSearch.Api/**", "src/SemanticSearch.Core/**"]

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
        run: dotnet test tests/SemanticSearch.Api.Tests/ --no-build

      - name: Login to Azure
        uses: azure/login@v2
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}

      - name: Build and push image
        run: |
          az acr build \
            --registry ${{ vars.ACR_NAME }} \
            --image semantic-search-api:${{ github.sha }} \
            ./src/SemanticSearch.Api

      - name: Update Container App revision
        run: |
          az containerapp update \
            --name semantic-search-api \
            --resource-group ${{ vars.RESOURCE_GROUP }} \
            --image ${{ vars.ACR_NAME }}.azurecr.io/semantic-search-api:${{ github.sha }}
```

---

## Servidor MCP — agente @doc-search

El servidor MCP está escrito en C# usando `Microsoft.Extensions.AI` y corre como
proceso local. Copilot Chat de VS Code lo detecta como participante `@doc-search`.

```csharp
// Tools/SearchDocumentsTool.cs
public class SearchDocumentsTool(HttpClient httpClient) : IMcpTool
{
    public string Name        => "search_documents";
    public string Description => "Busca documentos por pregunta en lenguaje natural";

    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct)
    {
        var query = parameters.GetProperty("query").GetString()!;
        var topK  = parameters.TryGetProperty("top_k", out var tk) ? tk.GetInt32() : 5;

        var request  = new { query, top_k = topK };
        var response = await httpClient.PostAsJsonAsync("/query", request, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
```

```json
// .vscode/settings.json — configuración del servidor MCP
{
  "github.copilot.chat.mcpServers": {
    "doc-search": {
      "command": "dotnet",
      "args": ["run", "--project", "src/SemanticSearch.McpServer"],
      "env": {
        "API_URL": "http://localhost:8080"
      }
    }
  }
}
```

**Ejemplos de uso en Copilot Chat:**
```
@doc-search ¿en qué contratos aparece la cláusula de rescisión anticipada?
@doc-search listá los documentos de la categoría legal subidos esta semana
@doc-search re-indexá el documento con id abc-123
```

---

## Equivalencias Go → C#

| Concepto          | Go (original)              | C# (este blueprint)                        |
|-------------------|----------------------------|--------------------------------------------|
| Framework HTTP    | Gin Web Framework          | ASP.NET Core 8 Minimal APIs                |
| DI container      | Manual en `main.go`        | `IServiceCollection` nativo                |
| Options           | Structs + env vars         | `IOptions<T>` con validación               |
| Middleware        | `gin.HandlerFunc`          | `IMiddleware` / `RequestDelegate`          |
| Interfaces        | `interface{}`              | `interface` con implementación explícita   |
| Goroutines        | `go func()`                | `async/await` + `CancellationToken`        |
| Error handling    | `error` como return value  | Exceptions + `ExceptionMiddleware`         |
| Logging           | `slog`                     | `ILogger<T>` con structured logging        |
| Tests             | `testing` + `testify`      | `xUnit` + `Moq` + `FluentAssertions`       |
| Entrypoint        | `cmd/main.go`              | `Program.cs`                               |
