using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Api.Services;

public class EmbeddingService(
    AzureOpenAIClient client,
    IOptions<OpenAIOptions> opts) : IEmbeddingService
{
    private readonly EmbeddingClient _embeddingClient =
        client.GetEmbeddingClient(opts.Value.EmbeddingDeployment);

    public async Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default)
    {
        var result = await _embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats();
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        var result = await _embeddingClient.GenerateEmbeddingsAsync(texts, cancellationToken: ct);
        return result.Value.Select(e => e.ToFloats()).ToList();
    }
}
