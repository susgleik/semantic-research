namespace SemanticSearch.Api.Models;

public record QueryResponse(
    string Answer,
    IReadOnlyList<SourceChunk> Sources
);
