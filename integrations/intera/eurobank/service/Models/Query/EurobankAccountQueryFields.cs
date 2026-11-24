using Intera.Common.Infrastructure.Query;

namespace Intera.Eurobank.Api.Models.Query;

public static class EurobankAccountQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "account_name",
        "account_type",
        "account_type_description",
        "iban",
        "account_number",
        "currency",
        "available_balance",
        "current_balance",
        "status");
}
