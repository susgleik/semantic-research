using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SemanticSearch.McpServer.Tools;

[McpServerToolType]
public class ListDocumentsTool(HttpClient httpClient)
{
    [McpServerTool(Name = "list_documents")]
    [Description("Lista los documentos indexados con su estado, categoría y cantidad de chunks.")]
    public async Task<string> ListDocuments(
        [Description("Cantidad máxima de documentos a devolver (default 20)")] int limit = 20,
        [Description("Desde qué posición empezar, para paginar (default 0)")] int offset = 0,
        CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"/documents?limit={limit}&offset={offset}", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
