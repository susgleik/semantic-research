using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using FluentAssertions;
using Moq;
using SemanticSearch.Functions.Upload.Services;

namespace SemanticSearch.Functions.Upload.Tests;

public class UploadFunctionTests
{
    private readonly Mock<IS3UploadService> _s3UploadService = new();
    private readonly Mock<ILambdaContext> _context = new();

    private UploadFunction CreateFunction() => new(_s3UploadService.Object);

    [Fact]
    public async Task FunctionHandler_ValidRequest_Returns200WithUploadUrl()
    {
        _s3UploadService
            .Setup(s => s.CreatePresignedUploadAsync(It.IsAny<string>(), "contratos", "informe.pdf", "application/pdf", default))
            .ReturnsAsync(("https://s3.example.com/presigned", "contratos/some-id/informe.pdf"));

        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            Body = """{"filename":"informe.pdf","category":"contratos","contentType":"application/pdf"}"""
        };

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("https://s3.example.com/presigned");
        response.Body.Should().Contain("\"status\":\"pending\"");
    }

    [Fact]
    public async Task FunctionHandler_MissingFilename_Returns400()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            Body = """{"category":"contratos"}"""
        };

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task FunctionHandler_MissingCategory_Returns400()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            Body = """{"filename":"informe.pdf"}"""
        };

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData("informe.xlsx")]
    [InlineData("presentacion.pptx")]
    [InlineData("notas.txt")]
    public async Task FunctionHandler_UnsupportedExtension_Returns400(string filename)
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            Body = $$"""{"filename":"{{filename}}","category":"contratos"}"""
        };

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task FunctionHandler_InvalidJson_Returns400()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest { Body = "not-json" };

        var response = await CreateFunction().FunctionHandler(request, _context.Object);

        response.StatusCode.Should().Be(400);
    }
}
