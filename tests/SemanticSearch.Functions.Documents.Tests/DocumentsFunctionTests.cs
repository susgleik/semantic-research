using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using FluentAssertions;
using Moq;
using SemanticSearch.Core.Models;
using SemanticSearch.Functions.Documents.Services;

namespace SemanticSearch.Functions.Documents.Tests;

public class DocumentsFunctionTests
{
    private readonly Mock<IDocumentRegistryService> _registry = new();
    private readonly Mock<IS3DocumentService> _s3DocumentService = new();
    private readonly Mock<ILambdaContext> _context = new();

    private DocumentsFunction CreateFunction() => new(_registry.Object, _s3DocumentService.Object);

    private static APIGatewayHttpApiV2ProxyRequest Request(
        string method, string path, Dictionary<string, string>? pathParams = null, Dictionary<string, string>? query = null) => new()
    {
        RawPath = path,
        RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
        {
            Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Method = method }
        },
        PathParameters = pathParams,
        QueryStringParameters = query
    };

    private static ChunkRecord Chunk(string docId, string category, string filename) => new()
    {
        DocumentId = docId,
        ChunkId    = "chunk-000001",
        Text       = "texto",
        Embedding  = [0.1f],
        Filename   = filename,
        Category   = category,
        Status     = "indexed",
        CreatedAt  = "2026-01-01T00:00:00Z"
    };

    [Fact]
    public async Task FunctionHandler_Health_Returns200()
    {
        var response = await CreateFunction().FunctionHandler(Request("GET", "/health"), _context.Object);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("\"status\":\"ok\"");
    }

    [Fact]
    public async Task FunctionHandler_ListDocuments_UsesDefaultPagination()
    {
        _registry
            .Setup(r => r.ListDocumentsAsync(20, 0, default))
            .ReturnsAsync((new List<DocumentSummary> { new("doc-1", "a.pdf", "contratos", "indexed", 3, "2026-01-01T00:00:00Z") }, 1));

        var response = await CreateFunction().FunctionHandler(Request("GET", "/documents"), _context.Object);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("doc-1");
        _registry.Verify(r => r.ListDocumentsAsync(20, 0, default), Times.Once);
    }

    [Fact]
    public async Task FunctionHandler_ListDocuments_ForwardsCustomLimitAndOffset()
    {
        _registry
            .Setup(r => r.ListDocumentsAsync(5, 10, default))
            .ReturnsAsync((new List<DocumentSummary>(), 0));

        var query = new Dictionary<string, string> { ["limit"] = "5", ["offset"] = "10" };
        await CreateFunction().FunctionHandler(Request("GET", "/documents", query: query), _context.Object);

        _registry.Verify(r => r.ListDocumentsAsync(5, 10, default), Times.Once);
    }

    [Fact]
    public async Task FunctionHandler_Reindex_DocumentFound_TriggersS3CopyAndReturns202()
    {
        _registry
            .Setup(r => r.GetChunksAsync("doc-1", default))
            .ReturnsAsync([Chunk("doc-1", "contratos", "a.pdf")]);

        var pathParams = new Dictionary<string, string> { ["docId"] = "doc-1" };
        var response = await CreateFunction().FunctionHandler(
            Request("POST", "/reindex/doc-1", pathParams), _context.Object);

        response.StatusCode.Should().Be(202);
        _s3DocumentService.Verify(s => s.TriggerReindexAsync("contratos", "doc-1", "a.pdf", default), Times.Once);
    }

    [Fact]
    public async Task FunctionHandler_Reindex_DocumentNotFound_Returns404()
    {
        _registry.Setup(r => r.GetChunksAsync("doc-x", default)).ReturnsAsync([]);

        var pathParams = new Dictionary<string, string> { ["docId"] = "doc-x" };
        var response = await CreateFunction().FunctionHandler(
            Request("POST", "/reindex/doc-x", pathParams), _context.Object);

        response.StatusCode.Should().Be(404);
        _s3DocumentService.Verify(
            s => s.TriggerReindexAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default),
            Times.Never);
    }

    [Fact]
    public async Task FunctionHandler_Delete_DocumentFound_DeletesChunksAndS3Object_Returns204()
    {
        _registry
            .Setup(r => r.GetChunksAsync("doc-1", default))
            .ReturnsAsync([Chunk("doc-1", "contratos", "a.pdf")]);

        var pathParams = new Dictionary<string, string> { ["docId"] = "doc-1" };
        var response = await CreateFunction().FunctionHandler(
            Request("DELETE", "/documents/doc-1", pathParams), _context.Object);

        response.StatusCode.Should().Be(204);
        _registry.Verify(r => r.DeleteDocumentAsync("doc-1", default), Times.Once);
        _s3DocumentService.Verify(s => s.DeleteObjectAsync("contratos", "doc-1", "a.pdf", default), Times.Once);
    }

    [Fact]
    public async Task FunctionHandler_Delete_DocumentNotFound_Returns404()
    {
        _registry.Setup(r => r.GetChunksAsync("doc-x", default)).ReturnsAsync([]);

        var pathParams = new Dictionary<string, string> { ["docId"] = "doc-x" };
        var response = await CreateFunction().FunctionHandler(
            Request("DELETE", "/documents/doc-x", pathParams), _context.Object);

        response.StatusCode.Should().Be(404);
        _registry.Verify(r => r.DeleteDocumentAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task FunctionHandler_UnknownRoute_Returns404()
    {
        var response = await CreateFunction().FunctionHandler(Request("GET", "/unknown"), _context.Object);

        response.StatusCode.Should().Be(404);
    }
}
