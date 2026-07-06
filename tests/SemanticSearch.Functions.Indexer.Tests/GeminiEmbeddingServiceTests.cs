using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SemanticSearch.Core.Options;
using SemanticSearch.Functions.Indexer.Services;

namespace SemanticSearch.Functions.Indexer.Tests;

public class GeminiEmbeddingServiceTests
{
    private class FakeHttpMessageHandler(HttpResponseMessage response, Action<HttpRequestMessage, string> onRequest)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            onRequest(request, body);
            return response;
        }
    }

    private static GeminiOptions Options() => new()
    {
        ApiKey = "test-key",
        EmbeddingModel = "text-embedding-004",
        ChatModel = "gemini-2.0-flash"
    };

    [Fact]
    public async Task EmbedBatchAsync_SendsBatchRequestWithTaskTypeAndModel()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var responseJson = """{"embeddings":[{"values":[0.1,0.2,0.3]},{"values":[0.4,0.5,0.6]}]}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        };

        var handler = new FakeHttpMessageHandler(response, (req, body) =>
        {
            capturedRequest = req;
            capturedBody = body;
        });
        var httpClient = new HttpClient(handler);
        var service = new GeminiEmbeddingService(httpClient, Options());

        var result = await service.EmbedBatchAsync(["texto uno", "texto dos"], "RETRIEVAL_DOCUMENT");

        result.Should().HaveCount(2);
        result[0].Should().Equal(0.1f, 0.2f, 0.3f);

        capturedRequest!.RequestUri!.ToString().Should().Contain("text-embedding-004:batchEmbedContents");
        capturedRequest.RequestUri!.ToString().Should().Contain("key=test-key");

        using var doc = JsonDocument.Parse(capturedBody!);
        var requests = doc.RootElement.GetProperty("requests");
        requests.GetArrayLength().Should().Be(2);
        requests[0].GetProperty("taskType").GetString().Should().Be("RETRIEVAL_DOCUMENT");
        requests[0].GetProperty("model").GetString().Should().Be("models/text-embedding-004");
        requests[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString().Should().Be("texto uno");
    }
}
