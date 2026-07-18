using FluentAssertions;
using Moq;
using SemanticSearch.Core.Models;
using SemanticSearch.Functions.Query.Services;

namespace SemanticSearch.Functions.Query.Tests;

public class SimilaritySearchServiceTests
{
    private static ChunkRecord Chunk(string docId, string chunkId, params float[] embedding) => new()
    {
        DocumentId = docId,
        ChunkId = chunkId,
        Text = $"texto de {chunkId}",
        Embedding = embedding.ToList(),
        Filename = "doc.pdf",
        Page = 1,
        Status = "indexed",
        CreatedAt = "2026-01-01T00:00:00Z"
    };

    [Fact]
    public async Task SearchAsync_RanksChunksByCosineSimilarityDescending()
    {
        var reader = new Mock<IDynamoChunkReader>();
        reader.Setup(r => r.GetAllChunksAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Chunk("doc-1", "chunk-000001", 0f, 1f),   // ortogonal a la query → score 0
            Chunk("doc-1", "chunk-000002", 1f, 0f),   // idéntico a la query → score 1
            Chunk("doc-1", "chunk-000003", 0.7f, 0.7f) // parcialmente similar
        ]);

        var sut = new SimilaritySearchService(reader.Object);

        var result = await sut.SearchAsync(new ReadOnlyMemory<float>([1f, 0f]), topK: 3);

        result.Should().HaveCount(3);
        result[0].DocId.Should().Be("doc-1");
        result[0].Score.Should().BeApproximately(1f, 0.0001f);
        result[2].Score.Should().BeApproximately(0f, 0.0001f);
        result.Select(r => r.Score).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task SearchAsync_RespectsTopK()
    {
        var reader = new Mock<IDynamoChunkReader>();
        reader.Setup(r => r.GetAllChunksAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Chunk("doc-1", "chunk-000001", 1f, 0f),
            Chunk("doc-1", "chunk-000002", 1f, 0f),
            Chunk("doc-1", "chunk-000003", 1f, 0f)
        ]);

        var sut = new SimilaritySearchService(reader.Object);

        var result = await sut.SearchAsync(new ReadOnlyMemory<float>([1f, 0f]), topK: 2);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_NoChunks_ReturnsEmpty()
    {
        var reader = new Mock<IDynamoChunkReader>();
        reader.Setup(r => r.GetAllChunksAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var sut = new SimilaritySearchService(reader.Object);

        var result = await sut.SearchAsync(new ReadOnlyMemory<float>([1f, 0f]), topK: 5);

        result.Should().BeEmpty();
    }
}
