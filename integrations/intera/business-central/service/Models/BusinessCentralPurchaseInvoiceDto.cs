using System.Text.Json.Serialization;

namespace Intera.BusinessCentral.Api.Models;

public sealed class BusinessCentralPurchaseInvoiceDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("vendorInvoiceNumber")]
    public string? VendorInvoiceNumber { get; set; }

    [JsonPropertyName("invoiceDate")]
    public DateOnly? InvoiceDate { get; set; }

    [JsonPropertyName("postingDate")]
    public DateOnly? PostingDate { get; set; }

    [JsonPropertyName("dueDate")]
    public DateOnly? DueDate { get; set; }

    [JsonPropertyName("vendorId")]
    public string? VendorId { get; set; }

    [JsonPropertyName("vendorNumber")]
    public string? VendorNumber { get; set; }

    [JsonPropertyName("vendorName")]
    public string? VendorName { get; set; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("totalAmountExcludingTax")]
    public decimal? TotalAmountExcludingTax { get; set; }

    [JsonPropertyName("totalTaxAmount")]
    public decimal? TotalTaxAmount { get; set; }

    [JsonPropertyName("totalAmountIncludingTax")]
    public decimal? TotalAmountIncludingTax { get; set; }

    [JsonPropertyName("remainingAmount")]
    public decimal? RemainingAmount { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }
}
