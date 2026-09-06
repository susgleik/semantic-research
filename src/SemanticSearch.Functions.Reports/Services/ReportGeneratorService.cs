using SemanticSearch.Core.Models;

namespace SemanticSearch.Functions.Reports.Services;

public class ReportGeneratorService(IReportChatService chatService) : IReportGeneratorService
{
    private const string NoMatchesMessage = "No hay documentos que coincidan con los filtros indicados.";

    public async Task<string> GenerateReportAsync(
        ReportRequest request, IReadOnlyList<ChunkRecord> chunks, string ownerId, CancellationToken ct = default)
    {
        var filtered = FilterChunks(chunks, request, ownerId);
        if (filtered.Count == 0)
            return NoMatchesMessage;

        var documents = filtered
            .GroupBy(c => c.DocumentId)
            .Select(group => new
            {
                DocumentId = group.Key,
                Filename = group.First().Filename,
                Text = string.Join("\n", group.OrderBy(c => c.ChunkId).Select(c => c.Text))
            })
            .ToList();

        // Map: resumir cada documento por separado en vez de meter el corpus completo en un
        // solo prompt (controla el consumo de tokens/créditos de Gemini a medida que crece el corpus).
        var summaries = new List<string>(documents.Count);
        foreach (var doc in documents)
        {
            var mapPrompt = BuildMapPrompt(request.Scenario, request.Instruction, doc.Filename, doc.Text);
            var summary = await chatService.GenerateAsync(mapPrompt, ct);
            summaries.Add($"[{doc.Filename}]\n{summary}");
        }

        // Reduce: combinar los resúmenes por documento en el informe final.
        var reducePrompt = BuildReducePrompt(request.Scenario, request.Instruction, summaries);
        return await chatService.GenerateAsync(reducePrompt, ct);
    }

    public static IReadOnlyList<ChunkRecord> FilterChunks(IReadOnlyList<ChunkRecord> chunks, ReportRequest request, string ownerId)
    {
        IEnumerable<ChunkRecord> result = chunks.Where(c => c.OwnerId == ownerId || string.IsNullOrEmpty(c.OwnerId));

        if (!string.IsNullOrWhiteSpace(request.Category))
            result = result.Where(c => c.Category == request.Category);

        if (request.DocumentIds is { Count: > 0 })
            result = result.Where(c => request.DocumentIds.Contains(c.DocumentId));

        if (!string.IsNullOrWhiteSpace(request.DateFrom))
            result = result.Where(c => string.CompareOrdinal(c.CreatedAt, request.DateFrom) >= 0);

        if (!string.IsNullOrWhiteSpace(request.DateTo))
            result = result.Where(c => string.CompareOrdinal(c.CreatedAt, request.DateTo) <= 0);

        return result.ToList();
    }

    private static string BuildMapPrompt(string scenario, string? instruction, string filename, string documentText)
    {
        var lens = scenario switch
        {
            ReportScenarios.Risks => "Identificá riesgos, inconsistencias o cláusulas problemáticas en este documento.",
            ReportScenarios.Compare => "Resumí los puntos clave de este documento para poder compararlo luego con otro.",
            ReportScenarios.Extract => "Extraé del documento fechas, nombres propios, montos y cláusulas relevantes, en formato de lista.",
            ReportScenarios.Custom => $"Respecto a la siguiente instrucción: \"{instruction}\", extraé y resumí la información relevante de este documento.",
            _ => "Resumí el contenido clave de este documento en pocos párrafos."
        };

        return $"""
            {lens}

            Documento: {filename}

            Contenido:
            {documentText}
            """;
    }

    private static string BuildReducePrompt(string scenario, string? instruction, IReadOnlyList<string> summaries)
    {
        var combined = string.Join("\n\n", summaries);

        var goal = scenario switch
        {
            ReportScenarios.Risks => "Generá un informe consolidado de riesgos e inconsistencias, señalando explícitamente si hay contradicciones ENTRE documentos.",
            ReportScenarios.Compare => "Comparás estos dos documentos punto por punto, señalando similitudes y diferencias claramente.",
            ReportScenarios.Extract => "Consolidá estos datos extraídos por documento en una única lista organizada, evitando duplicados.",
            ReportScenarios.Custom => $"Respecto a la instrucción \"{instruction}\", combiná estos análisis por documento en un informe final coherente.",
            _ => "Generá un resumen ejecutivo del corpus completo a partir de estos resúmenes por documento."
        };

        return $"""
            {goal}

            Resúmenes por documento:
            {combined}
            """;
    }
}
