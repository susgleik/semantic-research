using System.Net.Http.Json;
using System.Text.Json;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Functions.Reports.Services;

public class ReportChatService(HttpClient httpClient, GeminiOptions options) : IReportChatService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{options.ChatModel}:generateContent?key={options.ApiKey}";

        using var response = await httpClient.PostAsJsonAsync(url, requestBody, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Gemini generateContent failed ({(int)response.StatusCode} {response.StatusCode}): {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(JsonOptions, ct);
        return result?.Candidates.FirstOrDefault()?.Content.Parts.FirstOrDefault()?.Text
            ?? "No se pudo generar contenido.";
    }

    private record GenerateContentResponse(List<Candidate> Candidates);
    private record Candidate(ContentPart Content);
    private record ContentPart(List<TextPart> Parts);
    private record TextPart(string Text);
}
