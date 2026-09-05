using SemanticSearch.Core.Models;

namespace SemanticSearch.Functions.Query.Services;

public interface IQueryCacheService
{
    Task<QueryResponse?> GetAsync(string query, int topK, string ownerId, CancellationToken ct = default);

    Task SetAsync(string query, int topK, string ownerId, QueryResponse response, CancellationToken ct = default);
}
