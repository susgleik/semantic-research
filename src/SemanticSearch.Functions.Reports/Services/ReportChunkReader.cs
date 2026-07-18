using Amazon.DynamoDBv2.DataModel;
using SemanticSearch.Core.Models;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Functions.Reports.Services;

public class ReportChunkReader(IDynamoDBContext context, DynamoDbOptions options) : IReportChunkReader
{
    public async Task<IReadOnlyList<ChunkRecord>> GetAllChunksAsync(CancellationToken ct = default)
    {
        // Scan completo de la tabla de chunks — mismo trade-off que query-service/documents-service
        // (no hay vector store gestionado ni tabla de documentos separada). Ver docs/architecture.md.
        var config = new DynamoDBOperationConfig { OverrideTableName = options.TableName };
        return await context.ScanAsync<ChunkRecord>([], config).GetRemainingAsync(ct);
    }
}
