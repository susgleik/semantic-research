namespace SemanticSearch.Api.Services;

public interface IBlobService
{
    Task<string> UploadAsync(IFormFile file, string docId, string category, CancellationToken ct = default);
    Task DeleteAsync(string docId, CancellationToken ct = default);
    Task TriggerReindexAsync(string docId, CancellationToken ct = default);
}
