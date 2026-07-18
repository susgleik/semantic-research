using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace SemanticSearch.Functions.Indexer.Services;

public class TextExtractorService : ITextExtractorService
{
    public string Extract(byte[] content, string filename) =>
        Path.GetExtension(filename).ToLowerInvariant() switch
        {
            ".pdf"  => ExtractPdf(content),
            ".docx" => ExtractDocx(content),
            _       => throw new NotSupportedException($"Tipo de archivo no soportado: {filename}")
        };

    private static string ExtractPdf(byte[] content)
    {
        using var document = PdfDocument.Open(content);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
            sb.AppendLine(string.Join(' ', page.GetWords().Select(w => w.Text)));
        return sb.ToString();
    }

    private static string ExtractDocx(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var doc    = WordprocessingDocument.Open(stream, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;

        return string.Join('\n', body.Descendants<Paragraph>()
            .Select(p => p.InnerText)
            .Where(t => !string.IsNullOrWhiteSpace(t)));
    }
}
