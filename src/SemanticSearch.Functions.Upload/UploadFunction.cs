using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3;
using SemanticSearch.Core.Models;
using SemanticSearch.Core.Options;
using SemanticSearch.Functions.Upload.Services;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SemanticSearch.Functions.Upload;

public class UploadFunction
{
    private static readonly string[] AllowedExtensions = [".pdf", ".docx"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IS3UploadService _s3UploadService;

    public UploadFunction() : this(BuildS3UploadService(LoadS3Options()))
    {
    }

    public UploadFunction(IS3UploadService s3UploadService)
    {
        _s3UploadService = s3UploadService;
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        UploadRequest? uploadRequest;
        try
        {
            uploadRequest = JsonSerializer.Deserialize<UploadRequest>(request.Body ?? string.Empty, JsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest("El body debe ser JSON válido.");
        }

        if (uploadRequest is null || string.IsNullOrWhiteSpace(uploadRequest.Filename))
            return BadRequest("El campo 'filename' es obligatorio.");

        if (string.IsNullOrWhiteSpace(uploadRequest.Category))
            return BadRequest("El campo 'category' es obligatorio.");

        var extension = Path.GetExtension(uploadRequest.Filename).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return BadRequest($"Tipo de archivo no soportado. Permitidos: {string.Join(", ", AllowedExtensions)}");

        var docId = Guid.NewGuid().ToString();
        var (uploadUrl, _) = await _s3UploadService.CreatePresignedUploadAsync(
            docId, uploadRequest.Category, uploadRequest.Filename, uploadRequest.ContentType);

        var response = new UploadResponse(docId, uploadRequest.Filename, "pending", uploadUrl);

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

    private static S3Options LoadS3Options() => new()
    {
        BucketName = Environment.GetEnvironmentVariable("S3_BUCKET_DOCS") ?? "docs",
        Region     = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
        ServiceUrl = Environment.GetEnvironmentVariable("S3_SERVICE_URL") // solo en local (LocalStack)
    };

    private static IS3UploadService BuildS3UploadService(S3Options options)
    {
        var s3Client = string.IsNullOrEmpty(options.ServiceUrl)
            ? new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(options.Region))
            : new AmazonS3Client(new AmazonS3Config
            {
                ServiceURL     = options.ServiceUrl,
                ForcePathStyle = true // requerido por LocalStack
            });

        return new S3UploadService(s3Client, options);
    }
}
