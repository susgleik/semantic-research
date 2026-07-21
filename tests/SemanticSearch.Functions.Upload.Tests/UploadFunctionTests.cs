using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using FluentAssertions;
using Moq;
using SemanticSearch.Functions.Upload.Services;

namespace SemanticSearch.Functions.Upload.Tests;

public class UploadFunctionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Mock<IS3UploadService> _s3UploadService = new();
    private readonly Mock<ILambdaContext> _context = new();

    private UploadFunction CreateFunction() => new(_s3UploadService.Object);

    private static JsonElement ToInput(object payload) =>
        JsonSerializer.SerializeToElement(payload, JsonOptions);

    [Fact]
    public async Task FunctionHandler_ValidRequest_Returns200WithUploadUrl()
    {
        _s3UploadService
            .Setup(s => s.CreatePresignedUploadAsync(It.IsAny<string>(), "contratos", "informe.pdf", "application/pdf", default))
            .ReturnsAsync(("https://s3.example.com/presigned", "contratos/some-id/informe.pdf"));

        var input = ToInput(new APIGatewayHttpApiV2ProxyRequest
        {
            Body = """{"filename":"informe.pdf","category":"contratos","contentType":"application/pdf"}"""
        });

        var response = (APIGatewayHttpApiV2ProxyResponse)await CreateFunction().FunctionHandler(input, _context.Object);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("https://s3.example.com/presigned");
        response.Body.Should().Contain("\"status\":\"pending\"");
    }

    [Fact]
    public async Task FunctionHandler_MissingFilename_Returns400()
    {
        var input = ToInput(new APIGatewayHttpApiV2ProxyRequest { Body = """{"category":"contratos"}""" });

        var response = (APIGatewayHttpApiV2ProxyResponse)await CreateFunction().FunctionHandler(input, _context.Object);

        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task FunctionHandler_MissingCategory_Returns400()
    {
        var input = ToInput(new APIGatewayHttpApiV2ProxyRequest { Body = """{"filename":"informe.pdf"}""" });

        var response = (APIGatewayHttpApiV2ProxyResponse)await CreateFunction().FunctionHandler(input, _context.Object);

        response.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData("informe.xlsx")]
    [InlineData("presentacion.pptx")]
    [InlineData("notas.txt")]
    public async Task FunctionHandler_UnsupportedExtension_Returns400(string filename)
    {
        var input = ToInput(new APIGatewayHttpApiV2ProxyRequest
        {
            Body = $$"""{"filename":"{{filename}}","category":"contratos"}"""
        });

        var response = (APIGatewayHttpApiV2ProxyResponse)await CreateFunction().FunctionHandler(input, _context.Object);

        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task FunctionHandler_InvalidJson_Returns400()
    {
        var input = ToInput(new APIGatewayHttpApiV2ProxyRequest { Body = "not-json" });

        var response = (APIGatewayHttpApiV2ProxyResponse)await CreateFunction().FunctionHandler(input, _context.Object);

        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task FunctionHandler_CognitoPreSignUp_AutoConfirmsAndAutoVerifiesEmail()
    {
        var input = JsonSerializer.SerializeToElement(new
        {
            triggerSource = "PreSignUp_SignUp",
            userPoolId = "us-east-1_test",
            userName = "someone@example.com",
            request = new { userAttributes = new { email = "someone@example.com" } },
            response = new { }
        });

        var result = await CreateFunction().FunctionHandler(input, _context.Object);

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var response = doc.RootElement.GetProperty("response");

        response.GetProperty("autoConfirmUser").GetBoolean().Should().BeTrue();
        response.GetProperty("autoVerifyEmail").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task FunctionHandler_CognitoPreSignUp_WithoutEmail_DoesNotAutoVerifyEmail()
    {
        var input = JsonSerializer.SerializeToElement(new
        {
            triggerSource = "PreSignUp_AdminCreateUser",
            userPoolId = "us-east-1_test",
            userName = "someone",
            request = new { userAttributes = new { } },
            response = new { }
        });

        var result = await CreateFunction().FunctionHandler(input, _context.Object);

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var response = doc.RootElement.GetProperty("response");

        response.GetProperty("autoConfirmUser").GetBoolean().Should().BeTrue();
        response.TryGetProperty("autoVerifyEmail", out _).Should().BeFalse();
    }
}
