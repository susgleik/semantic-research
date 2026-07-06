using SemanticSearch.Core.Models;

namespace SemanticSearch.Functions.Indexer.Services;

public interface IDynamoChunkWriter
{
    Task WriteChunksAsync(IEnumerable<ChunkRecord> chunks, CancellationToken ct = default);
}
