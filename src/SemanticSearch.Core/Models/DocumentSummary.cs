namespace SemanticSearch.Core.Models;

public record DocumentSummary(
    string DocId,
    string Filename,
    string Category,
    string Status,
    int ChunkCount,
    string IndexedAt
);
