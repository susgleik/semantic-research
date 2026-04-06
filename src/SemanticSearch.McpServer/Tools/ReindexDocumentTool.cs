using System.Text.Json;

namespace SemanticSearch.McpServer.Tools;

public class ReindexDocumentTool(HttpClient httpClient)
{
    public string Name        => "reindex_document";
    public string Description => "Fuerza la re-indexación de un documento por su ID";

    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct)
    {
        var docId    = parameters.GetProperty("doc_id").GetString()!;
        var response = await httpClient.PostAsync($"/reindex/{docId}", content: null, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
