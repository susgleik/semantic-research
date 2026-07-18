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

    public async Task<IReadOnlyList<DocumentRecord>> ListDocumentsAsync(
        int skip = 0,
        int top = 20,
        CancellationToken ct = default)
    {
        // El índice almacena chunks; recuperamos suficientes para cubrir todos los documentos únicos.
        var options = new SearchOptions
        {
            Size = 1000,
            Select = { "doc_id", "filename" }
        };

        var response = await searchClient.SearchAsync<SearchDocument>("*", options, ct);

        var seen    = new HashSet<string>();
        var records = new List<DocumentRecord>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            var docId = result.Document["doc_id"].ToString()!;
            if (seen.Add(docId))
            {
                records.Add(new DocumentRecord(
                    DocId:     docId,
                    Filename:  result.Document["filename"].ToString()!,
                    Category:  string.Empty,
                    Status:    "indexed",
                    BlobUrl:   string.Empty,
                    IndexedAt: DateTimeOffset.MinValue
                ));
            }
        }

        logger.LogInformation("ListDocuments returned {Count} unique documents", records.Count);
        return records.Skip(skip).Take(top).ToList();
    }
}
