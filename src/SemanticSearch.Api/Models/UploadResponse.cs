namespace SemanticSearch.Api.Models;

public record UploadResponse(
    string DocId,
    string Filename,
    string Status,
    string BlobUrl
);
