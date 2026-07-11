using SemanticSearch.Core.Models;

namespace SemanticSearch.Functions.Query.Services;

public class SimilaritySearchService(IDynamoChunkReader chunkReader) : ISimilaritySearchService
{
    public async Task<IReadOnlyList<SourceChunk>> SearchAsync(
        ReadOnlyMemory<float> queryVector, int topK, CancellationToken ct = default)
    {
        var chunks = await chunkReader.GetAllChunksAsync(ct);

        return chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = CosineSimilarity(queryVector.Span, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(chunk.Embedding))
            })
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => new SourceChunk(
                DocId: x.Chunk.DocumentId,
                Filename: x.Chunk.Filename,
                Chunk: x.Chunk.Text,
                Score: x.Score,
                Page: x.Chunk.Page))
            .ToList();
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
            return 0f;

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
