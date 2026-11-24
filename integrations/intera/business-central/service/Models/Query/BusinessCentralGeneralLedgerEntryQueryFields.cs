using Intera.Common.Infrastructure.Query;

namespace Intera.BusinessCentral.Api.Models.Query;

/// <summary>
/// No dimension_1/dimension_2-style fields here on purpose: BC's standard API exposes dimensions as a
/// separate generalLedgerEntries({id})/dimensionSetLines sub-collection, not flat columns on the entry.
/// Revisit only if the tenant's BC is confirmed to run a custom AL extension that projects dimension
/// values onto this entity.
/// </summary>
public static class BusinessCentralGeneralLedgerEntryQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "id",
        "entry_number",
        "posting_date",
        "document_number",
        "document_type",
        "account_id",
        "account_number",
        "description",
        "debit_amount",
        "credit_amount",
        "additional_currency_debit_amount",
        "additional_currency_credit_amount");
}
