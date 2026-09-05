using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3;
using SemanticSearch.Core.Auth;
using SemanticSearch.Core.Models;
using SemanticSearch.Core.Options;
using SemanticSearch.Functions.Reports.Services;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SemanticSearch.Functions.Reports;

public class ReportFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IReportChunkReader _chunkReader;
    private readonly IReportGeneratorService _generator;
    private readonly IReportStorageService _storage;

    public ReportFunction() : this(BuildChunkReader(), BuildGenerator(), BuildStorage())
    {
    }

    public ReportFunction(
        IReportChunkReader chunkReader, IReportGeneratorService generator, IReportStorageService storage)
    {
        _chunkReader = chunkReader;
        _generator   = generator;
        _storage     = storage;
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        var method = request.RequestContext?.Http?.Method ?? "GET";
        var path   = request.RawPath ?? "";

        if (method == "POST" && path == "/reports")
            return await HandleCreateReportAsync(request);

        if (method == "GET" && path.StartsWith("/reports/") && path != "/reports")
            return await HandleGetReportAsync(ExtractReportId(request, path));

        return NotFound();
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> HandleCreateReportAsync(
        APIGatewayHttpApiV2ProxyRequest request)
    {
        ReportRequest? reportRequest;
        try
        {
            reportRequest = JsonSerializer.Deserialize<ReportRequest>(request.Body ?? string.Empty, JsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest("El body debe ser JSON válido.");
        }

        if (reportRequest is null || string.IsNullOrWhiteSpace(reportRequest.Scenario))
            return BadRequest("El campo 'scenario' es obligatorio.");

        if (!ReportScenarios.IsKnown(reportRequest.Scenario))
            return BadRequest($"Escenario desconocido. Valores válidos: {string.Join(", ", ReportScenarios.Known)}.");

        if (reportRequest.Scenario == ReportScenarios.Compare && (reportRequest.DocumentIds?.Count ?? 0) != 2)
            return BadRequest("El escenario 'compare' requiere exactamente 2 elementos en 'documentIds'.");

        if (reportRequest.Scenario == ReportScenarios.Custom && string.IsNullOrWhiteSpace(reportRequest.Instruction))
            return BadRequest("El escenario 'custom' requiere el campo 'instruction'.");

        var ownerId = CallerIdentity.GetOwnerId(request);
        var chunks = await _chunkReader.GetAllChunksAsync();
        var reportText = await _generator.GenerateReportAsync(reportRequest, chunks, ownerId);

        var reportId = Guid.NewGuid().ToString();
        await _storage.SaveReportAsync(reportId, reportText);
        var downloadUrl = await _storage.GetDownloadUrlAsync(reportId);

        return Created(new ReportResponse(reportId, "ready", downloadUrl));
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> HandleGetReportAsync(string reportId)
    {
        var downloadUrl = await _storage.GetDownloadUrlAsync(reportId);
        if (downloadUrl is null)
            return NotFound();

        return Ok(new ReportResponse(reportId, "ready", downloadUrl));
    }

    private static string ExtractReportId(APIGatewayHttpApiV2ProxyRequest request, string path) =>
        request.PathParameters is not null && request.PathParameters.TryGetValue("reportId", out var reportId)
            ? reportId
            : path["/reports/".Length..].Split('/')[0];

    private static APIGatewayHttpApiV2ProxyResponse Ok(object body) => JsonResponse(200, body);
    private static APIGatewayHttpApiV2ProxyResponse Created(object body) => JsonResponse(201, body);
    private static APIGatewayHttpApiV2ProxyResponse BadRequest(string error) => JsonResponse(400, new { error });
    private static APIGatewayHttpApiV2ProxyResponse NotFound() => JsonResponse(404, new { error = "Reporte no encontrado." });

    private static APIGatewayHttpApiV2ProxyResponse JsonResponse(int statusCode, object body) => new()
    {
        StatusCode = statusCode,
        Headers    = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body       = JsonSerializer.Serialize(body, JsonOptions)
    };

    private static DynamoDbOptions LoadDynamoDbOptions() => new()
    {
        TableName  = Environment.GetEnvironmentVariable("DYNAMODB_TABLE_NAME") ?? "chunks",
        Region     = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
        ServiceUrl = Environment.GetEnvironmentVariable("DYNAMODB_SERVICE_URL")
    };

    private static S3Options LoadS3ReportsOptions() => new()
    {
        BucketName = Environment.GetEnvironmentVariable("S3_BUCKET_REPORTS") ?? "reports",
        Region     = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
        ServiceUrl = Environment.GetEnvironmentVariable("S3_SERVICE_URL")
    };

    private static GeminiOptions LoadGeminiOptions() => new()
    {
        ApiKey              = GeminiSecretLoader.ApiKey,
        EmbeddingModel      = Environment.GetEnvironmentVariable("GEMINI_EMBEDDING_MODEL") ?? "gemini-embedding-001",
        ChatModel           = Environment.GetEnvironmentVariable("GEMINI_CHAT_MODEL") ?? "gemini-flash-latest",
        EmbeddingDimensions = int.TryParse(Environment.GetEnvironmentVariable("GEMINI_EMBEDDING_DIMENSIONS"), out var d) ? d : 768
    };

    private static IReportChunkReader BuildChunkReader()
    {
        var options = LoadDynamoDbOptions();
        var dynamoClient = string.IsNullOrEmpty(options.ServiceUrl)
            ? new AmazonDynamoDBClient(Amazon.RegionEndpoint.GetBySystemName(options.Region))
            : new AmazonDynamoDBClient(new AmazonDynamoDBConfig { ServiceURL = options.ServiceUrl });

        var context = new DynamoDBContext(dynamoClient);
        return new ReportChunkReader(context, options);
    }

    private static IReportGeneratorService BuildGenerator() =>
        new ReportGeneratorService(new ReportChatService(SharedHttpClient, LoadGeminiOptions()));

    private static IReportStorageService BuildStorage()
    {
        var options = LoadS3ReportsOptions();
        var s3Client = string.IsNullOrEmpty(options.ServiceUrl)
            ? new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(options.Region))
            : new AmazonS3Client(new AmazonS3Config { ServiceURL = options.ServiceUrl, ForcePathStyle = true });

        // Cliente separado solo para firmar la URL de descarga: "S3_SERVICE_URL" (host interno
        // de Docker) no lo puede resolver el navegador. En AWS real no hay override de ninguno
        // de los dos, asi que ambos terminan siendo el mismo cliente contra el S3 real.
        var publicServiceUrl = Environment.GetEnvironmentVariable("S3_PUBLIC_SERVICE_URL") ?? options.ServiceUrl;
        var presignClient = string.IsNullOrEmpty(publicServiceUrl)
            ? s3Client
            : new AmazonS3Client(new AmazonS3Config { ServiceURL = publicServiceUrl, ForcePathStyle = true });

        return new ReportStorageService(s3Client, presignClient, options, isLocalDev: !string.IsNullOrEmpty(publicServiceUrl));
    }

    // HttpClient estático: se reutiliza entre invocaciones dentro del mismo entorno de ejecución del Lambda.
    private static readonly HttpClient SharedHttpClient = new();
}
