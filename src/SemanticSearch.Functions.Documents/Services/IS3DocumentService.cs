namespace SemanticSearch.Functions.Documents.Services;

public interface IS3DocumentService
{
    /// <summary>Re-copia el objeto sobre sí mismo para retriggerear el evento s3:ObjectCreated que dispara indexer-service.</summary>
    Task TriggerReindexAsync(string category, string docId, string filename, string ownerId, CancellationToken ct = default);

    Task DeleteObjectAsync(string category, string docId, string filename, CancellationToken ct = default);
}
