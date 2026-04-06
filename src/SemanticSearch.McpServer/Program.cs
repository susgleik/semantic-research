using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SemanticSearch.McpServer.Tools;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        var apiUrl = ctx.Configuration["API_URL"] ?? "http://localhost:8080";

        services.AddHttpClient<SearchDocumentsTool>(client =>
            client.BaseAddress = new Uri(apiUrl));

        services.AddHttpClient<ListDocumentsTool>(client =>
            client.BaseAddress = new Uri(apiUrl));

        services.AddHttpClient<ReindexDocumentTool>(client =>
            client.BaseAddress = new Uri(apiUrl));
    })
    .Build();

await host.RunAsync();
