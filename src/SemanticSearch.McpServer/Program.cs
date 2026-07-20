using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SemanticSearch.McpServer.Tools;

var builder = Host.CreateApplicationBuilder(args);

// stdout está reservado para los mensajes JSON-RPC del transporte stdio del MCP —
// los logs van a stderr, si no Copilot Chat no puede parsear la salida del proceso.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var apiUrl = builder.Configuration["API_URL"]
    ?? throw new InvalidOperationException("Falta API_URL (URL de API Gateway o del gateway local).");
var accessToken = builder.Configuration["MCP_ACCESS_TOKEN"];

// Un único HttpClient singleton, no un typed client por tool via AddHttpClient<T>():
// el SDK de MCP construye las instancias de las tools sin pasar por el mecanismo de
// typed client (usa ActivatorUtilities directo), así que terminaba resolviendo un
// HttpClient default sin BaseAddress. Registrando la instancia ya configurada como
// singleton no hay ambigüedad posible en cómo se resuelve el parámetro del constructor.
var apiHttpClient = new HttpClient { BaseAddress = new Uri(apiUrl) };
// Todas las rutas exigen JWT de Cognito salvo /health (Fase 6) — sin esto, cada
// llamada de una tool devuelve 401. El token se obtiene a mano (ver README de Fase 8)
// y no se refresca solo: expira a la hora, como cualquier access token de Cognito.
if (!string.IsNullOrEmpty(accessToken))
    apiHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

builder.Services.AddSingleton(apiHttpClient);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SearchDocumentsTool>()
    .WithTools<ListDocumentsTool>()
    .WithTools<ReindexDocumentTool>();

await builder.Build().RunAsync();
