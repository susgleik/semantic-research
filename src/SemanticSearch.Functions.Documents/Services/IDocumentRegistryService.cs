using SemanticSearch.Core.Models;

namespace SemanticSearch.Functions.Documents.Services;

public interface IDocumentRegistryService
{
    Task<(IReadOnlyList<DocumentSummary> Documents, int Total)> ListDocumentsAsync(
        int limit, int offset, CancellationToken ct = default);

    Task<IReadOnlyList<ChunkRecord>> GetChunksAsync(string docId, CancellationToken ct = default);

    Task DeleteDocumentAsync(string docId, CancellationToken ct = default);
}
