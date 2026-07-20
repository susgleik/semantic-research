using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SemanticSearch.Core.Services;

namespace SemanticSearch.Functions.Indexer.Tests;

public class GeminiRetryPolicyTests
{
    private class CountingHandler(Func<int, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(responseFactory(CallCount));
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task PostAsJsonWithRetryAsync_TransientErrorThenSuccess_RetriesAndReturnsSuccess()
    {
        var handler = new CountingHandler(call => call < 3
            ? JsonResponse(HttpStatusCode.ServiceUnavailable, """{"error":"high demand"}""")
            : JsonResponse(HttpStatusCode.OK, """{"ok":true}"""));
        var httpClient = new HttpClient(handler);

        var response = await GeminiRetryPolicy.PostAsJsonWithRetryAsync(
            httpClient, "https://example.test/x", new { }, new JsonSerializerOptions());

        response.IsSuccessStatusCode.Should().BeTrue();
        handler.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task PostAsJsonWithRetryAsync_NonTransientError_DoesNotRetry()
    {
        var handler = new CountingHandler(_ => JsonResponse(HttpStatusCode.BadRequest, """{"error":"bad request"}"""));
        var httpClient = new HttpClient(handler);

        var response = await GeminiRetryPolicy.PostAsJsonWithRetryAsync(
            httpClient, "https://example.test/x", new { }, new JsonSerializerOptions());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task PostAsJsonWithRetryAsync_AlwaysTransient_GivesUpAfterMaxRetries()
    {
        var handler = new CountingHandler(_ => JsonResponse(HttpStatusCode.TooManyRequests, """{"error":"rate limited"}"""));
        var httpClient = new HttpClient(handler);

        var response = await GeminiRetryPolicy.PostAsJsonWithRetryAsync(
            httpClient, "https://example.test/x", new { }, new JsonSerializerOptions());

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        handler.CallCount.Should().Be(3); // intento inicial + 2 reintentos
    }
}
