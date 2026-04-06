using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using SemanticSearch.Api.Models;

namespace SemanticSearch.Api.Services;

public class SearchService(
    SearchClient searchClient,
    ILogger<SearchService> logger) : ISearchService
{
    public async Task<IReadOnlyList<SourceChunk>> HybridSearchAsync(
        string query,
        ReadOnlyMemory<float> vector,
        int topK,
        string? filter,
        CancellationToken ct = default)
    {
        var options = new SearchOptions
        {
            Size = topK,
            Filter = filter,
            Select = { "doc_id", "filename", "text", "page" },
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(vector)
                    {
                        KNearestNeighborsCount = topK,
                        Fields = { "embedding" }
                    }
                }
            },
            SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = "default",
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive)
            }
        };

        var response = await searchClient.SearchAsync<SearchDocument>(query, options, ct);

        var chunks = new List<SourceChunk>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            chunks.Add(new SourceChunk(
                DocId:    result.Document["doc_id"].ToString()!,
                Filename: result.Document["filename"].ToString()!,
                Chunk:    result.Document["text"].ToString()!,
                Score:    result.Score ?? 0,
                Page:     Convert.ToInt32(result.Document["page"])
            ));
        }

        logger.LogInformation("Hybrid search returned {Count} chunks", chunks.Count);
        return chunks;
    }
}
