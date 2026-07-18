using SemanticSearch.Api.Models;
using SemanticSearch.Api.Services;

namespace SemanticSearch.Api.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/").RequireAuthorization();

        group.MapGet("/documents", async (
                ISearchService searchService,
                int skip = 0,
                int top = 20,
                CancellationToken ct = default) =>
            {
                if (skip < 0)
                    return Results.BadRequest(new { error = "skip debe ser mayor o igual a 0." });

                if (top is < 1 or > 100)
                    return Results.BadRequest(new { error = "top debe estar entre 1 y 100." });

                var docs = await searchService.ListDocumentsAsync(skip, top, ct);
                return Results.Ok(docs);
            })
            .WithName("ListDocuments")
            .Produces<IReadOnlyList<DocumentRecord>>()
            .ProducesProblem(500);

        group.MapPost("/reindex/{docId}", async (
                string docId,
                IBlobService blobService,
                CancellationToken ct) =>
            {
                try
                {
                    await blobService.TriggerReindexAsync(docId, ct);
                    return Results.Accepted($"/documents/{docId}", new { docId, status = "reindexing" });
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { error = $"Document '{docId}' not found" });
                }
            })
            .WithName("ReindexDocument")
            .Produces(202)
            .ProducesProblem(404);

        return app;
    }
}
