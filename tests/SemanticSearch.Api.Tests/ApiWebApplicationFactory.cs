using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SemanticSearch.Api.Services;

namespace SemanticSearch.Api.Tests;

/// <summary>
/// Arranca la API en memoria con servicios de Azure reemplazados por mocks.
/// WebApplicationFactory usa TestServer (sin Kestrel), así que los límites
/// de tamaño de request de Kestrel no aplican — los checks llegan al endpoint.
/// </summary>
public class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public Mock<IRagService>    RagService    { get; } = new();
    public Mock<IBlobService>   BlobService   { get; } = new();
    public Mock<ISearchService> SearchService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Valores fake que pasan ValidateOnStart() (campos [Required] no vacíos).
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"]              = "fake-openai-key",
                ["OpenAI:EmbeddingDeployment"] = "text-embedding-3-large",
                ["OpenAI:ChatDeployment"]      = "gpt-4o",
                ["AzureSearch:Endpoint"]            = "https://fake.search.windows.net",
                ["AzureSearch:ApiKey"]              = "fake-search-key",
                ["AzureSearch:IndexName"]           = "documents",
                ["AzureBlob:ConnectionString"]      =
                    "DefaultEndpointsProtocol=https;AccountName=fakeaccount;" +
                    "AccountKey=ZmFrZWtleWZha2VrZXlmYWtla2V5ZmFrZWtla2V5ZmFrZWs=;" +
                    "EndpointSuffix=core.windows.net",
                ["AzureBlob:Container"]             = "docs",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Handler de auth que siempre aprueba — necesario porque los endpoints
            // usan RequireAuthorization() y UseAuthorization() llama a AuthenticateAsync.
            // Sin un esquema registrado, el middleware lanza InvalidOperationException.
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, PassThroughAuthHandler>("Test", _ => { });

            // Reemplaza servicios de dominio con mocks.
            services.RemoveAll<IRagService>();
            services.AddScoped<IRagService>(_ => RagService.Object);

            services.RemoveAll<IBlobService>();
            services.AddScoped<IBlobService>(_ => BlobService.Object);

            services.RemoveAll<ISearchService>();
            services.AddScoped<ISearchService>(_ => SearchService.Object);
        });
    }
}

/// <summary>
/// Handler de autenticación para tests: siempre retorna un ticket válido.
/// </summary>
file sealed class PassThroughAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity  = new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
