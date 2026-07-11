using System.ComponentModel.DataAnnotations;

namespace SemanticSearch.Core.Models;

public record QueryRequest(
    [Required] string Query,
    int TopK = 5
);
