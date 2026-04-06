using SemanticSearch.Api.Models;

namespace SemanticSearch.Api.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/").RequireAuthorization();

        // TODO: implementar listado con paginación desde Azure AI Search
        group.MapGet("/documents", () => Results.Ok(Array.Empty<DocumentRecord>()))
            .WithName("ListDocuments")
            .Produces<IReadOnlyList<DocumentRecord>>();

        // TODO: implementar re-indexación forzada
        group.MapPost("/reindex/{docId}", (string docId) =>
            Results.Accepted($"/documents/{docId}", new { docId, status = "reindexing" }))
            .WithName("ReindexDocument")
            .Produces(202);

        return app;
    }
}
