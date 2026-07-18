using System.ComponentModel.DataAnnotations;

namespace SemanticSearch.Core.Options;

public class GeminiOptions
{
    [Required] public string ApiKey             { get; set; } = "";
    [Required] public string EmbeddingModel     { get; set; } = "gemini-embedding-001";
    [Required] public string ChatModel          { get; set; } = "gemini-flash-latest";
    public int EmbeddingDimensions              { get; set; } = 768;
}
