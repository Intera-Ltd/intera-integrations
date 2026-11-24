namespace Intera.Eurobank.Api.Models;

public sealed record EurobankAuthorizeUrlDto(
    string Url,
    string RedirectUri);

public sealed record EurobankOAuthStatusDto(
    bool Configured,
    bool HasAccessToken,
    bool HasRefreshToken,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? UpdatedAt,
    string RedirectUri,
    string? RedirectUriSource);
