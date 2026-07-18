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

    Task<IReadOnlyList<DocumentRecord>> ListDocumentsAsync(
        int skip = 0,
        int top = 20,
        CancellationToken ct = default);
}
