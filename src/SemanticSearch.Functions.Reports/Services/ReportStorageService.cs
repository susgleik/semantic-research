using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Functions.Reports.Services;

/// <summary>
/// Dos clientes de S3 a propósito: <paramref name="s3Client"/> hace las llamadas reales
/// (Put/GetObjectMetadata) y en local dev debe apuntar al host interno de Docker
/// (http://localstack:4566, solo resuelve dentro de --docker-network). <paramref name="presignClient"/>
/// solo firma URLs (nunca conecta), y en local dev debe apuntar al host público que puede
/// resolver el navegador (http://localhost:4566) — si no, la URL de descarga no le sirve al
/// frontend. En AWS real ambos son el mismo endpoint (S3 real) y <paramref name="isLocalDev"/> es false.
/// </summary>
public class ReportStorageService(IAmazonS3 s3Client, IAmazonS3 presignClient, S3Options options, bool isLocalDev) : IReportStorageService
{
    private static readonly TimeSpan DownloadUrlTtl = TimeSpan.FromMinutes(15);

    public async Task SaveReportAsync(string reportId, string content, CancellationToken ct = default)
    {
        await s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName  = options.BucketName,
            Key         = BuildKey(reportId),
            ContentBody = content,
            ContentType = "text/markdown"
        }, ct);
    }

    public async Task<string?> GetDownloadUrlAsync(string reportId, CancellationToken ct = default)
    {
        var key = BuildKey(reportId);

        try
        {
            await s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = options.BucketName,
                Key        = key
            }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var request = new GetPreSignedUrlRequest
        {
            BucketName = options.BucketName,
            Key        = key,
            Verb       = HttpVerb.GET,
            Expires    = DateTime.UtcNow.Add(DownloadUrlTtl),
            // AmazonS3Config.UseHttp no afecta el esquema de las URLs prefirmadas en este SDK;
            // hay que forzarlo acá. Solo en local (LocalStack habla HTTP plano).
            Protocol = isLocalDev ? Protocol.HTTP : Protocol.HTTPS
        };

        return await presignClient.GetPreSignedURLAsync(request);
    }

    private static string BuildKey(string reportId) => $"{reportId}.md";
}
