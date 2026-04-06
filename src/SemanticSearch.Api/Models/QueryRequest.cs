using System.ComponentModel.DataAnnotations;

namespace SemanticSearch.Api.Models;

public record QueryRequest(
    [Required] string Query,
    int TopK = 5,
    string? Filter = null,
    string Language = "es"
);
