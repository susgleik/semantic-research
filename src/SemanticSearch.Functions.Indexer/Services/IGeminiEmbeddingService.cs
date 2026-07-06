namespace SemanticSearch.Functions.Indexer.Services;

public interface IGeminiEmbeddingService
{
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts, string taskType, CancellationToken ct = default);
}
