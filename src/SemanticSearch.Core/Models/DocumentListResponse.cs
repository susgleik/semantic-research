namespace SemanticSearch.Core.Models;

public record DocumentListResponse(
    IReadOnlyList<DocumentSummary> Documents,
    int Total,
    int Limit,
    int Offset
);
