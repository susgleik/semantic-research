using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using FluentAssertions;
using Moq;
using SemanticSearch.Core.Models;
using SemanticSearch.Core.Services;
using SemanticSearch.Functions.Query.Services;

namespace SemanticSearch.Functions.Query.Tests;

public class QueryFunctionTests
{
    private readonly Mock<IGeminiEmbeddingService> _embeddingService = new();
    private readonly Mock<ISimilaritySearchService> _similaritySearch = new();
    private readonly Mock<IRagAnswerService> _ragAnswerService = new();
    private readonly Mock<IQueryCacheService> _queryCache = new();
    private readonly Mock<ILambdaContext> _context = new();

    private QueryFunction CreateFunction() =>
        new(_embeddingService.Object, _similaritySearch.Object, _ragAnswerService.Object, _queryCache.Object);

    private static APIGatewayHttpApiV2ProxyRequest Request(string body, string? ownerId = null) => new()
    {
        Body = body,
        RequestContext = ownerId is null ? null : new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
        {
            Authorizer = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription
            {
                Jwt = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription.JwtDescription
                {
                    Claims = new Dictionary<string, string> { ["sub"] = ownerId }
                }
            }
        }
    };

    [Fact]
    public async Task FunctionHandler_ValidRequest_Returns200WithAnswerAndSources()
    {
        _embeddingService
            .Setup(e => e.EmbedBatchAsync(new[] { "¿cuál es el plazo?" }, "RETRIEVAL_QUERY", default))
            .ReturnsAsync([[0.1f, 0.2f]]);

        var sources = new List<SourceChunk> { new("doc-1", "doc.pdf", "texto", 0.9f, 1) };
        _similaritySearch
            .Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), 5, "", default))
            .ReturnsAsync(sources);

        _ragAnswerService
            .Setup(r => r.GenerateAnswerAsync("¿cuál es el plazo?", sources, default))
            .ReturnsAsync("30 dias [doc.pdf]");

        var request = Request("""{"query":"¿cuál es el plazo?"}""");

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("30 dias [doc.pdf]");
        response.Body.Should().Contain("doc.pdf");
    }

    [Fact]
    public async Task FunctionHandler_MissingQuery_Returns400()
    {
        var request = Request("""{"topK":5}""");

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(400);
        _embeddingService.Verify(
            e => e.EmbedBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FunctionHandler_InvalidJson_Returns400()
    {
        var request = Request("not json");

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task FunctionHandler_CustomTopK_IsForwardedToSimilaritySearch()
    {
        _embeddingService
            .Setup(e => e.EmbedBatchAsync(It.IsAny<IEnumerable<string>>(), "RETRIEVAL_QUERY", default))
            .ReturnsAsync([[0.1f, 0.2f]]);

        int capturedTopK = 0;
        _similaritySearch
            .Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(), It.IsAny<string>(), default))
            .Callback<ReadOnlyMemory<float>, int, string, CancellationToken>((_, k, _, _) => capturedTopK = k)
            .ReturnsAsync([]);

        _ragAnswerService
            .Setup(r => r.GenerateAnswerAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<SourceChunk>>(), default))
            .ReturnsAsync("respuesta");

        var request = Request("""{"query":"pregunta","topK":10}""");

        await CreateFunction().FunctionHandler(request, _context.Object);

        capturedTopK.Should().Be(10);
    }

    [Fact]
    public async Task FunctionHandler_CacheHit_SkipsEmbeddingAndAnswerAndReturnsCachedResponse()
    {
        var cached = new QueryResponse("respuesta cacheada", []);
        _queryCache
            .Setup(c => c.GetAsync("pregunta repetida", 5, "", default))
            .ReturnsAsync(cached);

        var request = Request("""{"query":"pregunta repetida"}""");

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("respuesta cacheada");
        _embeddingService.Verify(
            e => e.EmbedBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _ragAnswerService.Verify(
            r => r.GenerateAnswerAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<SourceChunk>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FunctionHandler_CacheMiss_GeneratesAnswerAndStoresItInCache()
    {
        _queryCache
            .Setup(c => c.GetAsync("pregunta nueva", 5, "", default))
            .ReturnsAsync((QueryResponse?)null);

        _embeddingService
            .Setup(e => e.EmbedBatchAsync(It.IsAny<IEnumerable<string>>(), "RETRIEVAL_QUERY", default))
            .ReturnsAsync([[0.1f, 0.2f]]);
        _similaritySearch
            .Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), 5, "", default))
            .ReturnsAsync([]);
        _ragAnswerService
            .Setup(r => r.GenerateAnswerAsync("pregunta nueva", It.IsAny<IReadOnlyList<SourceChunk>>(), default))
            .ReturnsAsync("respuesta nueva");

        var request = Request("""{"query":"pregunta nueva"}""");

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("respuesta nueva");
        _queryCache.Verify(
            c => c.SetAsync(
                "pregunta nueva", 5, "",
                It.Is<QueryResponse>(r => r.Answer == "respuesta nueva"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FunctionHandler_CacheWriteFails_StillReturnsGeneratedAnswer()
    {
        _context.Setup(c => c.Logger).Returns(Mock.Of<ILambdaLogger>());

        _queryCache
            .Setup(c => c.GetAsync("pregunta nueva", 5, "", default))
            .ReturnsAsync((QueryResponse?)null);
        _queryCache
            .Setup(c => c.SetAsync("pregunta nueva", 5, "", It.IsAny<QueryResponse>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AccessDenied"));

        _embeddingService
            .Setup(e => e.EmbedBatchAsync(It.IsAny<IEnumerable<string>>(), "RETRIEVAL_QUERY", default))
            .ReturnsAsync([[0.1f, 0.2f]]);
        _similaritySearch
            .Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), 5, "", default))
            .ReturnsAsync([]);
        _ragAnswerService
            .Setup(r => r.GenerateAnswerAsync("pregunta nueva", It.IsAny<IReadOnlyList<SourceChunk>>(), default))
            .ReturnsAsync("respuesta nueva");

        var request = Request("""{"query":"pregunta nueva"}""");

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("respuesta nueva");
    }

    [Fact]
    public async Task FunctionHandler_AuthenticatedCaller_ThreadsOwnerIdIntoCacheAndSearch()
    {
        _queryCache
            .Setup(c => c.GetAsync("pregunta", 5, "user-1", default))
            .ReturnsAsync((QueryResponse?)null);

        _embeddingService
            .Setup(e => e.EmbedBatchAsync(It.IsAny<IEnumerable<string>>(), "RETRIEVAL_QUERY", default))
            .ReturnsAsync([[0.1f, 0.2f]]);
        _similaritySearch
            .Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), 5, "user-1", default))
            .ReturnsAsync([]);
        _ragAnswerService
            .Setup(r => r.GenerateAnswerAsync("pregunta", It.IsAny<IReadOnlyList<SourceChunk>>(), default))
            .ReturnsAsync("respuesta");

        var request = Request("""{"query":"pregunta"}""", ownerId: "user-1");

        await CreateFunction().FunctionHandler(request, _context.Object);

        _similaritySearch.Verify(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), 5, "user-1", default), Times.Once);
        _queryCache.Verify(
            c => c.SetAsync("pregunta", 5, "user-1", It.IsAny<QueryResponse>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
