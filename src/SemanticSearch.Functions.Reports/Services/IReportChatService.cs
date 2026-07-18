namespace SemanticSearch.Functions.Reports.Services;

public interface IReportChatService
{
    Task<string> GenerateAsync(string prompt, CancellationToken ct = default);
}
