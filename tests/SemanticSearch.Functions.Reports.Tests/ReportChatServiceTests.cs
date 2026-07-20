using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SemanticSearch.Core.Options;
using SemanticSearch.Functions.Reports.Services;

namespace SemanticSearch.Functions.Reports.Tests;

public class ReportChatServiceTests
{
    // Factory (no una única instancia) porque GeminiRetryPolicy puede pedir la
    // respuesta más de una vez en el mismo test (retry ante 429/503) -- reusar un
    // HttpResponseMessage ya leído/dispuesto tira ObjectDisposedException.
    private class FakeHttpMessageHandler(
        Func<HttpResponseMessage> responseFactory, Action<HttpRequestMessage, string> onRequest)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            onRequest(request, body);
            return responseFactory();
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

        var handler = new FakeHttpMessageHandler(
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            },
            (req, body) =>
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
        var handler = new FakeHttpMessageHandler(
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"candidates":[]}""", Encoding.UTF8, "application/json")
            },
            (_, _) => { });
        var service = new ReportChatService(new HttpClient(handler), Options());

        var result = await service.GenerateAsync("prompt");

        result.Should().Be("No se pudo generar contenido.");
    }

    [Fact]
    public async Task GenerateAsync_ErrorStatusCode_ThrowsWithBody()
    {
        var handler = new FakeHttpMessageHandler(
            () => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":"rate limited"}""", Encoding.UTF8, "application/json")
            },
            (_, _) => { });
        var service = new ReportChatService(new HttpClient(handler), Options());

        var act = () => service.GenerateAsync("prompt");

        // 429 es "transitorio" para GeminiRetryPolicy -- reintenta (con backoff real,
        // ~3s en este test) antes de tirar la excepción final con el mismo body.
        (await act.Should().ThrowAsync<HttpRequestException>()).WithMessage("*rate limited*");
    }
}
