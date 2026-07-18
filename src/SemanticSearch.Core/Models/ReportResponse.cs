namespace SemanticSearch.Core.Models;

public record ReportResponse(
    string ReportId,
    string Status,
    string? DownloadUrl = null);
