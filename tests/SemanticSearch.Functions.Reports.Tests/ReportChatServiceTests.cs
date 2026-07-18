using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SemanticSearch.Core.Options;
using SemanticSearch.Functions.Reports.Services;

namespace SemanticSearch.Functions.Reports.Tests;

public class ReportChatServiceTests
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
        EmbeddingModel = "gemini-embedding-001",
        ChatModel = "gemini-2.5-flash"
    };

    [Fact]
    public async Task GenerateAsync_SendsPrompt_ReturnsCandidateText()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var responseJson = """{"candidates":[{"content":{"parts":[{"text":"informe generado"}]}}]}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        };

        var handler = new FakeHttpMessageHandler(response, (req, body) =>
        {
            capturedRequest = req;
            capturedBody = body;
        });
        var service = new ReportChatService(new HttpClient(handler), Options());

        var result = await service.GenerateAsync("Resumí este documento");

        result.Should().Be("informe generado");
        capturedRequest!.RequestUri!.ToString().Should().Contain("gemini-2.5-flash:generateContent");
        capturedRequest.RequestUri!.ToString().Should().Contain("key=test-key");

        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()
            .Should().Be("Resumí este documento");
    }

    [Fact]
    public async Task GenerateAsync_NoCandidates_ReturnsFallbackMessage()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"candidates":[]}""", Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response, (_, _) => { });
        var service = new ReportChatService(new HttpClient(handler), Options());

        var result = await service.GenerateAsync("prompt");

        result.Should().Be("No se pudo generar contenido.");
    }

    [Fact]
    public async Task GenerateAsync_ErrorStatusCode_ThrowsWithBody()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"error":"rate limited"}""", Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response, (_, _) => { });
        var service = new ReportChatService(new HttpClient(handler), Options());

        var act = () => service.GenerateAsync("prompt");

        (await act.Should().ThrowAsync<HttpRequestException>()).WithMessage("*rate limited*");
    }
}
