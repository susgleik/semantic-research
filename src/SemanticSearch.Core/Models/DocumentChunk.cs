namespace SemanticSearch.Core.Models;

public record DocumentChunk(
    string DocId,
    string Filename,
    string Text,
    int StartIndex,
    int WordCount,
    int Page = 0
);
