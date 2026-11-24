namespace Intera.BusinessCentral.Api.Models;

public sealed record BusinessCentralOAuthStatusDto(
    bool HasAccessToken,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? AcquiredAt);
