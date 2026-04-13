using Azure;
using Azure.AI.OpenAI;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SemanticSearch.Api.Models;
using SemanticSearch.Api.Services;
using SemanticSearch.Core.Options;
using Xunit;

namespace SemanticSearch.Api.Tests.Services;

public class RagServiceTests
{
    // Vector falso reutilizado en varios tests
    private static readonly ReadOnlyMemory<float> FakeVector =
        new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f, 0.3f });

    private readonly Mock<IEmbeddingService> _embeddings = new();
    private readonly Mock<ISearchService>    _search     = new();

    private RagService BuildSut()
    {
        var opts = Options.Create(new OpenAIOptions
        {
            Endpoint            = "https://fake.openai.azure.com/",
            ApiKey              = "fake-key",
            EmbeddingDeployment = "text-embedding-3-large",
            ChatDeployment      = "gpt-4o"
        });

        // No llegamos a usar el cliente de OpenAI en estos tests (los mocks cortan antes).
        var fakeClient = new AzureOpenAIClient(
            new Uri("https://fake.openai.azure.com/"),
            new AzureKeyCredential("fake-key"));

        return new RagService(
            _embeddings.Object,
            _search.Object,
            fakeClient,
            opts,
            Mock.Of<ILogger<RagService>>());
    }

    // ── Test 1: EmbedAsync se llama con el texto de la query ─────────────────

    [Fact]
    public async Task QueryAsync_EmbedAsync_IsCalledWithQueryText()
    {
        _embeddings
            .Setup(e => e.EmbedAsync("¿cuál es el plazo de garantía?", It.IsAny<CancellationToken>()))
            .ReturnsAsync(FakeVector);

        // El search lanza para cortar la ejecución antes de llegar a OpenAI.
        _search
            .Setup(s => s.HybridSearchAsync(
                It.IsAny<string>(), It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("stop"));

        var sut = BuildSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.QueryAsync(new QueryRequest("¿cuál es el plazo de garantía?")));

        _embeddings.Verify(
            e => e.EmbedAsync("¿cuál es el plazo de garantía?", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Test 2: HybridSearchAsync recibe el vector retornado por EmbedAsync ──

    [Fact]
    public async Task QueryAsync_HybridSearch_IsCalledWithEmbeddedVectorAndTopK()
    {
        ReadOnlyMemory<float>? capturedVector = null;
        int                    capturedTopK   = 0;
        string?                capturedQuery  = null;

        _embeddings
            .Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FakeVector);

        _search
            .Setup(s => s.HybridSearchAsync(
                It.IsAny<string>(), It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ReadOnlyMemory<float>, int, string?, CancellationToken>(
                (q, v, k, _, _) => { capturedQuery = q; capturedVector = v; capturedTopK = k; })
            .ThrowsAsync(new InvalidOperationException("stop"));

        var sut = BuildSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.QueryAsync(new QueryRequest("mi pregunta", TopK: 7)));

        capturedQuery.Should().Be("mi pregunta");
        capturedTopK.Should().Be(7);
        capturedVector.Should().NotBeNull();
        capturedVector!.Value.Span.SequenceEqual(FakeVector.Span).Should().BeTrue();
    }

    // ── Test 3: Si EmbedAsync falla, el search nunca se llama ────────────────

    [Fact]
    public async Task QueryAsync_WhenEmbeddingFails_SearchIsNeverCalled()
    {
        _embeddings
            .Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding service down"));

        var sut = BuildSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.QueryAsync(new QueryRequest("query")));

        _search.Verify(
            s => s.HybridSearchAsync(
                It.IsAny<string>(), It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Test 4: Si HybridSearchAsync falla, la excepción se propaga ──────────

    [Fact]
    public async Task QueryAsync_WhenSearchFails_ExceptionPropagates()
    {
        _embeddings
            .Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FakeVector);

        _search
            .Setup(s => s.HybridSearchAsync(
                It.IsAny<string>(), It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Azure AI Search timeout"));

        var sut = BuildSut();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            sut.QueryAsync(new QueryRequest("query")));

        ex.Message.Should().Be("Azure AI Search timeout");
    }

    // ── Test 5: Filter se pasa correctamente a HybridSearchAsync ─────────────

    [Fact]
    public async Task QueryAsync_Filter_IsForwardedToSearch()
    {
        string? capturedFilter = "not-set";

        _embeddings
            .Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FakeVector);

        _search
            .Setup(s => s.HybridSearchAsync(
                It.IsAny<string>(), It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ReadOnlyMemory<float>, int, string?, CancellationToken>(
                (_, _, _, f, _) => capturedFilter = f)
            .ThrowsAsync(new InvalidOperationException("stop"));

        var sut = BuildSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.QueryAsync(new QueryRequest("query", Filter: "category eq 'legal'")));

        capturedFilter.Should().Be("category eq 'legal'");
    }
}
