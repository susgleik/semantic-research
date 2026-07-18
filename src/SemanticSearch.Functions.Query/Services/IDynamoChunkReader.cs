using SemanticSearch.Core.Models;

namespace SemanticSearch.Functions.Query.Services;

public interface IDynamoChunkReader
{
    Task<IReadOnlyList<ChunkRecord>> GetAllChunksAsync(CancellationToken ct = default);
}
