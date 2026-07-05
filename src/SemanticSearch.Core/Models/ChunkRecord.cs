using Amazon.DynamoDBv2.DataModel;

namespace SemanticSearch.Core.Models;

[DynamoDBTable("chunks")]
public class ChunkRecord
{
    [DynamoDBHashKey]
    public string DocumentId { get; set; } = "";

    [DynamoDBRangeKey]
    public string ChunkId { get; set; } = "";

    public string Text { get; set; } = "";

    public List<float> Embedding { get; set; } = [];

    public string Filename { get; set; } = "";

    public int Page { get; set; }

    public string Status { get; set; } = "indexed";

    public string CreatedAt { get; set; } = "";
}
