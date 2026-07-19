using Amazon.DynamoDBv2.DataModel;

namespace SemanticSearch.Functions.Query.Models;

[DynamoDBTable("query-cache")]
public class QueryCacheRecord
{
    [DynamoDBHashKey]
    public string QueryHash { get; set; } = "";

    public string Query { get; set; } = "";

    public int TopK { get; set; }

    public string ResponseJson { get; set; } = "";

    // Epoch seconds. Chequeado en código (expiración corta, minutos) y además
    // configurado como atributo TTL nativo de DynamoDB (limpieza async, no inmediata,
    // sirve solo como respaldo para no dejar basura acumulándose en la tabla).
    public long ExpiresAt { get; set; }
}
