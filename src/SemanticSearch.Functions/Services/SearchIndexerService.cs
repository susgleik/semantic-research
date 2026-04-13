using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreOptions = SemanticSearch.Core.Options.SearchOptions;
using static SemanticSearch.Functions.Services.ChunkerService;

namespace SemanticSearch.Functions.Services;

public class SearchIndexerService(
    SearchIndexClient indexClient,
    IOptions<CoreOptions> opts,
    ILogger<SearchIndexerService> logger)
{
    private readonly string _indexName = opts.Value.IndexName;

    public async Task IndexChunksAsync(
        string docId,
        string filename,
        IReadOnlyList<Chunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> vectors,
        CancellationToken ct = default)
    {
        var searchClient = indexClient.GetSearchClient(_indexName);

        var documents = chunks.Select((chunk, i) => new SearchDocument
        {
            ["id"]        = $"{docId}-{i}",
            ["doc_id"]    = docId,
            ["filename"]  = filename,
            ["text"]      = chunk.Text,
            ["page"]      = 0,
            ["embedding"] = vectors[i].ToArray()
        }).ToList();

        var batch = IndexDocumentsBatch.Upload(documents);
        var result = await searchClient.IndexDocumentsAsync(batch, cancellationToken: ct);

        logger.LogInformation("Indexed {Count} chunks for document {DocId}", documents.Count, docId);
    }
}
