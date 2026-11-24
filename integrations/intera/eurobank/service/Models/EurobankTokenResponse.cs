using System.Text.Json.Serialization;

namespace Intera.Eurobank.Api.Models;

/// <summary>
/// Shape of the <c>POST {AuthBaseUrl}/v2/token/exchange</c> and <c>/v2/token</c> responses per the OpenAPI
/// specification (<c>TokenPairObject</c>): <c>expires_at</c>, <c>refresh_expires_at</c>, and <c>created_on</c> are
/// absolute epoch-millisecond timestamps, and <c>scope</c> is an array.
/// </summary>
public sealed class EurobankTokenResponse
{
    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("client_id")]
    public string? ClientId { get; set; }

    [JsonPropertyName("scope")]
    public List<string>? Scope { get; set; }

    [JsonPropertyName("created_on")]
    public long? CreatedOn { get; set; }

    [JsonPropertyName("expires_at")]
    public long? ExpiresAt { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("refresh_expires_at")]
    public long? RefreshExpiresAt { get; set; }
}
