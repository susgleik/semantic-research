using System.ComponentModel.DataAnnotations;

namespace SemanticSearch.Core.Options;

public class OpenAIOptions
{
    [Required] public string Endpoint            { get; set; } = "";
    [Required] public string ApiKey              { get; set; } = "";
    [Required] public string EmbeddingDeployment { get; set; } = "text-embedding-3-large";
    [Required] public string ChatDeployment      { get; set; } = "gpt-4o";
}
