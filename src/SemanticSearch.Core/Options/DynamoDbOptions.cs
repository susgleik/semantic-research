using System.ComponentModel.DataAnnotations;

namespace SemanticSearch.Core.Options;

public class DynamoDbOptions
{
    [Required] public string TableName { get; set; } = "chunks";
    [Required] public string Region    { get; set; } = "us-east-1";

    /// <summary>Override para DynamoDB Local/LocalStack en desarrollo; null en AWS real.</summary>
    public string? ServiceUrl { get; set; }
}
