using System.ComponentModel.DataAnnotations;

namespace SemanticSearch.Core.Options;

public class BlobOptions
{
    [Required] public string ConnectionString { get; set; } = "";
    [Required] public string Container        { get; set; } = "docs";
}
