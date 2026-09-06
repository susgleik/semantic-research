using Amazon.S3;
using Amazon.S3.Model;

namespace SemanticSearch.Functions.Indexer.Services;

public class S3ObjectService(IAmazonS3 s3Client) : IS3ObjectService
{
    public async Task<S3ObjectContent> DownloadAsync(string bucket, string key, CancellationToken ct = default)
    {
        using var response = await s3Client.GetObjectAsync(bucket, key, ct);
        using var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream, ct);

        // El SDK expone la metadata de usuario (x-amz-meta-owner-id) sin el prefijo.
        var ownerId = response.Metadata["owner-id"] ?? "";
        return new S3ObjectContent(memoryStream.ToArray(), ownerId);
    }

    public async Task MoveToFailedAsync(string bucket, string key, CancellationToken ct = default)
    {
        var failedKey = $"failed/{key}";

        await s3Client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket      = bucket,
            SourceKey         = key,
            DestinationBucket = bucket,
            DestinationKey    = failedKey
        }, ct);

        await s3Client.DeleteObjectAsync(bucket, key, ct);
    }
}
