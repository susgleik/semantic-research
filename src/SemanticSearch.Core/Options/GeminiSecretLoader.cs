using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

namespace SemanticSearch.Core.Options;

/// <summary>
/// Resuelve GEMINI_API_KEY sin pasarla nunca como variable de entorno en producción.
/// En AWS real, GEMINI_API_KEY_SSM_PARAM apunta al parámetro SecureString y el valor
/// se busca una vez por contenedor Lambda (cacheado igual que el HttpClient estático).
/// En local (Fase 12), esa variable no existe y cae al GEMINI_API_KEY plano de siempre.
/// </summary>
public static class GeminiSecretLoader
{
    private static readonly Lazy<string> CachedApiKey = new(() => FetchApiKey().GetAwaiter().GetResult());

    public static string ApiKey => CachedApiKey.Value;

    private static async Task<string> FetchApiKey()
    {
        var paramName = Environment.GetEnvironmentVariable("GEMINI_API_KEY_SSM_PARAM");
        if (string.IsNullOrEmpty(paramName))
            return Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";

        using var ssm = new AmazonSimpleSystemsManagementClient();
        var response = await ssm.GetParameterAsync(new GetParameterRequest
        {
            Name = paramName,
            WithDecryption = true
        });
        return response.Parameter.Value;
    }
}
