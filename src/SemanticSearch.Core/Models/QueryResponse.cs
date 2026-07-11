namespace SemanticSearch.Core.Models;

public record QueryResponse(
    string Answer,
    IReadOnlyList<SourceChunk> Sources
);
