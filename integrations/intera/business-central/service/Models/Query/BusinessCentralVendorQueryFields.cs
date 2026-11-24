using Intera.Common.Infrastructure.Query;

namespace Intera.BusinessCentral.Api.Models.Query;

public static class BusinessCentralVendorQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "id",
        "number",
        "display_name",
        "address_line1",
        "city",
        "country",
        "postal_code",
        "phone_number",
        "email",
        "currency_code",
        "balance",
        "blocked",
        "tax_registration_number",
        "payment_terms_id",
        "last_modified_date_time");
}
