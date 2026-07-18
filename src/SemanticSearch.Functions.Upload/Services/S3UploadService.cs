using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Functions.Upload.Services;

public class S3UploadService(IAmazonS3 s3Client, S3Options options) : IS3UploadService
{
    private static readonly TimeSpan UploadUrlTtl = TimeSpan.FromMinutes(15);

    public async Task<(string UploadUrl, string ObjectKey)> CreatePresignedUploadAsync(
        string docId, string category, string filename, string contentType, CancellationToken ct = default)
    {
        var objectKey = $"{category}/{docId}/{filename}";

        var request = new GetPreSignedUrlRequest
        {
            BucketName  = options.BucketName,
            Key         = objectKey,
            Verb        = HttpVerb.PUT,
            Expires     = DateTime.UtcNow.Add(UploadUrlTtl),
            ContentType = contentType,
            // AmazonS3Config.UseHttp no afecta el esquema de las URLs prefirmadas en este SDK
            // (queda ignorado); hay que forzarlo acá. Solo en local (LocalStack habla HTTP
            // plano) — en AWS real (options.ServiceUrl null) se deja el default (HTTPS).
            Protocol = string.IsNullOrEmpty(options.ServiceUrl) ? Protocol.HTTPS : Protocol.HTTP
        };

        var url = await s3Client.GetPreSignedURLAsync(request);
        return (url, objectKey);
    }
}
