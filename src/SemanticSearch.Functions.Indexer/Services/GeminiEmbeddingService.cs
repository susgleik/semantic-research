using System.Net.Http.Json;
using System.Text.Json;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Functions.Indexer.Services;

public class GeminiEmbeddingService(HttpClient httpClient, GeminiOptions options) : IGeminiEmbeddingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts, string taskType, CancellationToken ct = default)
    {
        var modelPath = $"models/{options.EmbeddingModel}";

        var requestBody = new
        {
            requests = texts.Select(text => new
            {
                model = modelPath,
                content = new { parts = new[] { new { text } } },
                taskType
            })
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/{modelPath}:batchEmbedContents?key={options.ApiKey}";

        using var response = await httpClient.PostAsJsonAsync(url, requestBody, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BatchEmbedResponse>(JsonOptions, ct);
        return result?.Embeddings.Select(e => e.Values).ToList() ?? [];
    }

    private record BatchEmbedResponse(List<EmbeddingValues> Embeddings);
    private record EmbeddingValues(float[] Values);
}
