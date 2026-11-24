namespace Intera.Revolut.Api.Models;

public sealed record RevolutAuthorizeUrlDto(
    string Url,
    string RedirectUri);

public sealed record RevolutOAuthStatusDto(
    bool Configured,
    bool HasAccessToken,
    bool HasRefreshToken,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? UpdatedAt,
    string RedirectUri,
    string? RedirectUriSource);
