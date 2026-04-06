using SemanticSearch.Api.Models;

namespace SemanticSearch.Api.Services;

public interface IRagService
{
    Task<QueryResponse> QueryAsync(QueryRequest request, CancellationToken ct = default);
}
