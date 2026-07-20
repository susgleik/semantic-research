using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SemanticSearch.McpServer.Tools;

[McpServerToolType]
public class ReindexDocumentTool(HttpClient httpClient)
{
    [McpServerTool(Name = "reindex_document")]
    [Description("Fuerza la re-indexación de un documento ya subido, a partir de su docId.")]
    public async Task<string> ReindexDocument(
        [Description("docId del documento a reindexar (visible en list_documents)")] string docId,
        CancellationToken ct = default)
    {
        var response = await httpClient.PostAsync($"/reindex/{docId}", content: null, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
