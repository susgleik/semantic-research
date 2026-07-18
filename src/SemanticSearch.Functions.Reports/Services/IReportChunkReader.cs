using SemanticSearch.Core.Models;

namespace SemanticSearch.Functions.Reports.Services;

public interface IReportChunkReader
{
    Task<IReadOnlyList<ChunkRecord>> GetAllChunksAsync(CancellationToken ct = default);
}
