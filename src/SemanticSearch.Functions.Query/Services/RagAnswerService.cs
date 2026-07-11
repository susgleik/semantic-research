using System.Net.Http.Json;
using System.Text.Json;
using SemanticSearch.Core.Models;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Functions.Query.Services;

public class RagAnswerService(HttpClient httpClient, GeminiOptions options) : IRagAnswerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> GenerateAnswerAsync(
        string query, IReadOnlyList<SourceChunk> chunks, CancellationToken ct = default)
    {
        var context = string.Join("\n\n", chunks.Select((c, i) =>
            $"[Fragmento {i + 1} — {c.Filename}]\n{c.Chunk}"));

        var prompt = $"""
            Sos un asistente que responde preguntas basándose exclusivamente en los
            fragmentos de documentos provistos. Si la respuesta no está en los
            fragmentos, indicalo claramente. Citá el nombre del documento fuente
            entre corchetes al final de cada afirmación.

            Pregunta: {query}

            Fragmentos relevantes:
            {context}
            """;

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{options.ChatModel}:generateContent?key={options.ApiKey}";

        using var response = await httpClient.PostAsJsonAsync(url, requestBody, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(JsonOptions, ct);
        return result?.Candidates.FirstOrDefault()?.Content.Parts.FirstOrDefault()?.Text
            ?? "No se pudo generar una respuesta.";
    }

    private record GenerateContentResponse(List<Candidate> Candidates);
    private record Candidate(ContentPart Content);
    private record ContentPart(List<TextPart> Parts);
    private record TextPart(string Text);
}
