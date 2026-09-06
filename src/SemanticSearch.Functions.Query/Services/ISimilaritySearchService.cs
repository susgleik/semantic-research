using SemanticSearch.Core.Models;

namespace SemanticSearch.Functions.Query.Services;

public interface ISimilaritySearchService
{
    Task<IReadOnlyList<SourceChunk>> SearchAsync(
        ReadOnlyMemory<float> queryVector, int topK, string ownerId, CancellationToken ct = default);
}
