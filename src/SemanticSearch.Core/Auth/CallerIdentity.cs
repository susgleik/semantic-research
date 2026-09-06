using Amazon.Lambda.APIGatewayEvents;

namespace SemanticSearch.Core.Auth;

public static class CallerIdentity
{
    // Nunca lanza: sin JWT (dev local sin Cognito, Fase 12) devuelve "", el mismo
    // sentinel que un ChunkRecord.OwnerId legacy/compartido.
    public static string GetOwnerId(APIGatewayHttpApiV2ProxyRequest request) =>
        request.RequestContext?.Authorizer?.Jwt?.Claims is { } claims &&
        claims.TryGetValue("sub", out var sub) && !string.IsNullOrWhiteSpace(sub)
            ? sub
            : "";
}
