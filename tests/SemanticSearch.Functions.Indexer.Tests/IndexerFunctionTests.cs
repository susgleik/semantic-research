using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using FluentAssertions;
using Moq;
using SemanticSearch.Core.Models;
using SemanticSearch.Functions.Indexer.Services;

namespace SemanticSearch.Functions.Indexer.Tests;

public class IndexerFunctionTests
{
    private readonly Mock<IS3ObjectService> _s3ObjectService = new();
    private readonly Mock<ITextExtractorService> _textExtractor = new();
    private readonly Mock<IGeminiEmbeddingService> _embeddingService = new();
    private readonly Mock<IDynamoChunkWriter> _chunkWriter = new();
    private readonly ChunkerService _chunker = new();
    private readonly Mock<ILambdaContext> _context = new();

    public IndexerFunctionTests()
    {
        _context.Setup(c => c.Logger).Returns(Mock.Of<ILambdaLogger>());
    }

    private IndexerFunction CreateFunction() => new(
        _s3ObjectService.Object,
        _textExtractor.Object,
        _chunker,
        _embeddingService.Object,
        _chunkWriter.Object);

    private static S3Event BuildEvent(string bucket, string key, long size) => new()
    {
        Records =
        [
            new S3Event.S3EventNotificationRecord
            {
                S3 = new S3Event.S3Entity
                {
                    Bucket = new S3Event.S3BucketEntity { Name = bucket },
                    Object = new S3Event.S3ObjectEntity { Key = key, Size = size }
                }
            }
        ]
    };

    [Fact]
    public async Task FunctionHandler_ValidDocument_WritesChunksToDynamo()
    {
        const string text = "hello world this is a short contract";
        _textExtractor.Setup(t => t.Extract(It.IsAny<byte[]>(), "informe.pdf")).Returns(text);
        _s3ObjectService.Setup(s => s.DownloadAsync("docs", "contratos/doc-1/informe.pdf", default))
            .ReturnsAsync([1, 2, 3]);
        _embeddingService
            .Setup(e => e.EmbedBatchAsync(It.IsAny<IEnumerable<string>>(), "RETRIEVAL_DOCUMENT", default))
            .ReturnsAsync([new float[] { 0.1f, 0.2f }]);

        List<ChunkRecord>? written = null;
        _chunkWriter
            .Setup(w => w.WriteChunksAsync(It.IsAny<IEnumerable<ChunkRecord>>(), default))
            .Callback<IEnumerable<ChunkRecord>, CancellationToken>((chunks, _) => written = chunks.ToList())
            .Returns(Task.CompletedTask);

        var s3Event = BuildEvent("docs", "contratos/doc-1/informe.pdf", 1024);

        await CreateFunction().FunctionHandler(s3Event, _context.Object);

        written.Should().HaveCount(1);
        written![0].DocumentId.Should().Be("doc-1");
        written[0].Filename.Should().Be("informe.pdf");
        _s3ObjectService.Verify(s => s.MoveToFailedAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task FunctionHandler_OversizedObject_MovesToFailedWithoutEmbedding()
    {
        var s3Event = BuildEvent("docs", "contratos/doc-2/grande.pdf", 11L * 1024 * 1024);

        await CreateFunction().FunctionHandler(s3Event, _context.Object);

        _s3ObjectService.Verify(s => s.MoveToFailedAsync("docs", "contratos/doc-2/grande.pdf", default), Times.Once);
        _embeddingService.Verify(
            e => e.EmbedBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), default), Times.Never);
        _chunkWriter.Verify(
            w => w.WriteChunksAsync(It.IsAny<IEnumerable<ChunkRecord>>(), default), Times.Never);
    }

    [Fact]
    public async Task FunctionHandler_ExtractionFails_MovesToFailed()
    {
        _s3ObjectService.Setup(s => s.DownloadAsync("docs", "contratos/doc-3/roto.pdf", default))
            .ReturnsAsync([1, 2, 3]);
        _textExtractor.Setup(t => t.Extract(It.IsAny<byte[]>(), "roto.pdf"))
            .Throws(new NotSupportedException("archivo corrupto"));

        var s3Event = BuildEvent("docs", "contratos/doc-3/roto.pdf", 1024);

        await CreateFunction().FunctionHandler(s3Event, _context.Object);

        _s3ObjectService.Verify(s => s.MoveToFailedAsync("docs", "contratos/doc-3/roto.pdf", default), Times.Once);
    }

    [Fact]
    public async Task FunctionHandler_UnexpectedKeyFormat_MovesToFailed()
    {
        var s3Event = BuildEvent("docs", "sin-formato-valido.pdf", 1024);

        await CreateFunction().FunctionHandler(s3Event, _context.Object);

        _s3ObjectService.Verify(s => s.MoveToFailedAsync("docs", "sin-formato-valido.pdf", default), Times.Once);
    }
}
