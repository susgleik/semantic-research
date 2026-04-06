using System.Text.Json;

namespace SemanticSearch.McpServer.Tools;

public class ListDocumentsTool(HttpClient httpClient)
{
    public string Name        => "list_documents";
    public string Description => "Lista los documentos indexados con su metadata";

    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct)
    {
        // TODO: agregar soporte de filtros opcionales (categoría, fecha)
        var response = await httpClient.GetAsync("/documents", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
