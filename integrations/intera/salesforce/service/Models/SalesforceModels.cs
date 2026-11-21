using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intera.Salesforce.Api.Models;

/// <summary>
/// Salesforce's OAuth2 token response.
/// </summary>
/// <remarks>
/// Note what is <b>not</b> here: <c>expires_in</c>. Salesforce does not send one for the
/// client-credentials grant — an access token lives as long as the org's session timeout allows, and
/// the API never says what that is. <c>SalesforceOptions.AccessTokenLifetimeMinutes</c> is the cache
/// TTL that stands in for it, and the retry on <c>INVALID_SESSION_ID</c> is what actually recovers.
/// </remarks>
public sealed class SalesforceTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    /// <summary>
    /// The host every REST call must then go to. Salesforce returns it per token rather than making
    /// it configuration, and it is not always the login host — which is why this is preferred over
    /// <c>Salesforce:InstanceUrl</c> unless that is set explicitly.
    /// </summary>
    [JsonPropertyName("instance_url")]
    public string? InstanceUrl { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    /// <summary>Identity URL of the run-as user the app acts as. Diagnostics only.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Unix epoch milliseconds, as a string. The only timestamp in the response.</summary>
    [JsonPropertyName("issued_at")]
    public string? IssuedAt { get; set; }
}

/// <summary>Salesforce's error body: always an array, even for a single error.</summary>
public sealed class SalesforceErrorDto
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    /// <summary>Present on a token-endpoint failure, which uses the OAuth error shape instead.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

/// <summary>
/// One page of a SOQL result.
/// </summary>
/// <remarks>
/// <c>done</c> plus <c>nextRecordsUrl</c> are the query locator: a result wider than the batch size
/// arrives in pages, and the next one is fetched from that URL rather than by re-running the query
/// with an offset. Records stay as <see cref="JsonElement"/> because their shape is decided by the
/// SELECT this project just wrote — deserialising into a DTO would mean one DTO per object per
/// field set.
/// </remarks>
public sealed class SalesforceQueryResponse
{
    [JsonPropertyName("totalSize")]
    public int TotalSize { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("nextRecordsUrl")]
    public string? NextRecordsUrl { get; set; }

    [JsonPropertyName("records")]
    public List<JsonElement>? Records { get; set; }
}

/// <summary>Cache state of the access token, for the diagnostics endpoint.</summary>
public sealed record SalesforceOAuthStatusDto(
    bool HasAccessToken,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? AcquiredAt,
    string? InstanceUrl);
