using SemanticSearch.Core.Models;

namespace SemanticSearch.Functions.Reports.Services;

public interface IReportGeneratorService
{
    Task<string> GenerateReportAsync(
        ReportRequest request, IReadOnlyList<ChunkRecord> chunks, string ownerId, CancellationToken ct = default);
}
