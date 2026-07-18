using System.ComponentModel.DataAnnotations;

namespace SemanticSearch.Core.Options;

public class S3Options
{
    [Required] public string BucketName { get; set; } = "docs";
    [Required] public string Region     { get; set; } = "us-east-1";

    /// <summary>Override para LocalStack en desarrollo; null en AWS real.</summary>
    public string? ServiceUrl { get; set; }
}
