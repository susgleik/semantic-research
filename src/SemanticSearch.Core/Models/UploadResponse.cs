namespace SemanticSearch.Core.Models;

public record UploadResponse(
    string DocId,
    string Filename,
    string Status,
    string UploadUrl
);
