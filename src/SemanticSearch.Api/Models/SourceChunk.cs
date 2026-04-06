namespace SemanticSearch.Api.Models;

public record SourceChunk(
    string DocId,
    string Filename,
    string Chunk,
    double Score,
    int Page
);
