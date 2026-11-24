namespace Intera.Common.Infrastructure.Options;

/// <summary>
/// Cross-cutting host settings shared by every API project, bound from the <c>Api</c> section.
/// </summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>
    /// Optional shared inbound access token (<c>Api:AccessToken</c>). Individual services may also bind
    /// platform-managed <c>INTERA_SERVICE_TOKEN</c> credentials.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Whether to serve Swagger UI at <c>/swagger</c>. <see langword="null"/> (the default) keeps the
    /// original behaviour — on in <c>Development</c>, off everywhere else; <see langword="true"/> or
    /// <see langword="false"/> decides it outright regardless of environment.
    /// </summary>
    /// <remarks>
    /// Tri-state rather than a plain <c>bool</c> so that "not configured" stays distinguishable from
    /// "explicitly off": a plain bool defaulting to false would have silently killed Swagger in
    /// Development for every existing checkout.
    /// <para>
    /// Turning this on in production publishes the endpoint list and schemas, not data — every endpoint
    /// still requires the access token — but it is off by default because there is no reason to
    /// advertise the surface to anyone who has not been given a token.
    /// </para>
    /// </remarks>
    public bool? EnableSwagger { get; set; }
}
