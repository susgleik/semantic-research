using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Functions.Services;

public class EmbeddingService(
    AzureOpenAIClient client,
    IOptions<OpenAIOptions> opts)
{
    private readonly EmbeddingClient _embeddingClient =
        client.GetEmbeddingClient(opts.Value.EmbeddingDeployment);

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        var result = await _embeddingClient.GenerateEmbeddingsAsync(texts, cancellationToken: ct);
        return result.Value.Select(e => e.ToFloats()).ToList();
    }
}
