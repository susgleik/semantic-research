namespace SemanticSearch.Core.Models;

public record IndexedDocument(
    string DocId,
    string Filename,
    string Category,
    string BlobUrl,
    string Status,
    DateTimeOffset IndexedAt
);
