using Amazon.DynamoDBv2.DataModel;
using FluentAssertions;
using Moq;
using SemanticSearch.Core.Models;
using SemanticSearch.Functions.Query.Models;
using SemanticSearch.Functions.Query.Services;

namespace SemanticSearch.Functions.Query.Tests;

public class QueryCacheServiceTests
{
    private readonly Mock<IDynamoDBContext> _context = new();
    private readonly Dictionary<string, QueryCacheRecord> _store = [];

    public QueryCacheServiceTests()
    {
        _context
            .Setup(c => c.SaveAsync(It.IsAny<QueryCacheRecord>(), It.IsAny<DynamoDBOperationConfig>(), It.IsAny<CancellationToken>()))
            .Callback<QueryCacheRecord, DynamoDBOperationConfig, CancellationToken>((record, _, _) => _store[record.QueryHash] = record)
            .Returns(Task.CompletedTask);

        _context
            .Setup(c => c.LoadAsync<QueryCacheRecord>(It.IsAny<object>(), It.IsAny<DynamoDBOperationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object hashKey, DynamoDBOperationConfig _, CancellationToken _) =>
                _store.GetValueOrDefault((string)hashKey));
    }

    [Fact]
    public async Task GetAsync_SameQueryDifferentCasingAndWhitespace_IsACacheHit()
    {
        var service = new QueryCacheService(_context.Object, "query-cache", ttlSeconds: 600);
        var response = new QueryResponse("30 dias", []);

        await service.SetAsync("  ¿Cuál es el Plazo?  ", 5, response);
        var hit = await service.GetAsync("¿cuál es el plazo?", 5);

        hit.Should().NotBeNull();
        hit!.Answer.Should().Be("30 dias");
    }

    [Fact]
    public async Task GetAsync_DifferentTopK_IsACacheMiss()
    {
        var service = new QueryCacheService(_context.Object, "query-cache", ttlSeconds: 600);
        await service.SetAsync("pregunta", 5, new QueryResponse("respuesta", []));

        var hit = await service.GetAsync("pregunta", 10);

        hit.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ExpiredEntry_ReturnsNull()
    {
        var service = new QueryCacheService(_context.Object, "query-cache", ttlSeconds: -1);
        await service.SetAsync("pregunta vieja", 5, new QueryResponse("respuesta", []));

        var hit = await service.GetAsync("pregunta vieja", 5);

        hit.Should().BeNull();
    }
}
