using System.ComponentModel.DataAnnotations;

namespace SemanticSearch.Core.Models;

public record UploadRequest(
    [Required] string Filename,
    [Required] string Category,
    string ContentType = "application/octet-stream"
);
