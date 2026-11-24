using Intera.Common.Infrastructure.Query;

namespace Intera.BusinessCentral.Api.Models.Query;

public static class BusinessCentralCompanyQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "id",
        "name",
        "display_name",
        "business_profile_id");
}
