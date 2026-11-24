using Intera.Common.Infrastructure.Query;

namespace Intera.BusinessCentral.Api.Models.Query;

public static class BusinessCentralPurchaseInvoiceQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "id",
        "number",
        "vendor_invoice_number",
        "invoice_date",
        "posting_date",
        "due_date",
        "vendor_id",
        "vendor_number",
        "vendor_name",
        "currency_code",
        "status",
        "total_amount_excluding_tax",
        "total_tax_amount",
        "total_amount_including_tax",
        "remaining_amount",
        "last_modified_date_time");
}
