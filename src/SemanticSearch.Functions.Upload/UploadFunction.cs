using System.Text.Json;
using System.Text.Json.Nodes;
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

    // Este Lambda también hace de trigger pre-signup de Cognito (no hay presupuesto
    // para sumar un Lambda nuevo solo para eso — ver infra/cognito.tf). Cognito y
    // API Gateway invocan el mismo handler configurado, así que hay que distinguir
    // el evento por forma: los eventos de Cognito traen "triggerSource", los de
    // API Gateway HTTP API v2 no.
    public async Task<object> FunctionHandler(JsonElement input, ILambdaContext context)
    {
        if (input.TryGetProperty("triggerSource", out var triggerSource) &&
            (triggerSource.GetString() ?? "").StartsWith("PreSignUp_", StringComparison.Ordinal))
        {
            return HandleCognitoPreSignUp(input);
        }

        var request = JsonSerializer.Deserialize<APIGatewayHttpApiV2ProxyRequest>(input.GetRawText(), JsonOptions)!;
        return await HandleUploadAsync(request);
    }

    private static JsonNode HandleCognitoPreSignUp(JsonElement input)
    {
        // Sin SES verificado el correo de confirmación de Cognito nunca llega, así
        // que se autoconfirma la cuenta y se autoverifica el email para no bloquear
        // el registro (comportamiento temporal — ver comentario en infra/cognito.tf).
        var node = JsonNode.Parse(input.GetRawText())!.AsObject();
        var response = (node["response"] as JsonObject) ?? new JsonObject();
        node["response"] = response;

        response["autoConfirmUser"] = true;
        if (node["request"]?["userAttributes"]?["email"] is not null)
            response["autoVerifyEmail"] = true;

        return node;
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> HandleUploadAsync(APIGatewayHttpApiV2ProxyRequest request)
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
        // La URL prefirmada la consume el navegador, no este Lambda (nunca llama a S3
        // de verdad, solo firma). "S3_SERVICE_URL" (http://localstack:4566) solo resuelve
        // dentro de --docker-network; "S3_PUBLIC_SERVICE_URL" es el host alcanzable desde
        // fuera de la red Docker (http://localhost:4566). En AWS real ninguna de las dos existe.
        ServiceUrl = Environment.GetEnvironmentVariable("S3_PUBLIC_SERVICE_URL")
                     ?? Environment.GetEnvironmentVariable("S3_SERVICE_URL")
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
