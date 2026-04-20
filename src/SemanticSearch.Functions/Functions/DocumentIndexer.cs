using System.Text;
using Azure.Storage.Blobs;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SemanticSearch.Functions.Services;
using UglyToad.PdfPig;

namespace SemanticSearch.Functions.Functions;

public class DocumentIndexer(
    ChunkerService chunker,
    EmbeddingService embeddings,
    SearchIndexerService indexer,
    ILogger<DocumentIndexer> logger)
{
    [Function("DocumentIndexer")]
    public async Task RunAsync(
        [BlobTrigger("docs/{name}", Connection = "AzureStorageConnection")] BlobClient blobClient,
        string name,
        CancellationToken ct)
    {
        logger.LogInformation("Indexing document: {Name}", name);

        // 1. Descargar y extraer texto del blob
        var content = await blobClient.DownloadContentAsync(ct);
        var text = ExtractText(content.Value.Content.ToArray(), name);

        // 2. Dividir en chunks con sliding window
        var chunks = chunker.SlidingWindow(text, windowSize: 512, overlap: 64);
        logger.LogInformation("Created {Count} chunks for {Name}", chunks.Count, name);

        // 3. Generar embeddings en batch
        var vectors = await embeddings.EmbedBatchAsync(chunks.Select(c => c.Text), ct);

        // 4. Indexar en Azure AI Search
        var docId = Path.GetFileNameWithoutExtension(name);
        await indexer.IndexChunksAsync(docId, name, chunks, vectors, ct);

        logger.LogInformation("Document {Name} indexed successfully", name);
    }

    private static string ExtractText(byte[] content, string filename) =>
        Path.GetExtension(filename).ToLowerInvariant() switch
        {
            ".txt"  => Encoding.UTF8.GetString(content),
            ".pdf"  => ExtractPdfText(content),
            ".docx" => ExtractDocxText(content),
            _       => throw new NotSupportedException($"Unsupported file type: {filename}")
        };

    private static string ExtractPdfText(byte[] content)
    {
        using var document = PdfDocument.Open(content);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
            sb.AppendLine(string.Join(' ', page.GetWords().Select(w => w.Text)));
        return sb.ToString();
    }

    private static string ExtractDocxText(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var doc   = WordprocessingDocument.Open(stream, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;
        return string.Join('\n', body.Descendants<Paragraph>()
            .Select(p => p.InnerText)
            .Where(t => !string.IsNullOrWhiteSpace(t)));
    }
}
