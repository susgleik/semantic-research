using Amazon.DynamoDBv2.DataModel;
using SemanticSearch.Core.Models;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Functions.Query.Services;

public class DynamoChunkReader(IDynamoDBContext context, DynamoDbOptions options) : IDynamoChunkReader
{
    public async Task<IReadOnlyList<ChunkRecord>> GetAllChunksAsync(CancellationToken ct = default)
    {
        // Scan completo de la tabla de chunks — viable para un corpus académico; ver
        // docs/architecture.md para el trade-off frente a un vector store gestionado.
        var config = new DynamoDBOperationConfig { OverrideTableName = options.TableName };
        return await context.ScanAsync<ChunkRecord>([], config).GetRemainingAsync(ct);
    }
}
