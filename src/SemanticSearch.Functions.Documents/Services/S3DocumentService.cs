using Amazon.S3;
using Amazon.S3.Model;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Functions.Documents.Services;

public class S3DocumentService(IAmazonS3 s3Client, S3Options options) : IS3DocumentService
{
    public async Task TriggerReindexAsync(string category, string docId, string filename, string ownerId, CancellationToken ct = default)
    {
        var key = BuildKey(category, docId, filename);
        var request = new CopyObjectRequest
        {
            SourceBucket      = options.BucketName,
            SourceKey         = key,
            DestinationBucket = options.BucketName,
            DestinationKey    = key,
            // S3 rechaza un CopyObject sobre la misma key si no cambia nada — REPLACE
            // fuerza a tratarlo como una actualización válida en vez de un no-op. Pero
            // REPLACE también borra la metadata existente si no se vuelve a setear acá
            // — sin esto, cada reindex le quitaría el owner-id al documento.
            MetadataDirective = S3MetadataDirective.REPLACE
        };

        if (!string.IsNullOrEmpty(ownerId))
            request.Metadata.Add("owner-id", ownerId);

        await s3Client.CopyObjectAsync(request, ct);
    }

    public async Task DeleteObjectAsync(string category, string docId, string filename, CancellationToken ct = default)
    {
        var key = BuildKey(category, docId, filename);
        await s3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = options.BucketName,
            Key        = key
        }, ct);
    }

    private static string BuildKey(string category, string docId, string filename) => $"{category}/{docId}/{filename}";
}
