using System.Text.Json.Serialization;

namespace Intera.BusinessCentral.Api.Models;

/// <summary>Generic envelope BC's OData v4 API wraps every collection response in.</summary>
internal sealed class ODataCollectionResponse<T>
{
    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = [];

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }
}
