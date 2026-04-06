using SemanticSearch.Api.Models;

namespace SemanticSearch.Api.Services;

public interface ISearchService
{
    Task<IReadOnlyList<SourceChunk>> HybridSearchAsync(
        string query,
        ReadOnlyMemory<float> vector,
        int topK,
        string? filter,
        CancellationToken ct = default);
}
