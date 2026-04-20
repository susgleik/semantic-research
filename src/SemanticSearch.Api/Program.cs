using Azure;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using OpenAI;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Scalar.AspNetCore;
using SemanticSearch.Api.Endpoints;
using SemanticSearch.Api.Middleware;
using SemanticSearch.Api.Services;
using System.Threading.RateLimiting;
using CoreOptions = SemanticSearch.Core.Options;

var builder = WebApplication.CreateBuilder(args);

// ── Options (strongly-typed, validados al arrancar) ──────────────────────────
builder.Services
    .AddOptions<CoreOptions.OpenAIOptions>()
    .BindConfiguration("OpenAI")
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

// ── Clientes (singleton — reusan connection pool) ────────────────────────────
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<CoreOptions.OpenAIOptions>>().Value;
    return new OpenAIClient(opts.ApiKey);
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

// ── Auth Azure AD ─────────────────────────────────────────────────────────────
// En Production se valida JWT con Azure AD.
// En Development se registran los servicios base sin esquema → todos los requests pasan.
if (!builder.Environment.IsDevelopment())
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration);
else
    builder.Services.AddAuthentication(); // registra IAuthenticationSchemeProvider sin esquemas

builder.Services.AddAuthorization();

// ── OpenAPI / Scalar (solo en Development) ────────────────────────────────────
if (builder.Environment.IsDevelopment())
    builder.Services.AddOpenApi();

// ── Rate limiting (.NET 10 built-in) ─────────────────────────────────────────
// Límite por IP: 100 requests/minuto en ventana fija, cola de hasta 10.
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = 100,
                Window               = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 10
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Logging estructurado ──────────────────────────────────────────────────────
builder.Logging.AddConsole();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseMiddleware<ExceptionMiddleware>();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapUploadEndpoints();
app.MapQueryEndpoints();
app.MapDocumentEndpoints();
app.MapHealthEndpoints();

app.Run();
