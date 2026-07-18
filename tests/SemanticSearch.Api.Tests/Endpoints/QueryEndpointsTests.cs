using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Moq;
using SemanticSearch.Api.Models;
using Xunit;

namespace SemanticSearch.Api.Tests.Endpoints;

public class QueryEndpointsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient               _client;

    public QueryEndpointsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── 200 OK ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostQuery_WithValidRequest_Returns200AndAnswer()
    {
        var fakeChunks = new List<SourceChunk>
        {
            new("doc-1", "contrato.pdf", "texto del fragmento", 0.95, 1)
        };
        var fakeResponse = new QueryResponse("La respuesta es 42.", fakeChunks);

        _factory.RagService
            .Setup(r => r.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeResponse);

        var response = await _client.PostAsJsonAsync("/query", new { query = "¿cuál es la respuesta?" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<QueryResponse>();
        body!.Answer.Should().Be("La respuesta es 42.");
        body.Sources.Should().HaveCount(1);
    }

    // ── El request llega a RagService con los parámetros correctos ──────────

    [Fact]
    public async Task PostQuery_ForwardsTopKAndFilterToRagService()
    {
        QueryRequest? captured = null;

        _factory.RagService
            .Setup(r => r.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .Callback<QueryRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new QueryResponse("ok", []));

        await _client.PostAsJsonAsync("/query", new
        {
            query  = "mi pregunta",
            topK   = 7,
            filter = "category eq 'legal'"
        });

        captured.Should().NotBeNull();
        captured!.Query.Should().Be("mi pregunta");
        captured.TopK.Should().Be(7);
        captured.Filter.Should().Be("category eq 'legal'");
    }

    // ── 500 cuando el servicio lanza excepción ────────────────────────────────

    [Fact]
    public async Task PostQuery_WhenRagServiceThrows_Returns500()
    {
        _factory.RagService
            .Setup(r => r.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("upstream failure"));

        var response = await _client.PostAsJsonAsync("/query", new { query = "test" });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }
}
