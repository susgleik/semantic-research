using Microsoft.AspNetCore.Mvc;
using SemanticSearch.Api.Models;
using SemanticSearch.Api.Services;

namespace SemanticSearch.Api.Endpoints;

public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/").RequireAuthorization();

        group.MapPost("/upload", async (
            IFormFile file,
            [FromForm] string category,
            IBlobService blobService,
            CancellationToken ct) =>
        {
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
