using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Api.Services;

public class BlobService(
    BlobServiceClient blobServiceClient,
    IOptions<BlobOptions> opts,
    ILogger<BlobService> logger) : IBlobService
{
    private readonly string _container = opts.Value.Container;

    public async Task<string> UploadAsync(IFormFile file, string docId, string category, CancellationToken ct = default)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(_container);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var blobName = $"{category}/{docId}/{file.FileName}";
        var blobClient = containerClient.GetBlobClient(blobName);

        await using var stream = file.OpenReadStream();
        await blobClient.UploadAsync(stream, overwrite: true, ct);

        logger.LogInformation("Uploaded blob {BlobName}", blobName);
        return blobClient.Uri.ToString();
    }

    public async Task DeleteAsync(string docId, CancellationToken ct = default)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(_container);
        await foreach (var blob in containerClient.GetBlobsAsync(prefix: docId, cancellationToken: ct))
        {
            await containerClient.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: ct);
        }
    }
}
