using Amazon.Lambda.APIGatewayEvents;
using FluentAssertions;
using SemanticSearch.Core.Auth;

namespace SemanticSearch.Functions.Upload.Tests;

public class CallerIdentityTests
{
    [Fact]
    public void GetOwnerId_ClaimPresent_ReturnsSub()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Authorizer = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription
                {
                    Jwt = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription.JwtDescription
                    {
                        Claims = new Dictionary<string, string> { ["sub"] = "user-1" }
                    }
                }
            }
        };

        CallerIdentity.GetOwnerId(request).Should().Be("user-1");
    }

    [Fact]
    public void GetOwnerId_NoRequestContext_ReturnsEmptyString()
    {
        // Dev local sin Cognito (Fase 12): sam local no valida JWT, no hay Authorizer.
        var request = new APIGatewayHttpApiV2ProxyRequest();

        CallerIdentity.GetOwnerId(request).Should().Be("");
    }

    [Fact]
    public void GetOwnerId_AuthorizerWithoutJwt_ReturnsEmptyString()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Authorizer = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription()
            }
        };

        CallerIdentity.GetOwnerId(request).Should().Be("");
    }

    [Fact]
    public void GetOwnerId_SubClaimWhitespace_ReturnsEmptyString()
    {
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Authorizer = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription
                {
                    Jwt = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription.JwtDescription
                    {
                        Claims = new Dictionary<string, string> { ["sub"] = "   " }
                    }
                }
            }
        };

        CallerIdentity.GetOwnerId(request).Should().Be("");
    }
}
