using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using SemanticSearch.Core.Auth;
using SemanticSearch.Core.Models;
using SemanticSearch.Core.Options;
using SemanticSearch.Core.Services;
using SemanticSearch.Functions.Query.Services;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SemanticSearch.Functions.Query;

public class QueryFunction
{
    private const int DefaultTopK = 5;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IGeminiEmbeddingService _embeddingService;
    private readonly ISimilaritySearchService _similaritySearch;
    private readonly IRagAnswerService _ragAnswerService;
    private readonly IQueryCacheService _queryCache;

    public QueryFunction() : this(
        BuildEmbeddingService(),
        BuildSimilaritySearchService(),
        BuildRagAnswerService(),
        BuildQueryCacheService())
    {
    }

    public QueryFunction(
        IGeminiEmbeddingService embeddingService,
        ISimilaritySearchService similaritySearch,
        IRagAnswerService ragAnswerService,
        IQueryCacheService queryCache)
    {
        _embeddingService = embeddingService;
        _similaritySearch = similaritySearch;
        _ragAnswerService = ragAnswerService;
        _queryCache        = queryCache;
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        QueryRequest? queryRequest;
        try
        {
            queryRequest = JsonSerializer.Deserialize<QueryRequest>(request.Body ?? string.Empty, JsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest("El body debe ser JSON válido.");
        }

        if (queryRequest is null || string.IsNullOrWhiteSpace(queryRequest.Query))
            return BadRequest("El campo 'query' es obligatorio.");

        var topK = queryRequest.TopK > 0 ? queryRequest.TopK : DefaultTopK;
        var ownerId = CallerIdentity.GetOwnerId(request);

        var cached = await _queryCache.GetAsync(queryRequest.Query, topK, ownerId);
        if (cached is not null)
            return JsonResponse(cached);

        var vectors = await _embeddingService.EmbedBatchAsync(
            [queryRequest.Query], taskType: "RETRIEVAL_QUERY");
        var queryVector = vectors.FirstOrDefault() ?? [];

        var sources = await _similaritySearch.SearchAsync(queryVector, topK, ownerId);
        var answer = await _ragAnswerService.GenerateAnswerAsync(queryRequest.Query, sources);

        var response = new QueryResponse(answer, sources);

        try
        {
            await _queryCache.SetAsync(queryRequest.Query, topK, ownerId, response);
        }
        catch (Exception ex)
        {
            // El cache es una optimización de costo, no una dependencia dura: si
            // falla (permisos, throttling), la respuesta ya generada por Gemini
            // igual se devuelve al usuario en vez de perderla.
            context.Logger.LogWarning($"No se pudo cachear la respuesta de query: {ex.Message}");
        }

        return JsonResponse(response);
    }

    private static APIGatewayHttpApiV2ProxyResponse BadRequest(string error) => new()
    {
        StatusCode = 400,
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body = JsonSerializer.Serialize(new { error }, JsonOptions)
    };

    private static APIGatewayHttpApiV2ProxyResponse JsonResponse(QueryResponse response) => new()
    {
        StatusCode = 200,
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body = JsonSerializer.Serialize(response, JsonOptions)
    };

    private static DynamoDbOptions LoadDynamoDbOptions() => new()
    {
        TableName  = Environment.GetEnvironmentVariable("DYNAMODB_TABLE_NAME") ?? "chunks",
        Region     = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
        ServiceUrl = Environment.GetEnvironmentVariable("DYNAMODB_SERVICE_URL")
    };

    private static GeminiOptions LoadGeminiOptions() => new()
    {
        ApiKey              = GeminiSecretLoader.ApiKey,
        EmbeddingModel      = Environment.GetEnvironmentVariable("GEMINI_EMBEDDING_MODEL") ?? "gemini-embedding-001",
        ChatModel           = Environment.GetEnvironmentVariable("GEMINI_CHAT_MODEL") ?? "gemini-flash-latest",
        EmbeddingDimensions = int.TryParse(Environment.GetEnvironmentVariable("GEMINI_EMBEDDING_DIMENSIONS"), out var d) ? d : 768
    };

    private static IGeminiEmbeddingService BuildEmbeddingService() =>
        new GeminiEmbeddingService(SharedHttpClient, LoadGeminiOptions());

    private static IRagAnswerService BuildRagAnswerService() =>
        new RagAnswerService(SharedHttpClient, LoadGeminiOptions());

    private static ISimilaritySearchService BuildSimilaritySearchService()
    {
        var options = LoadDynamoDbOptions();
        var dynamoClient = string.IsNullOrEmpty(options.ServiceUrl)
            ? new AmazonDynamoDBClient(Amazon.RegionEndpoint.GetBySystemName(options.Region))
            : new AmazonDynamoDBClient(new AmazonDynamoDBConfig { ServiceURL = options.ServiceUrl });

        var context = new DynamoDBContext(dynamoClient);
        return new SimilaritySearchService(new DynamoChunkReader(context, options));
    }

    private static IQueryCacheService BuildQueryCacheService()
    {
        var options = LoadDynamoDbOptions();
        var dynamoClient = string.IsNullOrEmpty(options.ServiceUrl)
            ? new AmazonDynamoDBClient(Amazon.RegionEndpoint.GetBySystemName(options.Region))
            : new AmazonDynamoDBClient(new AmazonDynamoDBConfig { ServiceURL = options.ServiceUrl });

        var context   = new DynamoDBContext(dynamoClient);
        var tableName = Environment.GetEnvironmentVariable("QUERY_CACHE_TABLE_NAME") ?? "query-cache";
        var ttlSeconds = int.TryParse(Environment.GetEnvironmentVariable("QUERY_CACHE_TTL_SECONDS"), out var ttl) ? ttl : 600;

        return new QueryCacheService(context, tableName, ttlSeconds);
    }

    // HttpClient estático: se reutiliza entre invocaciones dentro del mismo entorno de ejecución del Lambda.
    private static readonly HttpClient SharedHttpClient = new();
}
