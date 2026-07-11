using Amazon.DynamoDBv2.DataModel;
using SemanticSearch.Core.Models;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Functions.Documents.Services;

public class DocumentRegistryService(IDynamoDBContext context, DynamoDbOptions options) : IDocumentRegistryService
{
    public async Task<(IReadOnlyList<DocumentSummary> Documents, int Total)> ListDocumentsAsync(
        int limit, int offset, CancellationToken ct = default)
    {
        // Scan completo de la tabla de chunks — no existe una tabla "documents" separada,
        // se agrupa por DocumentId en memoria. Viable para un corpus académico; ver
        // docs/architecture.md para el trade-off frente a un vector store gestionado.
        var config = new DynamoDBOperationConfig { OverrideTableName = options.TableName };
        var chunks = await context.ScanAsync<ChunkRecord>([], config).GetRemainingAsync(ct);

        return GroupAndPaginate(chunks, limit, offset);
    }

    /// <summary>Agrupa chunks en resúmenes de documento y pagina el resultado. Extraído para poder testearse sin el SDK de DynamoDB.</summary>
    public static (IReadOnlyList<DocumentSummary> Documents, int Total) GroupAndPaginate(
        IReadOnlyList<ChunkRecord> chunks, int limit, int offset)
    {
        var documents = chunks
            .GroupBy(c => c.DocumentId)
            .Select(group =>
            {
                var first = group.OrderBy(c => c.CreatedAt).First();
                return new DocumentSummary(
                    DocId: group.Key,
                    Filename: first.Filename,
                    Category: first.Category,
                    Status: group.Any(c => c.Status == "failed") ? "failed" : "indexed",
                    ChunkCount: group.Count(),
                    IndexedAt: first.CreatedAt);
            })
            .OrderByDescending(d => d.IndexedAt)
            .ToList();

        var page = documents.Skip(offset).Take(limit).ToList();
        return (page, documents.Count);
    }

    public async Task<IReadOnlyList<ChunkRecord>> GetChunksAsync(string docId, CancellationToken ct = default)
    {
        var config = new DynamoDBOperationConfig { OverrideTableName = options.TableName };
        return await context.QueryAsync<ChunkRecord>(docId, config).GetRemainingAsync(ct);
    }

    public async Task DeleteDocumentAsync(string docId, CancellationToken ct = default)
    {
        var chunks = await GetChunksAsync(docId, ct);
        if (chunks.Count == 0)
            return;

        var config = new DynamoDBOperationConfig { OverrideTableName = options.TableName };
        var batchWrite = context.CreateBatchWrite<ChunkRecord>(config);
        batchWrite.AddDeleteItems(chunks);
        await batchWrite.ExecuteAsync(ct);
    }
}
