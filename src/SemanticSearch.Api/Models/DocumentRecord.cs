namespace SemanticSearch.Api.Models;

public record DocumentRecord(
    string DocId,
    string Filename,
    string Category,
    string Status,
    string BlobUrl,
    DateTimeOffset IndexedAt
);
