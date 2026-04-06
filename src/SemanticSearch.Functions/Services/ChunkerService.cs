namespace SemanticSearch.Functions.Services;

public class ChunkerService
{
    public record Chunk(string Text, int StartIndex, int WordCount);

    public IReadOnlyList<Chunk> SlidingWindow(string text, int windowSize = 512, int overlap = 64)
    {
        var words  = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<Chunk>();
        var step   = windowSize - overlap;

        for (int i = 0; i < words.Length; i += step)
        {
            var end       = Math.Min(i + windowSize, words.Length);
            var chunkText = string.Join(' ', words[i..end]);
            chunks.Add(new Chunk(chunkText, i, end - i));

            if (end == words.Length) break;
        }

        return chunks;
    }
}
