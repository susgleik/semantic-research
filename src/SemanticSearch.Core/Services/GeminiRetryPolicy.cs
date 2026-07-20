using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SemanticSearch.Core.Services;

// Los 3 servicios que llaman a la API de Gemini (embeddings, RAG answer, reportes)
// comparten el mismo problema: un 503 "high demand" o 429 de rate limit son
// transitorios del lado de Google, no errores reales de la request — reintentar sin
// tocar nada casi siempre funciona. Backoff corto (1s, 2s) para no sumar carga a un
// servicio ya saturado.
public static class GeminiRetryPolicy
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    public static async Task<HttpResponseMessage> PostAsJsonWithRetryAsync<TRequest>(
        HttpClient httpClient, string url, TRequest requestBody, JsonSerializerOptions jsonOptions,
        CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await httpClient.PostAsJsonAsync(url, requestBody, jsonOptions, ct);

            var isTransient = response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests;
            if (response.IsSuccessStatusCode || !isTransient || attempt >= RetryDelays.Length)
                return response;

            response.Dispose();
            await Task.Delay(RetryDelays[attempt], ct);
        }
    }
}
