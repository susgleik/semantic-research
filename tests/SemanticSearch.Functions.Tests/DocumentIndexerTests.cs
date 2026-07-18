using FluentAssertions;
using SemanticSearch.Functions.Services;
using Xunit;

namespace SemanticSearch.Functions.Tests;

public class DocumentIndexerTests
{
    private readonly ChunkerService _chunker = new();

    // ── Chunk count y tamaño ──────────────────────────────────────────────────

    [Fact]
    public void SlidingWindow_600Words_FirstChunkHas512Words()
    {
        var text   = Words(600);
        var chunks = _chunker.SlidingWindow(text, windowSize: 512, overlap: 64);

        chunks.Should().NotBeEmpty();
        chunks[0].WordCount.Should().Be(512);
    }

    [Fact]
    public void SlidingWindow_ShortText_ReturnsSingleChunk()
    {
        var chunks = _chunker.SlidingWindow("hello world this is a short text", windowSize: 512, overlap: 64);

        chunks.Should().HaveCount(1);
    }

    [Fact]
    public void SlidingWindow_ExactlyWindowSize_ReturnsSingleChunk()
    {
        var text   = Words(512);
        var chunks = _chunker.SlidingWindow(text, windowSize: 512, overlap: 64);

        chunks.Should().HaveCount(1);
        chunks[0].WordCount.Should().Be(512);
    }

    [Fact]
    public void SlidingWindow_WindowSizePlusOne_ReturnsTwoChunks()
    {
        // 513 palabras con window=512, overlap=64 → step=448 → necesita 2 chunks.
        var text   = Words(513);
        var chunks = _chunker.SlidingWindow(text, windowSize: 512, overlap: 64);

        chunks.Should().HaveCount(2);
    }

    // ── StartIndex ────────────────────────────────────────────────────────────

    [Fact]
    public void SlidingWindow_SecondChunk_StartsAtWindowMinusOverlap()
    {
        // step = windowSize - overlap = 512 - 64 = 448
        var text   = Words(600);
        var chunks = _chunker.SlidingWindow(text, windowSize: 512, overlap: 64);

        chunks[1].StartIndex.Should().Be(448);
    }

    // ── Texto vacío ───────────────────────────────────────────────────────────

    [Fact]
    public void SlidingWindow_EmptyText_ReturnsEmpty()
    {
        var chunks = _chunker.SlidingWindow(string.Empty, windowSize: 512, overlap: 64);

        chunks.Should().BeEmpty();
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static string Words(int count) =>
        string.Join(" ", Enumerable.Range(1, count).Select(i => $"word{i}"));
}
