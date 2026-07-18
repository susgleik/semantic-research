using SemanticSearch.Core.Models;

namespace SemanticSearch.Functions.Reports.Services;

public interface IReportGeneratorService
{
    Task<string> GenerateReportAsync(
        ReportRequest request, IReadOnlyList<ChunkRecord> chunks, CancellationToken ct = default);
}
