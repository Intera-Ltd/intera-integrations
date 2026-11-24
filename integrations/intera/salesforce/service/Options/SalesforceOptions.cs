namespace Intera.Salesforce.Api.Options;

public sealed class SalesforceOptions
{
    public const string SectionName = "Salesforce";

    /// <summary>
    /// The org's login host — its My Domain URL, e.g.
    /// <c>https://acme.my.salesforce.com</c>, or <c>https://acme--dev.sandbox.my.salesforce.com</c>
    /// for a sandbox.
    /// </summary>
    /// <remarks>
    /// The generic <c>login.salesforce.com</c> / <c>test.salesforce.com</c> hosts do not serve the
    /// client-credentials grant — Salesforce requires the org's own domain for it — so this is the
    /// one value a deployment cannot guess.
    /// </remarks>
    public string LoginUrl { get; set; } = string.Empty;

    /// <summary>Consumer Key of the connected app / external client app.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Consumer Secret of the same app.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// REST API version the query path is built from, e.g. <c>v67.0</c>.
    /// </summary>
    /// <remarks>
    /// Pinned rather than discovered. Salesforce keeps every published version alive for years, and a
    /// version that moves on its own would change field availability and response shapes under a
    /// dashboard that never asked for it.
    /// </remarks>
    public string ApiVersion { get; set; } = "v67.0";

    /// <summary>
    /// Overrides the instance host the queries go to. Left empty — the normal case — the host comes
    /// from the token response's own <c>instance_url</c>, which is the value Salesforce says to use
    /// and follows the org through a My Domain change or a sandbox refresh.
    /// </summary>
    public string InstanceUrl { get; set; } = string.Empty;

    /// <summary>
    /// How long an access token is treated as usable.
    /// </summary>
    /// <remarks>
    /// A guess, and unavoidably one: Salesforce's token response carries no <c>expires_in</c> for
    /// this grant — the lifetime is the org's session timeout (Setup → Session Settings), which the
    /// API never states. This is therefore a cache TTL, not an expiry; the actual recovery from an
    /// expired session is the single retry on <c>INVALID_SESSION_ID</c> in
    /// <c>SalesforceSession.InvokeAsync</c>, which works whatever the org's timeout happens to be.
    /// Setting this well under the org's timeout only saves the occasional wasted round-trip.
    /// </remarks>
    public int AccessTokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// <c>LIMIT</c> applied to a query that names no <c>top</c> of its own.
    /// </summary>
    /// <remarks>
    /// This is not a silent truncation of the answer: the
    /// filter, the ordering and the grouping all ran upstream first, so what it caps is a deliberate
    /// "give me everything matching" — and a passthrough over an org's whole Account table is not
    /// something a dashboard should be able to ask for by omission.
    /// </remarks>
    public int DefaultLimit { get; set; } = 2000;

    /// <summary>
    /// Rows per response batch, i.e. Salesforce's <c>Sforce-Query-Options</c> header.
    /// </summary>
    /// <remarks>
    /// 2000 is also Salesforce's maximum and its default. Lowering it costs more round-trips for the
    /// same rows and is only worth doing when the records are wide enough for the response to time
    /// out.
    /// </remarks>
    public int BatchSize { get; set; } = 2000;

    public int HttpTimeoutSeconds { get; set; } = 100;
}
