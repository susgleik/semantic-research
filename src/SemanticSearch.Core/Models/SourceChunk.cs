namespace SemanticSearch.Core.Models;

public record SourceChunk(
    string DocId,
    string Filename,
    string Chunk,
    float Score,
    int Page
);
