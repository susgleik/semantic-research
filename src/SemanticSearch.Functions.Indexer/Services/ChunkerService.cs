using SemanticSearch.Core.Models;

namespace SemanticSearch.Functions.Indexer.Services;

public class ChunkerService
{
    public IReadOnlyList<DocumentChunk> SlidingWindow(
        string docId, string filename, string text, int windowSize = 512, int overlap = 64)
    {
        var words  = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<DocumentChunk>();
        var step   = windowSize - overlap;

        for (var i = 0; i < words.Length; i += step)
        {
            var end       = Math.Min(i + windowSize, words.Length);
            var chunkText = string.Join(' ', words[i..end]);
            chunks.Add(new DocumentChunk(docId, filename, chunkText, i, end - i));

            if (end == words.Length) break;
        }

        return chunks;
    }
}
