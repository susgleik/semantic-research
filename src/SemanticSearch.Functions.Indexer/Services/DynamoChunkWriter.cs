using Amazon.DynamoDBv2.DataModel;
using SemanticSearch.Core.Models;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Functions.Indexer.Services;

public class DynamoChunkWriter(IDynamoDBContext context, DynamoDbOptions options) : IDynamoChunkWriter
{
    public async Task WriteChunksAsync(IEnumerable<ChunkRecord> chunks, CancellationToken ct = default)
    {
        var config = new DynamoDBOperationConfig { OverrideTableName = options.TableName };
        var batchWrite = context.CreateBatchWrite<ChunkRecord>(config);
        batchWrite.AddPutItems(chunks);
        await batchWrite.ExecuteAsync(ct);
    }
}
