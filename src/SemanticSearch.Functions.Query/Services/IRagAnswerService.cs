using SemanticSearch.Core.Models;

namespace SemanticSearch.Functions.Query.Services;

public interface IRagAnswerService
{
    Task<string> GenerateAnswerAsync(
        string query, IReadOnlyList<SourceChunk> chunks, CancellationToken ct = default);
}
