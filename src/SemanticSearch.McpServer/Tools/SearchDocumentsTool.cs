using System.Net.Http.Json;
using System.Text.Json;

namespace SemanticSearch.McpServer.Tools;

public class SearchDocumentsTool(HttpClient httpClient)
{
    public string Name        => "search_documents";
    public string Description => "Busca documentos por pregunta en lenguaje natural";

    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct)
    {
        var query = parameters.GetProperty("query").GetString()!;
        var topK  = parameters.TryGetProperty("top_k", out var tk) ? tk.GetInt32() : 5;

        var request  = new { query, top_k = topK };
        var response = await httpClient.PostAsJsonAsync("/query", request, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
