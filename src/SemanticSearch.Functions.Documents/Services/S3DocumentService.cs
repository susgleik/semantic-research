using Amazon.S3;
using Amazon.S3.Model;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Functions.Documents.Services;

public class S3DocumentService(IAmazonS3 s3Client, S3Options options) : IS3DocumentService
{
    public async Task TriggerReindexAsync(string category, string docId, string filename, CancellationToken ct = default)
    {
        var key = BuildKey(category, docId, filename);
        await s3Client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket      = options.BucketName,
            SourceKey         = key,
            DestinationBucket = options.BucketName,
            DestinationKey    = key
        }, ct);
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
