namespace SemanticSearch.Functions.Reports.Services;

public interface IReportStorageService
{
    Task SaveReportAsync(string reportId, string content, CancellationToken ct = default);

    /// <summary>Devuelve una URL prefirmada de descarga, o null si el reporte no existe.</summary>
    Task<string?> GetDownloadUrlAsync(string reportId, CancellationToken ct = default);
}
