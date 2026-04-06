namespace SemanticSearch.Api.Middleware;

// La validación de JWT con Azure AD está cubierta por Microsoft.Identity.Web
// configurado en Program.cs con AddMicrosoftIdentityWebApiAuthentication.
// Este archivo es un placeholder para lógica adicional de autorización si se necesita.
public class AuthMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context) => next(context);
}
