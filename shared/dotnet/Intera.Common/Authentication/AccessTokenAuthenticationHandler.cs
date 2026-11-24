using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Intera.Common.Authentication;

public sealed class AccessTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAccessTokenStore tokenStore)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = TryGetBearerToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        var tokenInfo = await tokenStore.TryValidateAsync(token, Context.RequestAborted).ConfigureAwait(false);
        if (tokenInfo is null)
        {
            return AuthenticateResult.Fail("Invalid service token.");
        }

        return SuccessForId(tokenInfo.Id);
    }

    private AuthenticateResult SuccessForId(long id)
    {
        var idText = id.ToString();
        var claims = new[]
        {
            new Claim(AccessTokenAuthenticationDefaults.TokenIdClaimType, idText),
            new Claim(ClaimTypes.NameIdentifier, idText)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    private string? TryGetBearerToken()
    {
        var authorization = Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";

        return authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[bearerPrefix.Length..].Trim()
            : null;
    }
}
