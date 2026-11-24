using Intera.Common.Infrastructure.Query;

namespace Intera.Revolut.Api.Models.Query;

public static class RevolutExchangeRateQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "from_currency",
        "to_currency",
        "amount",
        "to_amount",
        "rate");
}
