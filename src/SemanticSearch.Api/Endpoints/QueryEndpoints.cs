using SemanticSearch.Api.Models;
using SemanticSearch.Api.Services;

namespace SemanticSearch.Api.Endpoints;

public static class QueryEndpoints
{
    public static IEndpointRouteBuilder MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/").RequireAuthorization();

        group.MapPost("/query", async (
            QueryRequest request,
            IRagService ragService,
            CancellationToken ct) =>
        {
            var result = await ragService.QueryAsync(request, ct);
            return Results.Ok(result);
        })
        .WithName("Query")
        .Produces<QueryResponse>()
        .ProducesProblem(400)
        .ProducesProblem(500);

        return app;
    }
}
