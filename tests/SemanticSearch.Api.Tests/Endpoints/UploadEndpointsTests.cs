using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using SemanticSearch.Api.Endpoints;
using SemanticSearch.Api.Models;
using SemanticSearch.Api.Services;
using Xunit;

namespace SemanticSearch.Api.Tests.Endpoints;

public class UploadEndpointsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient               _client;

    public UploadEndpointsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── Helper para armar multipart/form-data ─────────────────────────────────

    private static MultipartFormDataContent BuildForm(
        byte[]  content,
        string  filename  = "test.txt",
        string  mediaType = "text/plain",
        string  category  = "legal")
    {
        var form        = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent(category), "category");
        return form;
    }

    // ── 202 Accepted con archivo válido ───────────────────────────────────────

    [Fact]
    public async Task PostUpload_WithValidFile_Returns202AndDocId()
    {
        _factory.BlobService
            .Setup(b => b.UploadAsync(
                It.IsAny<IFormFile>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://fake.blob.core.windows.net/docs/legal/abc/test.txt");

        using var form     = BuildForm(Encoding.UTF8.GetBytes("contenido del documento"));
        var       response = await _client.PostAsync("/upload", form);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<UploadResponse>();
        body!.DocId.Should().NotBeNullOrEmpty();
        body.Filename.Should().Be("test.txt");
        body.Status.Should().Be("indexing");
        body.BlobUrl.Should().Contain("fake.blob");
    }

    // ── 400 cuando el archivo supera el tamaño máximo ─────────────────────────

    [Fact]
    public async Task PostUpload_WithFileTooLarge_Returns400()
    {
        // Genera un byte[] ligeramente mayor al límite.
        var oversized = new byte[UploadEndpoints.MaxFileSizeBytes + 1];

        using var form     = BuildForm(oversized, "big.bin", "application/octet-stream");
        var       response = await _client.PostAsync("/upload", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── 400 cuando el archivo está vacío ─────────────────────────────────────

    [Fact]
    public async Task PostUpload_WithEmptyFile_Returns400()
    {
        using var form     = BuildForm(Array.Empty<byte>());
        var       response = await _client.PostAsync("/upload", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── 400 cuando la categoría está vacía ───────────────────────────────────

    [Fact]
    public async Task PostUpload_WithBlankCategory_Returns400()
    {
        using var form     = BuildForm(Encoding.UTF8.GetBytes("contenido"), category: "   ");
        var       response = await _client.PostAsync("/upload", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── BlobService se llama con docId y category correctos ──────────────────

    [Fact]
    public async Task PostUpload_CallsBlobServiceWithCorrectCategory()
    {
        string? capturedCategory = null;

        _factory.BlobService
            .Setup(b => b.UploadAsync(
                It.IsAny<IFormFile>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<IFormFile, string, string, CancellationToken>(
                (_, _, cat, _) => capturedCategory = cat)
            .ReturnsAsync("https://fake.blob.core.windows.net/docs/contratos/abc/doc.txt");

        using var form = BuildForm(Encoding.UTF8.GetBytes("texto"), category: "contratos");
        await _client.PostAsync("/upload", form);

        capturedCategory.Should().Be("contratos");
    }
}
