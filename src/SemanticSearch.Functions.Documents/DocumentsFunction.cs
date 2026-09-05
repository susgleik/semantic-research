using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3;
using SemanticSearch.Core.Auth;
using SemanticSearch.Core.Models;
using SemanticSearch.Core.Options;
using SemanticSearch.Functions.Documents.Services;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SemanticSearch.Functions.Documents;

public class DocumentsFunction
{
    private const int DefaultLimit = 20;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDocumentRegistryService _registry;
    private readonly IS3DocumentService _s3DocumentService;

    public DocumentsFunction() : this(BuildRegistryService(), BuildS3DocumentService())
    {
    }

    public DocumentsFunction(IDocumentRegistryService registry, IS3DocumentService s3DocumentService)
    {
        _registry          = registry;
        _s3DocumentService = s3DocumentService;
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        var method = request.RequestContext?.Http?.Method ?? "GET";
        var path   = request.RawPath ?? "";

        if (method == "GET" && path == "/health")
            return Ok(new { status = "ok" });

        if (method == "GET" && path == "/documents")
            return await HandleListDocumentsAsync(request);

        var ownerId = CallerIdentity.GetOwnerId(request);

        if (method == "POST" && path.StartsWith("/reindex/"))
            return await HandleReindexAsync(ExtractDocId(request, path, "/reindex/"), ownerId);

        if (method == "DELETE" && path.StartsWith("/documents/") && path != "/documents")
            return await HandleDeleteAsync(ExtractDocId(request, path, "/documents/"), ownerId);

        return NotFound();
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> HandleListDocumentsAsync(
        APIGatewayHttpApiV2ProxyRequest request)
    {
        var qs = request.QueryStringParameters;
        var limit  = qs is not null && qs.TryGetValue("limit", out var l) && int.TryParse(l, out var lv) && lv > 0 ? lv : DefaultLimit;
        var offset = qs is not null && qs.TryGetValue("offset", out var o) && int.TryParse(o, out var ov) && ov >= 0 ? ov : 0;

        var ownerId = CallerIdentity.GetOwnerId(request);
        var (documents, total) = await _registry.ListDocumentsAsync(ownerId, limit, offset);
        return Ok(new DocumentListResponse(documents, total, limit, offset));
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> HandleReindexAsync(string docId, string ownerId)
    {
        var chunks = await _registry.GetChunksAsync(docId);
        if (chunks.Count == 0)
            return NotFound();

        var first = chunks[0];
        // Mismo 404 tanto si el doc no existe como si es de otro usuario específico —
        // no revelar existencia. Los legacy/compartidos (OwnerId vacío) son reindexables
        // por cualquiera, consistente con tratarlos como compartidos en la lectura.
        if (!string.IsNullOrEmpty(first.OwnerId) && first.OwnerId != ownerId)
            return NotFound();

        await _s3DocumentService.TriggerReindexAsync(first.Category, docId, first.Filename, first.OwnerId);

        return Accepted(new { docId, status = "reindexing" });
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> HandleDeleteAsync(string docId, string ownerId)
    {
        var chunks = await _registry.GetChunksAsync(docId);
        if (chunks.Count == 0)
            return NotFound();

        var first = chunks[0];
        if (!string.IsNullOrEmpty(first.OwnerId) && first.OwnerId != ownerId)
            return NotFound();

        await _registry.DeleteDocumentAsync(docId);
        await _s3DocumentService.DeleteObjectAsync(first.Category, docId, first.Filename);

        return NoContent();
    }

    private static string ExtractDocId(APIGatewayHttpApiV2ProxyRequest request, string path, string prefix) =>
        request.PathParameters is not null && request.PathParameters.TryGetValue("docId", out var docId)
            ? docId
            : path[prefix.Length..].Split('/')[0];

    private static APIGatewayHttpApiV2ProxyResponse Ok(object body) => JsonResponse(200, body);
    private static APIGatewayHttpApiV2ProxyResponse Accepted(object body) => JsonResponse(202, body);
    private static APIGatewayHttpApiV2ProxyResponse NotFound() => JsonResponse(404, new { error = "Documento no encontrado." });

    private static APIGatewayHttpApiV2ProxyResponse NoContent() => new() { StatusCode = 204 };

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

    private static S3Options LoadS3Options() => new()
    {
        BucketName = Environment.GetEnvironmentVariable("S3_BUCKET_DOCS") ?? "docs",
        Region     = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
        ServiceUrl = Environment.GetEnvironmentVariable("S3_SERVICE_URL")
    };

    private static IDocumentRegistryService BuildRegistryService()
    {
        var options = LoadDynamoDbOptions();
        var dynamoClient = string.IsNullOrEmpty(options.ServiceUrl)
            ? new AmazonDynamoDBClient(Amazon.RegionEndpoint.GetBySystemName(options.Region))
            : new AmazonDynamoDBClient(new AmazonDynamoDBConfig { ServiceURL = options.ServiceUrl });

        var context = new DynamoDBContext(dynamoClient);
        return new DocumentRegistryService(context, options);
    }

    private static IS3DocumentService BuildS3DocumentService()
    {
        var options = LoadS3Options();
        var s3Client = string.IsNullOrEmpty(options.ServiceUrl)
            ? new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(options.Region))
            : new AmazonS3Client(new AmazonS3Config { ServiceURL = options.ServiceUrl, ForcePathStyle = true });

        return new S3DocumentService(s3Client, options);
    }
}
