using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using FluentAssertions;
using Moq;
using SemanticSearch.Core.Models;
using SemanticSearch.Functions.Reports.Services;

namespace SemanticSearch.Functions.Reports.Tests;

public class ReportFunctionTests
{
    private readonly Mock<IReportChunkReader> _chunkReader = new();
    private readonly Mock<IReportGeneratorService> _generator = new();
    private readonly Mock<IReportStorageService> _storage = new();
    private readonly Mock<ILambdaContext> _context = new();

    private ReportFunction CreateFunction() =>
        new(_chunkReader.Object, _generator.Object, _storage.Object);

    private static APIGatewayHttpApiV2ProxyRequest Request(
        string method, string path, string? body = null, Dictionary<string, string>? pathParams = null) => new()
    {
        RawPath = path,
        Body    = body,
        RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
        {
            Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Method = method }
        },
        PathParameters = pathParams
    };

    [Fact]
    public async Task FunctionHandler_ValidSummaryRequest_Returns201WithDownloadUrl()
    {
        _chunkReader.Setup(r => r.GetAllChunksAsync(default)).ReturnsAsync([]);
        _generator
            .Setup(g => g.GenerateReportAsync(It.IsAny<ReportRequest>(), It.IsAny<IReadOnlyList<ChunkRecord>>(), default))
            .ReturnsAsync("informe generado");
        _storage.Setup(s => s.SaveReportAsync(It.IsAny<string>(), "informe generado", default)).Returns(Task.CompletedTask);
        _storage.Setup(s => s.GetDownloadUrlAsync(It.IsAny<string>(), default)).ReturnsAsync("https://example.com/report.md");

        var request = Request("POST", "/reports", """{"scenario":"summary"}""");

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(201);
        response.Body.Should().Contain("https://example.com/report.md");
        response.Body.Should().Contain("\"status\":\"ready\"");
    }

    [Fact]
    public async Task FunctionHandler_MissingScenario_Returns400()
    {
        var request = Request("POST", "/reports", "{}");

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(400);
        _generator.Verify(
            g => g.GenerateReportAsync(It.IsAny<ReportRequest>(), It.IsAny<IReadOnlyList<ChunkRecord>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FunctionHandler_UnknownScenario_Returns400()
    {
        var request = Request("POST", "/reports", """{"scenario":"invented"}""");

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task FunctionHandler_CompareWithoutTwoDocumentIds_Returns400()
    {
        var request = Request("POST", "/reports", """{"scenario":"compare","documentIds":["doc-1"]}""");

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task FunctionHandler_CustomWithoutInstruction_Returns400()
    {
        var request = Request("POST", "/reports", """{"scenario":"custom"}""");

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task FunctionHandler_InvalidJson_Returns400()
    {
        var request = Request("POST", "/reports", "not json");

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task FunctionHandler_GetExistingReport_Returns200WithDownloadUrl()
    {
        _storage.Setup(s => s.GetDownloadUrlAsync("report-123", default)).ReturnsAsync("https://example.com/report-123.md");

        var request = Request("GET", "/reports/report-123", pathParams: new() { ["reportId"] = "report-123" });

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("https://example.com/report-123.md");
    }

    [Fact]
    public async Task FunctionHandler_GetMissingReport_Returns404()
    {
        _storage.Setup(s => s.GetDownloadUrlAsync("missing", default)).ReturnsAsync((string?)null);

        var request = Request("GET", "/reports/missing", pathParams: new() { ["reportId"] = "missing" });

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task FunctionHandler_UnknownRoute_Returns404()
    {
        var request = Request("GET", "/unknown");

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(404);
    }
}
