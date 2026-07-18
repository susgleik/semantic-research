namespace SemanticSearch.Core.Models;

public static class ReportScenarios
{
    public const string Summary = "summary";
    public const string Risks = "risks";
    public const string Compare = "compare";
    public const string Extract = "extract";
    public const string Custom = "custom";

    public static readonly IReadOnlyList<string> Known = [Summary, Risks, Compare, Extract, Custom];

    public static bool IsKnown(string? scenario) => scenario is not null && Known.Contains(scenario);
}
