using System.ComponentModel;
using System.Net.Http.Json;
using ModelContextProtocol.Server;

namespace SemanticSearch.McpServer.Tools;

[McpServerToolType]
public class SearchDocumentsTool(HttpClient httpClient)
{
    [McpServerTool(Name = "search_documents")]
    [Description("Busca en los documentos indexados una respuesta a una pregunta en lenguaje natural, citando las fuentes exactas.")]
    public async Task<string> SearchDocuments(
        [Description("Pregunta en lenguaje natural sobre el contenido de los documentos indexados")] string query,
        [Description("Cantidad de fragmentos fuente a considerar (default 5)")] int topK = 5,
        CancellationToken ct = default)
    {
        var request  = new { query, topK };
        var response = await httpClient.PostAsJsonAsync("/query", request, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
