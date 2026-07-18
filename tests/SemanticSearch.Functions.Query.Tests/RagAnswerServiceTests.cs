using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SemanticSearch.Core.Models;
using SemanticSearch.Core.Options;
using SemanticSearch.Functions.Query.Services;

namespace SemanticSearch.Functions.Query.Tests;

public class RagAnswerServiceTests
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
    public async Task GenerateAnswerAsync_SendsPromptWithQueryAndSources_ReturnsAnswerText()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var responseJson = """{"candidates":[{"content":{"parts":[{"text":"La respuesta es X [doc.pdf]"}]}}]}""";
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
        var service = new RagAnswerService(httpClient, Options());

        var chunks = new List<SourceChunk>
        {
            new("doc-1", "doc.pdf", "fragmento relevante", 0.9f, 1)
        };

        var answer = await service.GenerateAnswerAsync("¿cuál es X?", chunks);

        answer.Should().Be("La respuesta es X [doc.pdf]");

        capturedRequest!.RequestUri!.ToString().Should().Contain("gemini-2.0-flash:generateContent");
        capturedRequest.RequestUri!.ToString().Should().Contain("key=test-key");

        using var doc = JsonDocument.Parse(capturedBody!);
        var promptText = doc.RootElement
            .GetProperty("contents")[0]
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        promptText.Should().Contain("¿cuál es X?");
        promptText.Should().Contain("fragmento relevante");
        promptText.Should().Contain("doc.pdf");
    }

    [Fact]
    public async Task GenerateAnswerAsync_NoCandidatesInResponse_ReturnsFallbackMessage()
    {
        var responseJson = """{"candidates":[]}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        };

        var handler = new FakeHttpMessageHandler(response, (_, _) => { });
        var httpClient = new HttpClient(handler);
        var service = new RagAnswerService(httpClient, Options());

        var answer = await service.GenerateAnswerAsync("query", []);

        answer.Should().Be("No se pudo generar una respuesta.");
    }
}
