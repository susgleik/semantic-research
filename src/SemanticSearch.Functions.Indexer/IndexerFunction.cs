using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using SemanticSearch.Core.Models;
using SemanticSearch.Core.Options;
using SemanticSearch.Core.Services;
using SemanticSearch.Functions.Indexer.Services;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SemanticSearch.Functions.Indexer;

public class IndexerFunction
{
    private const long MaxFileSizeBytes = 10L * 1024 * 1024; // 10 MB, igual que la versión anterior
    private const int WindowSize = 512;
    private const int Overlap = 64;

    private readonly IS3ObjectService _s3ObjectService;
    private readonly ITextExtractorService _textExtractor;
    private readonly ChunkerService _chunker;
    private readonly IGeminiEmbeddingService _embeddingService;
    private readonly IDynamoChunkWriter _chunkWriter;

    public IndexerFunction() : this(
        BuildS3ObjectService(),
        new TextExtractorService(),
        new ChunkerService(),
        BuildEmbeddingService(),
        BuildChunkWriter())
    {
    }

    public IndexerFunction(
        IS3ObjectService s3ObjectService,
        ITextExtractorService textExtractor,
        ChunkerService chunker,
        IGeminiEmbeddingService embeddingService,
        IDynamoChunkWriter chunkWriter)
    {
        _s3ObjectService  = s3ObjectService;
        _textExtractor    = textExtractor;
        _chunker          = chunker;
        _embeddingService = embeddingService;
        _chunkWriter      = chunkWriter;
    }

    public async Task FunctionHandler(S3Event s3Event, ILambdaContext context)
    {
        foreach (var record in s3Event.Records)
        {
            await ProcessRecordAsync(record, context);
        }
    }

    private async Task ProcessRecordAsync(S3Event.S3EventNotificationRecord record, ILambdaContext context)
    {
        var bucket = record.S3.Bucket.Name;
        var key    = Uri.UnescapeDataString(record.S3.Object.Key);

        try
        {
            if (record.S3.Object.Size > MaxFileSizeBytes)
            {
                context.Logger.LogWarning(
                    $"Objeto {key} ({record.S3.Object.Size} bytes) supera el máximo de {MaxFileSizeBytes} bytes, se mueve a failed/");
                await _s3ObjectService.MoveToFailedAsync(bucket, key);
                return;
            }

            var (category, docId, filename) = ParseObjectKey(key);

            var content = await _s3ObjectService.DownloadAsync(bucket, key);
            var text    = _textExtractor.Extract(content, filename);
            var chunks  = _chunker.SlidingWindow(docId, filename, text, WindowSize, Overlap);

            var vectors = await _embeddingService.EmbedBatchAsync(
                chunks.Select(c => c.Text), taskType: "RETRIEVAL_DOCUMENT");

            var now = DateTimeOffset.UtcNow.ToString("O");
            var records = chunks.Zip(vectors, (chunk, vector) => new ChunkRecord
            {
                DocumentId = docId,
                ChunkId    = $"chunk-{chunk.StartIndex:D6}",
                Text       = chunk.Text,
                Embedding  = vector.ToList(),
                Filename   = filename,
                Category   = category,
                Page       = chunk.Page,
                Status     = "indexed",
                CreatedAt  = now
            }).ToList();

            await _chunkWriter.WriteChunksAsync(records);

            context.Logger.LogInformation($"Documento {filename} indexado: {records.Count} chunks (categoría {category})");
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error indexando {key}: {ex.Message}");
            await _s3ObjectService.MoveToFailedAsync(bucket, key);
        }
    }

    /// <summary>Convención de key: {category}/{docId}/{filename}, fijada por upload-service.</summary>
    private static (string Category, string DocId, string Filename) ParseObjectKey(string key)
    {
        var parts = key.Split('/', 3);
        if (parts.Length != 3)
            throw new FormatException($"Key de S3 con formato inesperado: {key}");

        return (parts[0], parts[1], parts[2]);
    }

    private static S3Options LoadS3Options() => new()
    {
        BucketName = Environment.GetEnvironmentVariable("S3_BUCKET_DOCS") ?? "docs",
        Region     = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
        ServiceUrl = Environment.GetEnvironmentVariable("S3_SERVICE_URL")
    };

    private static DynamoDbOptions LoadDynamoDbOptions() => new()
    {
        TableName  = Environment.GetEnvironmentVariable("DYNAMODB_TABLE_NAME") ?? "chunks",
        Region     = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
        ServiceUrl = Environment.GetEnvironmentVariable("DYNAMODB_SERVICE_URL")
    };

    private static GeminiOptions LoadGeminiOptions() => new()
    {
        ApiKey              = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "",
        EmbeddingModel      = Environment.GetEnvironmentVariable("GEMINI_EMBEDDING_MODEL") ?? "gemini-embedding-001",
        ChatModel           = Environment.GetEnvironmentVariable("GEMINI_CHAT_MODEL") ?? "gemini-2.5-flash",
        EmbeddingDimensions = int.TryParse(Environment.GetEnvironmentVariable("GEMINI_EMBEDDING_DIMENSIONS"), out var d) ? d : 768
    };

    private static IS3ObjectService BuildS3ObjectService()
    {
        var options = LoadS3Options();
        var s3Client = string.IsNullOrEmpty(options.ServiceUrl)
            ? new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(options.Region))
            : new AmazonS3Client(new AmazonS3Config { ServiceURL = options.ServiceUrl, ForcePathStyle = true });

        return new S3ObjectService(s3Client);
    }

    private static IGeminiEmbeddingService BuildEmbeddingService() =>
        new GeminiEmbeddingService(SharedHttpClient, LoadGeminiOptions());

    private static IDynamoChunkWriter BuildChunkWriter()
    {
        var options = LoadDynamoDbOptions();
        var dynamoClient = string.IsNullOrEmpty(options.ServiceUrl)
            ? new AmazonDynamoDBClient(Amazon.RegionEndpoint.GetBySystemName(options.Region))
            : new AmazonDynamoDBClient(new AmazonDynamoDBConfig { ServiceURL = options.ServiceUrl });

        var context = new DynamoDBContext(dynamoClient);
        return new DynamoChunkWriter(context, options);
    }

    // HttpClient estático: se reutiliza entre invocaciones dentro del mismo entorno de ejecución del Lambda.
    private static readonly HttpClient SharedHttpClient = new();
}
