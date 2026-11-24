using Intera.Common.Infrastructure.Query;

namespace Intera.BusinessCentral.Api.Models.Query;

public static class BusinessCentralSalesInvoiceQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "id",
        "number",
        "external_document_number",
        "invoice_date",
        "posting_date",
        "due_date",
        "customer_id",
        "customer_number",
        "customer_name",
        "currency_code",
        "status",
        "discount_amount",
        "total_amount_excluding_tax",
        "total_tax_amount",
        "total_amount_including_tax",
        "remaining_amount",
        "last_modified_date_time");
}
