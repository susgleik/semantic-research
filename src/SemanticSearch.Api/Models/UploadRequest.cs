using System.ComponentModel.DataAnnotations;

namespace SemanticSearch.Api.Models;

public record UploadRequest(
    [Required] string Category
);
