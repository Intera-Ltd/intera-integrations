using Intera.Common.Models.Query;
using Intera.Salesforce.Api.Clients;
using Intera.Salesforce.Api.Infrastructure.Soql;
using Intera.Salesforce.Api.Options;
using Microsoft.Extensions.Options;

namespace Intera.Salesforce.Api.Services;

/// <summary>
/// The rows of one query, plus the size of the whole filtered set when the caller asked for a page.
/// </summary>
public sealed record SObjectQueryResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    long? TotalCount);

public interface ISObjectQueryService
{
    Task<SObjectQueryResult> QueryAsync(SoqlObject sobject, QueryRequest? query, CancellationToken cancellationToken);
}

/// <summary>
/// Compiles a <see cref="QueryRequest"/> to SOQL, runs it, and reads the answer back into rows.
/// </summary>
/// <remarks>
/// Every endpoint in this project is this method with a different <see cref="SoqlObject"/>, which is
/// what makes the six controllers three lines each: the object's field list is the only thing that
/// differs between them, and there is no per-object fetching, mapping or shaping code to drift.
/// </remarks>
public sealed class SObjectQueryService(
    ISalesforceClient client,
    IOptions<SalesforceOptions> options,
    ILogger<SObjectQueryService> logger) : ISObjectQueryService
{
    public async Task<SObjectQueryResult> QueryAsync(
        SoqlObject sobject,
        QueryRequest? query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sobject);

        var defaultLimit = Math.Max(1, options.Value.DefaultLimit);
        var built = new SoqlQueryBuilder(sobject).Build(query, defaultLimit);

        logger.LogDebug("SOQL: {Soql}", built.Soql);

        var records = await client
            .QueryAsync(built.Soql, query?.Top ?? defaultLimit, cancellationToken)
            .ConfigureAwait(false);

        var rows = SoqlRecordReader.Read(records, built.Projection);

        if (query?.Skip is null)
        {
            return new SObjectQueryResult(rows, null);
        }

        // Only a paging caller pays for the second round-trip, the same rule the SQL path follows —
        // and it is a round-trip here, not the free count the in-memory engines report, since the
        // whole set never came over the wire.
        var countSoql = new SoqlQueryBuilder(sobject).BuildCount(query);
        var total = await client.CountAsync(countSoql, cancellationToken).ConfigureAwait(false);

        return new SObjectQueryResult(rows, total);
    }
}
