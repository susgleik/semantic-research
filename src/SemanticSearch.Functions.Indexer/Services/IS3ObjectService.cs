namespace SemanticSearch.Functions.Indexer.Services;

public record S3ObjectContent(byte[] Content, string OwnerId);

public interface IS3ObjectService
{
    Task<S3ObjectContent> DownloadAsync(string bucket, string key, CancellationToken ct = default);
    Task MoveToFailedAsync(string bucket, string key, CancellationToken ct = default);
}
