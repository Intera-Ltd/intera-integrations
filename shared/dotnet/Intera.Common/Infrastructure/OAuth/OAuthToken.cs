namespace Intera.Common.Infrastructure.OAuth;

public sealed record OAuthToken(
    string Provider,
    string? AccessToken,
    string? RefreshToken,
    string? TokenType,
    string? Scope,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset UpdatedAt);
