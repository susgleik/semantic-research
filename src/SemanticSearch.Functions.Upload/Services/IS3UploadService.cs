namespace SemanticSearch.Functions.Upload.Services;

public interface IS3UploadService
{
    Task<(string UploadUrl, string ObjectKey)> CreatePresignedUploadAsync(
        string docId, string category, string filename, string contentType, CancellationToken ct = default);
}
