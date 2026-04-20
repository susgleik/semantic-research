using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using SemanticSearch.Api.Models;
using SemanticSearch.Core.Options;

namespace SemanticSearch.Api.Services;

public class RagService(
    IEmbeddingService embeddings,
    ISearchService search,
    OpenAIClient openAiClient,
    IOptions<OpenAIOptions> opts,
    ILogger<RagService> logger) : IRagService
{
    private readonly string _chatDeployment = opts.Value.ChatDeployment;

    public async Task<QueryResponse> QueryAsync(QueryRequest request, CancellationToken ct = default)
    {
        // 1. Embed la pregunta del usuario
        logger.LogInformation("Generating embedding for query");
        var queryVector = await embeddings.EmbedAsync(request.Query, ct);

        // 2. Búsqueda híbrida: vector + keyword
        logger.LogInformation("Running hybrid search, top_k={TopK}", request.TopK);
        var chunks = await search.HybridSearchAsync(request.Query, queryVector, request.TopK, request.Filter, ct);

        // 3. Construir prompt RAG con el contexto recuperado
        var ragPrompt = BuildRagPrompt(request.Query, chunks);

        // 4. Completions con GPT-4o
        logger.LogInformation("Calling GPT-4o completions");
        var chatClient = openAiClient.GetChatClient(_chatDeployment);

        var completion = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage("""
                    Sos un asistente que responde preguntas basándose exclusivamente
                    en los fragmentos de documentos provistos. Si la respuesta no está
                    en los fragmentos, indicalo claramente. Respondé en el mismo idioma
                    que la pregunta.
                    """),
                new UserChatMessage(ragPrompt)
            ],
            new ChatCompletionOptions { Temperature = 0.1f, MaxOutputTokenCount = 1500 },
            ct
        );

        var answer = completion.Value.Content[0].Text;
        return new QueryResponse(answer, chunks);
    }

    private static string BuildRagPrompt(string query, IReadOnlyList<SourceChunk> chunks)
    {
        var context = string.Join("\n\n", chunks.Select((c, i) =>
            $"[Fragmento {i + 1} — {c.Filename}, página {c.Page}]\n{c.Chunk}"));

        return $"""
            Pregunta: {query}

            Fragmentos de documentos relevantes:
            {context}

            Respondé la pregunta basándote en los fragmentos anteriores.
            """;
    }
}
