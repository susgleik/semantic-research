using Microsoft.AspNetCore.Mvc;
using SemanticSearch.Api.Models;
using SemanticSearch.Api.Services;

namespace SemanticSearch.Api.Endpoints;

public static class UploadEndpoints
{
    public const long MaxFileSizeBytes = 10L * 1024 * 1024; // 10 MB

    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/").RequireAuthorization();

        group.MapPost("/upload", async (
            IFormFile file,
            [FromForm] string category,
            IBlobService blobService,
            CancellationToken ct) =>
        {
            if (file.Length == 0)
                return Results.BadRequest(new { error = "El archivo no puede estar vacío." });

            if (file.Length > MaxFileSizeBytes)
                return Results.BadRequest(new
                {
                    error = $"El archivo supera el tamaño máximo permitido de {MaxFileSizeBytes / 1024 / 1024} MB."
                });

            if (string.IsNullOrWhiteSpace(category))
                return Results.BadRequest(new { error = "La categoría es obligatoria." });

            var docId   = Guid.NewGuid().ToString();
            var blobUrl = await blobService.UploadAsync(file, docId, category, ct);

            return Results.Accepted("/upload", new UploadResponse(
                DocId:    docId,
                Filename: file.FileName,
                Status:   "indexing",
                BlobUrl:  blobUrl
            ));
        })
        .WithName("Upload")
        .DisableAntiforgery()
        .Produces<UploadResponse>(202)
        .ProducesProblem(400);

        return app;
    }
}
