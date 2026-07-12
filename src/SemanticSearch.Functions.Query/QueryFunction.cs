using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
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

    public QueryFunction() : this(
        BuildEmbeddingService(),
        BuildSimilaritySearchService(),
        BuildRagAnswerService())
    {
    }

    public QueryFunction(
        IGeminiEmbeddingService embeddingService,
        ISimilaritySearchService similaritySearch,
        IRagAnswerService ragAnswerService)
    {
        _embeddingService = embeddingService;
        _similaritySearch = similaritySearch;
        _ragAnswerService = ragAnswerService;
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

        var vectors = await _embeddingService.EmbedBatchAsync(
            [queryRequest.Query], taskType: "RETRIEVAL_QUERY");
        var queryVector = vectors.FirstOrDefault() ?? [];

        var sources = await _similaritySearch.SearchAsync(queryVector, topK);
        var answer = await _ragAnswerService.GenerateAnswerAsync(queryRequest.Query, sources);

        var response = new QueryResponse(answer, sources);

        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = 200,
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            Body = JsonSerializer.Serialize(response, JsonOptions)
        };
    }

    private static APIGatewayHttpApiV2ProxyResponse BadRequest(string error) => new()
    {
        StatusCode = 400,
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body = JsonSerializer.Serialize(new { error }, JsonOptions)
    };

    private static DynamoDbOptions LoadDynamoDbOptions() => new()
    {
        TableName  = Environment.GetEnvironmentVariable("DYNAMODB_TABLE_NAME") ?? "chunks",
        Region     = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
        ServiceUrl = Environment.GetEnvironmentVariable("DYNAMODB_SERVICE_URL")
    };

    private static GeminiOptions LoadGeminiOptions() => new()
    {
        ApiKey              = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "",
        EmbeddingModel      = Environment.GetEnvironmentVariable("GEMINI_EMBEDDING_MODEL") ?? "gemini-embedding-001",
        ChatModel           = Environment.GetEnvironmentVariable("GEMINI_CHAT_MODEL") ?? "gemini-2.5-flash",
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

    // HttpClient estático: se reutiliza entre invocaciones dentro del mismo entorno de ejecución del Lambda.
    private static readonly HttpClient SharedHttpClient = new();
}
