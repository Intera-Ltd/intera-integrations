using System.Text.Json.Serialization;

namespace Intera.BusinessCentral.Api.Models;

public sealed class BusinessCentralVendorDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("addressLine1")]
    public string? AddressLine1 { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("balance")]
    public decimal? Balance { get; set; }

    [JsonPropertyName("blocked")]
    public string? Blocked { get; set; }

    [JsonPropertyName("taxRegistrationNumber")]
    public string? TaxRegistrationNumber { get; set; }

    [JsonPropertyName("paymentTermsId")]
    public string? PaymentTermsId { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }
}
