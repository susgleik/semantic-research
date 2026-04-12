using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Scalar.AspNetCore;
using SemanticSearch.Api.Endpoints;
using SemanticSearch.Api.Middleware;
using SemanticSearch.Api.Services;
using CoreOptions = SemanticSearch.Core.Options;

var builder = WebApplication.CreateBuilder(args);

// ── Options (strongly-typed, validados al arrancar) ──────────────────────────
builder.Services
    .AddOptions<CoreOptions.OpenAIOptions>()
    .BindConfiguration("AzureOpenAI")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<CoreOptions.SearchOptions>()
    .BindConfiguration("AzureSearch")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<CoreOptions.BlobOptions>()
    .BindConfiguration("AzureBlob")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── Azure clients (singleton — reusan connection pool) ───────────────────────
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<CoreOptions.OpenAIOptions>>().Value;
    return new AzureOpenAIClient(new Uri(opts.Endpoint), new AzureKeyCredential(opts.ApiKey));
});

builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<CoreOptions.SearchOptions>>().Value;
    return new SearchClient(new Uri(opts.Endpoint), opts.IndexName, new AzureKeyCredential(opts.ApiKey));
});

builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<CoreOptions.BlobOptions>>().Value;
    return new BlobServiceClient(opts.ConnectionString);
});

// ── Servicios de dominio ──────────────────────────────────────────────────────
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IBlobService, BlobService>();
builder.Services.AddScoped<IRagService, RagService>();

// ── Auth Azure AD (solo en Production) ───────────────────────────────────────
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration);
    builder.Services.AddAuthorization();
}

// ── OpenAPI / Scalar (solo en Development) ────────────────────────────────────
if (builder.Environment.IsDevelopment())
    builder.Services.AddOpenApi();

// ── Logging estructurado ──────────────────────────────────────────────────────
builder.Logging.AddConsole();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseMiddleware<ExceptionMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapUploadEndpoints();
app.MapQueryEndpoints();
app.MapDocumentEndpoints();
app.MapHealthEndpoints();

app.Run();
