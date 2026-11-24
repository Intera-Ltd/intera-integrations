using Intera.Common.Infrastructure.Query;

namespace Intera.Revolut.Api.Models.Query;

public static class RevolutAccountQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "id",
        "name",
        "currency",
        "balance",
        "state",
        "public");
}
