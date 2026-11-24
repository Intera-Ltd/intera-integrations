using System.Text.Json.Serialization;

namespace Intera.BusinessCentral.Api.Models;

public sealed class BusinessCentralSalesInvoiceDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("externalDocumentNumber")]
    public string? ExternalDocumentNumber { get; set; }

    [JsonPropertyName("invoiceDate")]
    public DateOnly? InvoiceDate { get; set; }

    [JsonPropertyName("postingDate")]
    public DateOnly? PostingDate { get; set; }

    [JsonPropertyName("dueDate")]
    public DateOnly? DueDate { get; set; }

    [JsonPropertyName("customerId")]
    public string? CustomerId { get; set; }

    [JsonPropertyName("customerNumber")]
    public string? CustomerNumber { get; set; }

    [JsonPropertyName("customerName")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("discountAmount")]
    public decimal? DiscountAmount { get; set; }

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
