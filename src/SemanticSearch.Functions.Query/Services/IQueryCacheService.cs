using SemanticSearch.Core.Models;

namespace SemanticSearch.Functions.Query.Services;

public interface IQueryCacheService
{
    Task<QueryResponse?> GetAsync(string query, int topK, CancellationToken ct = default);

    Task SetAsync(string query, int topK, QueryResponse response, CancellationToken ct = default);
}
