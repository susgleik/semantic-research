using System.ComponentModel.DataAnnotations;

namespace SemanticSearch.Core.Options;

public class SearchOptions
{
    [Required] public string Endpoint  { get; set; } = "";
    [Required] public string ApiKey    { get; set; } = "";
    [Required] public string IndexName { get; set; } = "documents";
}
