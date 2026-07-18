namespace SemanticSearch.Core.Models;

public record ReportRequest(
    string Scenario,
    string? Category = null,
    List<string>? DocumentIds = null,
    string? Instruction = null,
    string? DateFrom = null,
    string? DateTo = null);
