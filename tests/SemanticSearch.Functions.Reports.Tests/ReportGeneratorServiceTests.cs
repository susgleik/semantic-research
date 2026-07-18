using FluentAssertions;
using Moq;
using SemanticSearch.Core.Models;
using SemanticSearch.Functions.Reports.Services;

namespace SemanticSearch.Functions.Reports.Tests;

public class ReportGeneratorServiceTests
{
    private static ChunkRecord Chunk(
        string docId, string chunkId, string text, string filename = "doc.pdf",
        string category = "general", string createdAt = "2026-01-01T00:00:00Z") => new()
    {
        DocumentId = docId,
        ChunkId    = chunkId,
        Text       = text,
        Filename   = filename,
        Category   = category,
        CreatedAt  = createdAt
    };

    [Fact]
    public void FilterChunks_ByCategory_KeepsOnlyMatchingCategory()
    {
        var chunks = new List<ChunkRecord>
        {
            Chunk("doc-1", "chunk-000000", "a", category: "legal"),
            Chunk("doc-2", "chunk-000000", "b", category: "finance")
        };

        var result = ReportGeneratorService.FilterChunks(chunks, new ReportRequest("summary", Category: "legal"));

        result.Should().ContainSingle().Which.DocumentId.Should().Be("doc-1");
    }

    [Fact]
    public void FilterChunks_ByDocumentIds_KeepsOnlyRequestedDocuments()
    {
        var chunks = new List<ChunkRecord>
        {
            Chunk("doc-1", "chunk-000000", "a"),
            Chunk("doc-2", "chunk-000000", "b"),
            Chunk("doc-3", "chunk-000000", "c")
        };

        var result = ReportGeneratorService.FilterChunks(
            chunks, new ReportRequest("compare", DocumentIds: ["doc-1", "doc-3"]));

        result.Select(c => c.DocumentId).Should().BeEquivalentTo(["doc-1", "doc-3"]);
    }

    [Fact]
    public void FilterChunks_ByDateRange_ExcludesChunksOutsideRange()
    {
        var chunks = new List<ChunkRecord>
        {
            Chunk("doc-1", "chunk-000000", "a", createdAt: "2026-01-01T00:00:00Z"),
            Chunk("doc-2", "chunk-000000", "b", createdAt: "2026-06-01T00:00:00Z"),
            Chunk("doc-3", "chunk-000000", "c", createdAt: "2026-12-01T00:00:00Z")
        };

        var result = ReportGeneratorService.FilterChunks(
            chunks, new ReportRequest("summary", DateFrom: "2026-02-01T00:00:00Z", DateTo: "2026-11-01T00:00:00Z"));

        result.Should().ContainSingle().Which.DocumentId.Should().Be("doc-2");
    }

    [Fact]
    public async Task GenerateReportAsync_NoMatchingChunks_ReturnsMessageWithoutCallingChat()
    {
        var chatService = new Mock<IReportChatService>();
        var generator = new ReportGeneratorService(chatService.Object);

        var result = await generator.GenerateReportAsync(
            new ReportRequest("summary", Category: "nonexistent"), []);

        result.Should().Be("No hay documentos que coincidan con los filtros indicados.");
        chatService.Verify(c => c.GenerateAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task GenerateReportAsync_MapReducesOnceperDocumentThenCombines()
    {
        var chunks = new List<ChunkRecord>
        {
            Chunk("doc-1", "chunk-000001", "segundo fragmento"),
            Chunk("doc-1", "chunk-000000", "primer fragmento"),
            Chunk("doc-2", "chunk-000000", "otro documento", filename: "otro.pdf")
        };

        var chatService = new Mock<IReportChatService>();
        chatService
            .SetupSequence(c => c.GenerateAsync(It.IsAny<string>(), default))
            .ReturnsAsync("resumen doc-1")
            .ReturnsAsync("resumen doc-2")
            .ReturnsAsync("informe final");

        var generator = new ReportGeneratorService(chatService.Object);

        var result = await generator.GenerateReportAsync(new ReportRequest("summary"), chunks);

        result.Should().Be("informe final");
        // 2 llamadas map (una por documento) + 1 reduce = 3 en total.
        chatService.Verify(c => c.GenerateAsync(It.IsAny<string>(), default), Times.Exactly(3));
    }

    [Fact]
    public async Task GenerateReportAsync_MapPrompt_PreservesChunkOrderWithinDocument()
    {
        var chunks = new List<ChunkRecord>
        {
            Chunk("doc-1", "chunk-000001", "segundo"),
            Chunk("doc-1", "chunk-000000", "primero")
        };

        string? capturedMapPrompt = null;
        var callCount = 0;
        var chatService = new Mock<IReportChatService>();
        chatService
            .Setup(c => c.GenerateAsync(It.IsAny<string>(), default))
            .Callback<string, CancellationToken>((prompt, _) =>
            {
                if (callCount == 0)
                    capturedMapPrompt = prompt;
                callCount++;
            })
            .ReturnsAsync("resumen");

        var generator = new ReportGeneratorService(chatService.Object);
        await generator.GenerateReportAsync(new ReportRequest("summary"), chunks);

        capturedMapPrompt.Should().NotBeNull();
        capturedMapPrompt!.IndexOf("primero", StringComparison.Ordinal)
            .Should().BeLessThan(capturedMapPrompt.IndexOf("segundo", StringComparison.Ordinal));
    }
}
