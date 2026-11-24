using System.Text.Json.Serialization;

namespace Intera.BusinessCentral.Api.Models;

/// <summary>Response shape of Entra ID's client-credentials token endpoint. There is no refresh_token in this grant.</summary>
public sealed class BusinessCentralTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }
}
