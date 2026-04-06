using FluentAssertions;
using SemanticSearch.Functions.Services;
using Xunit;

namespace SemanticSearch.Functions.Tests;

public class DocumentIndexerTests
{
    [Fact]
    public void ChunkerService_SlidingWindow_ReturnsCorrectChunkCount()
    {
        var chunker = new ChunkerService();
        var text = string.Join(" ", Enumerable.Range(1, 600).Select(i => $"word{i}"));

        var chunks = chunker.SlidingWindow(text, windowSize: 512, overlap: 64);

        chunks.Should().NotBeEmpty();
        chunks[0].WordCount.Should().Be(512);
    }

    [Fact]
    public void ChunkerService_SlidingWindow_ShortText_ReturnsSingleChunk()
    {
        var chunker = new ChunkerService();
        var text = "hello world this is a short text";

        var chunks = chunker.SlidingWindow(text, windowSize: 512, overlap: 64);

        chunks.Should().HaveCount(1);
    }
}
