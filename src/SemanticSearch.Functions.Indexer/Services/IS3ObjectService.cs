namespace SemanticSearch.Functions.Indexer.Services;

public interface IS3ObjectService
{
    Task<byte[]> DownloadAsync(string bucket, string key, CancellationToken ct = default);
    Task MoveToFailedAsync(string bucket, string key, CancellationToken ct = default);
}
