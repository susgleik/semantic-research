using Azure;
using Azure.Search.Documents.Indexes;
using OpenAI;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SemanticSearch.Core.Options;
using SemanticSearch.Functions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        services.AddOptions<OpenAIOptions>()
            .BindConfiguration("OpenAI")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SearchOptions>()
            .BindConfiguration("AzureSearch")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;
            return new OpenAIClient(opts.ApiKey);
        });

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SearchOptions>>().Value;
            return new SearchIndexClient(new Uri(opts.Endpoint), new AzureKeyCredential(opts.ApiKey));
        });

        services.AddSingleton<ChunkerService>();
        services.AddScoped<EmbeddingService>();
        services.AddScoped<SearchIndexerService>();
    })
    .Build();

host.Run();
