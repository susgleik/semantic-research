using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2.DataModel;
using SemanticSearch.Core.Models;
using SemanticSearch.Functions.Query.Models;

namespace SemanticSearch.Functions.Query.Services;

public class QueryCacheService(IDynamoDBContext context, string tableName, int ttlSeconds) : IQueryCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DynamoDBOperationConfig _config = new() { OverrideTableName = tableName };

    public async Task<QueryResponse?> GetAsync(string query, int topK, string ownerId, CancellationToken ct = default)
    {
        var record = await context.LoadAsync<QueryCacheRecord>(BuildHash(query, topK, ownerId), _config, ct);
        if (record is null)
            return null;

        if (record.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return null;

        return JsonSerializer.Deserialize<QueryResponse>(record.ResponseJson, JsonOptions);
    }

    public Task SetAsync(string query, int topK, string ownerId, QueryResponse response, CancellationToken ct = default)
    {
        var record = new QueryCacheRecord
        {
            QueryHash    = BuildHash(query, topK, ownerId),
            Query        = query,
            TopK         = topK,
            ResponseJson = JsonSerializer.Serialize(response, JsonOptions),
            ExpiresAt    = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds).ToUnixTimeSeconds()
        };

        return context.SaveAsync(record, _config, ct);
    }

    // ownerId entra en el hash a propósito: sin esto, dos usuarios que hacen la misma
    // pregunta comparten la respuesta cacheada y la respuesta de uno (basada en sus
    // documentos privados) se filtra al otro.
    private static string BuildHash(string query, int topK, string ownerId)
    {
        var normalized = $"{query.Trim().ToLowerInvariant()}|{topK}|{ownerId}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }
}
