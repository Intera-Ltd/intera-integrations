using Intera.Common.Infrastructure.Query;

namespace Intera.Revolut.Api.Models.Query;

public static class RevolutBalanceQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "account_id",
        "account_name",
        "currency",
        "balance",
        "state");
}
