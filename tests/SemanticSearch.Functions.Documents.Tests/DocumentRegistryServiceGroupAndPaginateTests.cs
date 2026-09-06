using FluentAssertions;
using SemanticSearch.Core.Models;
using SemanticSearch.Functions.Documents.Services;

namespace SemanticSearch.Functions.Documents.Tests;

public class DocumentRegistryServiceGroupAndPaginateTests
{
    private static ChunkRecord Chunk(string docId, string chunkId, string status, string createdAt, string ownerId = "") => new()
    {
        DocumentId = docId,
        ChunkId    = chunkId,
        Text       = "texto",
        Embedding  = [0.1f],
        Filename   = $"{docId}.pdf",
        Category   = "contratos",
        Status     = status,
        CreatedAt  = createdAt,
        OwnerId    = ownerId
    };

    [Fact]
    public void GroupAndPaginate_GroupsChunksByDocumentId_WithChunkCount()
    {
        var chunks = new List<ChunkRecord>
        {
            Chunk("doc-1", "chunk-000001", "indexed", "2026-01-01T00:00:00Z"),
            Chunk("doc-1", "chunk-000002", "indexed", "2026-01-01T00:00:01Z"),
            Chunk("doc-2", "chunk-000001", "indexed", "2026-01-02T00:00:00Z")
        };

        var (documents, total) = DocumentRegistryService.GroupAndPaginate(chunks, limit: 20, offset: 0);

        total.Should().Be(2);
        documents.Single(d => d.DocId == "doc-1").ChunkCount.Should().Be(2);
        documents.Single(d => d.DocId == "doc-2").ChunkCount.Should().Be(1);
    }

    [Fact]
    public void GroupAndPaginate_AnyFailedChunk_MarksDocumentAsFailed()
    {
        var chunks = new List<ChunkRecord>
        {
            Chunk("doc-1", "chunk-000001", "indexed", "2026-01-01T00:00:00Z"),
            Chunk("doc-1", "chunk-000002", "failed", "2026-01-01T00:00:01Z")
        };

        var (documents, _) = DocumentRegistryService.GroupAndPaginate(chunks, limit: 20, offset: 0);

        documents.Single().Status.Should().Be("failed");
    }

    [Fact]
    public void GroupAndPaginate_OrdersByIndexedAtDescending()
    {
        var chunks = new List<ChunkRecord>
        {
            Chunk("doc-old", "chunk-000001", "indexed", "2026-01-01T00:00:00Z"),
            Chunk("doc-new", "chunk-000001", "indexed", "2026-01-03T00:00:00Z"),
            Chunk("doc-mid", "chunk-000001", "indexed", "2026-01-02T00:00:00Z")
        };

        var (documents, _) = DocumentRegistryService.GroupAndPaginate(chunks, limit: 20, offset: 0);

        documents.Select(d => d.DocId).Should().Equal("doc-new", "doc-mid", "doc-old");
    }

    [Fact]
    public void GroupAndPaginate_RespectsLimitAndOffset()
    {
        var chunks = Enumerable.Range(1, 5)
            .Select(i => Chunk($"doc-{i}", "chunk-000001", "indexed", $"2026-01-0{i}T00:00:00Z"))
            .ToList();

        var (documents, total) = DocumentRegistryService.GroupAndPaginate(chunks, limit: 2, offset: 1);

        total.Should().Be(5);
        documents.Should().HaveCount(2);
        documents.Select(d => d.DocId).Should().Equal("doc-4", "doc-3");
    }

    [Fact]
    public void FilterVisible_ReturnsOwnAndLegacySharedChunks_ExcludesOtherOwners()
    {
        var chunks = new List<ChunkRecord>
        {
            Chunk("doc-mine", "chunk-000001", "indexed", "2026-01-01T00:00:00Z", ownerId: "user-1"),
            Chunk("doc-shared", "chunk-000001", "indexed", "2026-01-01T00:00:00Z", ownerId: ""),
            Chunk("doc-other", "chunk-000001", "indexed", "2026-01-01T00:00:00Z", ownerId: "user-2")
        };

        var visible = DocumentRegistryService.FilterVisible(chunks, "user-1");

        visible.Select(c => c.DocumentId).Should().BeEquivalentTo(["doc-mine", "doc-shared"]);
    }

    [Fact]
    public void FilterVisible_CallerWithoutOwnerId_SeesOnlyLegacySharedChunks()
    {
        var chunks = new List<ChunkRecord>
        {
            Chunk("doc-shared", "chunk-000001", "indexed", "2026-01-01T00:00:00Z", ownerId: ""),
            Chunk("doc-other", "chunk-000001", "indexed", "2026-01-01T00:00:00Z", ownerId: "user-2")
        };

        var visible = DocumentRegistryService.FilterVisible(chunks, "");

        visible.Select(c => c.DocumentId).Should().BeEquivalentTo(["doc-shared"]);
    }
}
