using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using SemanticSearch.Api.Endpoints;
using SemanticSearch.Api.Middleware;
using SemanticSearch.Api.Services;
using SemanticSearch.Core.Options;

var builder = WebApplication.CreateBuilder(args);

// ── Options (strongly-typed, validados al arrancar) ──────────────────────────
builder.Services
    .AddOptions<OpenAIOptions>()
    .BindConfiguration("AzureOpenAI")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<SearchOptions>()
    .BindConfiguration("AzureSearch")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<BlobOptions>()
    .BindConfiguration("AzureBlob")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── Azure clients (singleton — reusan connection pool) ───────────────────────
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;
    return new AzureOpenAIClient(new Uri(opts.Endpoint), new AzureKeyCredential(opts.ApiKey));
});

builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<SearchOptions>>().Value;
    return new SearchClient(new Uri(opts.Endpoint), opts.IndexName, new AzureKeyCredential(opts.ApiKey));
});

builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<BlobOptions>>().Value;
    return new BlobServiceClient(opts.ConnectionString);
});

// ── Servicios de dominio ──────────────────────────────────────────────────────
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IBlobService, BlobService>();
builder.Services.AddScoped<IRagService, RagService>();

// ── Auth Azure AD ─────────────────────────────────────────────────────────────
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// ── Logging estructurado ──────────────────────────────────────────────────────
builder.Logging.AddConsole();

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapUploadEndpoints();
app.MapQueryEndpoints();
app.MapDocumentEndpoints();
app.MapHealthEndpoints();

app.Run();
