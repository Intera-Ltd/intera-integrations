using Intera.Common.Infrastructure.Http;
using Intera.Soft1.Api.Infrastructure;
using Intera.Soft1.Api.Options;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Intera.Soft1.Api.Clients;

/// <summary>A browser's answer: its column names and every row, each value as Soft1 sent it.</summary>
/// <param name="Fields">
/// Column names as <c>getBrowserInfo</c> lists them, e.g. <c>ZOOMINFO</c>, <c>CUSTOMER.CODE</c>. A row holds one
/// value per name, in this order.
/// </param>
public sealed record Soft1Browser(IReadOnlyList<string> Fields, IReadOnlyList<string?[]> Rows, int TotalCount);

/// <summary>One company/branch/module the web account may sign in to, as <c>login</c> lists them.</summary>
public sealed record Soft1LoginTarget(
    string? Company,
    string? CompanyName,
    string? Branch,
    string? BranchName,
    string? Module,
    string? ModuleName,
    string? RefId,
    string? RefIdName);

public interface ISoft1Client
{
    /// <summary>
    /// Runs a browser (<c>getBrowserInfo</c>, then <c>getBrowserData</c> page by page) and returns every row.
    /// More than <paramref name="maxRows"/> rows is refused before any page is read.
    /// </summary>
    Task<Soft1Browser> ReadBrowserAsync(string obj, string list, string filters, int maxRows, CancellationToken cancellationToken);

    /// <summary>The browser's columns and its first <paramref name="limit"/> rows — one call, for diagnostics.</summary>
    Task<Soft1Browser> PeekBrowserAsync(string obj, string list, string filters, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// One record (<c>getData</c>), restricted to the tables and fields <paramref name="locateInfo"/> names
    /// (<c>TABLE:FIELD,FIELD;TABLE:FIELD</c>). The answer is <c>data</c>: table name → array of row objects.
    /// </summary>
    Task<JsonElement> GetRecordAsync(string obj, string key, string locateInfo, CancellationToken cancellationToken);

    /// <summary>Any service, answered as its whole JSON body — the metadata calls behind the schema endpoints.</summary>
    Task<JsonElement> CallAsync(string service, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken);

    /// <summary>
    /// Reads rows of one database table (<c>selectorFields</c>): the <paramref name="resultFields"/> of every
    /// row whose <paramref name="keyName"/> equals <paramref name="keyValue"/>. No paging exists for this service,
    /// so the whole answer arrives at once.
    /// </summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> ReadTableAsync(
        string table,
        string keyName,
        string keyValue,
        IReadOnlyCollection<string> resultFields,
        CancellationToken cancellationToken);

    /// <summary>Signs in without authenticating and returns what the web account may authenticate into.</summary>
    Task<IReadOnlyList<Soft1LoginTarget>> GetLoginTargetsAsync(CancellationToken cancellationToken);

    /// <summary>The session in use, opening one if needed — for the company the tables are narrowed to.</summary>
    Task<Soft1SessionInfo> GetSessionAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Thin client over Soft1's <c>s1services</c>: opens and keeps the session, repeats the application id on every
/// call, reads browsers page by page. It knows nothing about the query DSL.
/// </summary>
public sealed class Soft1Client(
    IOptions<Soft1Options> options,
    ISoft1Transport transport,
    Soft1SessionCache sessionCache,
    Soft1RequestGate requestGate,
    ILogger<Soft1Client> logger) : ISoft1Client
{
    private Soft1Options Options => options.Value;

    public async Task<Soft1Browser> ReadBrowserAsync(
        string obj,
        string list,
        string filters,
        int maxRows,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(obj);

        var pageSize = Math.Clamp(Options.PageSize, 1, 5000);

        // VERSION 2 answers the first page together with the browser's description, so a browser that fits in
        // one page costs one call.
        var info = await CallAsync(
            "getBrowserInfo",
            new Dictionary<string, object?>
            {
                ["OBJECT"] = obj,
                ["LIST"] = list ?? string.Empty,
                ["FILTERS"] = filters ?? string.Empty,
                ["VERSION"] = 2,
                ["LIMIT"] = pageSize
            },
            cancellationToken).ConfigureAwait(false);

        var fields = ReadFieldNames(info);
        var total = ReadInt(info, "totalcount") ?? 0;

        if (total > maxRows)
        {
            throw new Soft1QueryTooBroadException(
                $"The Soft1 browser {obj}{(string.IsNullOrEmpty(list) ? string.Empty : "/" + list)} holds {total} rows for this query, " +
                $"more than Soft1:MaxRowsPerRead ({maxRows}).");
        }

        var rows = new List<string?[]>(Math.Min(total, maxRows));
        rows.AddRange(ReadRows(info));

        var requestId = ReadString(info, "reqID");

        while (rows.Count < total && requestId is not null)
        {
            var page = await CallAsync(
                "getBrowserData",
                new Dictionary<string, object?>
                {
                    ["reqID"] = requestId,
                    ["START"] = rows.Count,
                    ["LIMIT"] = pageSize
                },
                cancellationToken).ConfigureAwait(false);

            var before = rows.Count;
            rows.AddRange(ReadRows(page));

            if (rows.Count == before)
            {
                logger.LogWarning(
                    "Soft1 browser {Object} stopped at {Count} of {Total} rows (an empty page).", obj, rows.Count, total);
                break;
            }
        }

        return new Soft1Browser(fields, rows, total);
    }

    public async Task<Soft1Browser> PeekBrowserAsync(
        string obj,
        string list,
        string filters,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(obj);

        var info = await CallAsync(
            "getBrowserInfo",
            new Dictionary<string, object?>
            {
                ["OBJECT"] = obj,
                ["LIST"] = list ?? string.Empty,
                ["FILTERS"] = filters ?? string.Empty,
                ["VERSION"] = 2,
                ["LIMIT"] = Math.Clamp(limit, 1, 100)
            },
            cancellationToken).ConfigureAwait(false);

        return new Soft1Browser(ReadFieldNames(info), [.. ReadRows(info)], ReadInt(info, "totalcount") ?? 0);
    }

    public async Task<JsonElement> GetRecordAsync(string obj, string key, string locateInfo, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(obj);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var answer = await CallAsync(
            "getData",
            new Dictionary<string, object?>
            {
                ["OBJECT"] = obj,
                ["KEY"] = key,
                ["LOCATEINFO"] = locateInfo ?? string.Empty
            },
            cancellationToken).ConfigureAwait(false);

        return answer.TryGetProperty("data", out var data) ? data : default;
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> ReadTableAsync(
        string table,
        string keyName,
        string keyValue,
        IReadOnlyCollection<string> resultFields,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(resultFields);

        var answer = await CallAsync(
            "selectorFields",
            new Dictionary<string, object?>
            {
                ["TABLENAME"] = table,
                ["KEYNAME"] = keyName ?? string.Empty,
                ["KEYVALUE"] = keyValue ?? string.Empty,
                ["RESULTFIELDS"] = string.Join(',', resultFields)
            },
            cancellationToken).ConfigureAwait(false);

        var rows = new List<IReadOnlyDictionary<string, string?>>();

        if (answer.TryGetProperty("rows", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.EnumerateObject())
                {
                    row[property.Name] = Soft1Values.Text(property.Value);
                }

                rows.Add(row);
            }
        }

        return rows;
    }

    public Task<Soft1SessionInfo> GetSessionAsync(CancellationToken cancellationToken) =>
        sessionCache.GetOrSignInAsync(SignInAsync, cancellationToken);

    public async Task<JsonElement> CallAsync(
        string service,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentNullException.ThrowIfNull(parameters);

        var session = await sessionCache.GetOrSignInAsync(SignInAsync, cancellationToken).ConfigureAwait(false);

        try
        {
            return await SendAsync(service, session.ClientId, parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (Soft1ServiceException exception) when (!exception.IsSessionLost)
        {
            throw Soft1Errors.Translate(exception);
        }
        catch (Soft1ServiceException exception)
        {
            // Once: the session lapsed (its lifetime is a setting of the web account) or the server restarted.
            // A second failure is not a lapse, and retrying further would turn it into a sign-in loop.
            logger.LogInformation("Soft1 session was lost ({Code}); signing in again.", exception.Code);
            sessionCache.Invalidate(session);
        }

        var renewed = await sessionCache.GetOrSignInAsync(SignInAsync, cancellationToken).ConfigureAwait(false);

        try
        {
            return await SendAsync(service, renewed.ClientId, parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (Soft1ServiceException exception)
        {
            throw Soft1Errors.Translate(exception);
        }
    }

    public async Task<IReadOnlyList<Soft1LoginTarget>> GetLoginTargetsAsync(CancellationToken cancellationToken)
    {
        var (_, targets) = await LoginAsync(cancellationToken).ConfigureAwait(false);
        return targets;
    }

    private async Task<JsonElement> SendAsync(
        string service,
        string clientId,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>(parameters)
        {
            ["service"] = service,
            ["clientID"] = clientId,
            ["appId"] = Options.AppId
        };

        return await requestGate.RunAsync(
            async ct =>
            {
                using var document = await transport.PostAsync(Options.ServiceUrl, body, ct).ConfigureAwait(false);
                return document.RootElement.Clone();
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>login</c>, then <c>authenticate</c> into the configured company/branch/module/refid — the two-step
    /// flow Soft1 documents first. The one-step form (the four values sent with <c>login</c>) would save a call
    /// per sign-in but cannot tell a first installation which values exist.
    /// </summary>
    private async Task<Soft1SessionInfo> SignInAsync(CancellationToken cancellationToken)
    {
        var (loginClientId, targets) = await LoginAsync(cancellationToken).ConfigureAwait(false);
        var target = PickTarget(targets);

        JsonElement answer;
        try
        {
            answer = await requestGate.RunAsync(
                async ct =>
                {
                    using var document = await transport.PostAsync(
                        Options.ServiceUrl,
                        new Dictionary<string, object?>
                        {
                            ["service"] = "authenticate",
                            ["clientID"] = loginClientId,
                            ["COMPANY"] = target.Company,
                            ["BRANCH"] = target.Branch,
                            ["MODULE"] = target.Module,
                            ["REFID"] = target.RefId
                        },
                        ct).ConfigureAwait(false);
                    return document.RootElement.Clone();
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Soft1ServiceException exception)
        {
            throw Soft1Errors.Translate(exception);
        }

        var clientId = ReadString(answer, "clientID")
            ?? throw new Soft1AuthenticationException("Soft1 answered 'authenticate' without a clientID.");

        logger.LogInformation(
            "Signed in to Soft1 as {Username}: company {Company}, branch {Branch}, module {Module}, refid {RefId}.",
            Options.Username,
            target.Company,
            target.Branch,
            target.Module,
            target.RefId);

        return new Soft1SessionInfo(clientId, target.Company, target.Branch);
    }

    private async Task<(string ClientId, IReadOnlyList<Soft1LoginTarget> Targets)> LoginAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        JsonElement answer;
        try
        {
            answer = await requestGate.RunAsync(
                async ct =>
                {
                    using var document = await transport.PostAsync(
                        Options.ServiceUrl,
                        new Dictionary<string, object?>
                        {
                            ["service"] = "login",
                            ["username"] = Options.Username,
                            ["password"] = Options.Password,
                            ["appId"] = Options.AppId
                        },
                        ct).ConfigureAwait(false);
                    return document.RootElement.Clone();
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Soft1ServiceException exception)
        {
            throw Soft1Errors.Translate(exception);
        }

        var clientId = ReadString(answer, "clientID")
            ?? throw new Soft1AuthenticationException("Soft1 answered 'login' without a clientID.");

        var targets = new List<Soft1LoginTarget>();
        if (answer.TryGetProperty("objs", out var objs) && objs.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in objs.EnumerateArray())
            {
                targets.Add(new Soft1LoginTarget(
                    ReadString(entry, "COMPANY"),
                    ReadString(entry, "COMPANYNAME"),
                    ReadString(entry, "BRANCH"),
                    ReadString(entry, "BRANCHNAME"),
                    ReadString(entry, "MODULE"),
                    ReadString(entry, "MODULENAME"),
                    ReadString(entry, "REFID"),
                    ReadString(entry, "REFIDNAME")));
            }
        }

        return (clientId, targets);
    }

    /// <summary>
    /// The configured target, or the only one the account has. The configured values act as a filter, so
    /// setting only <c>Soft1:Company</c> is enough when that company has one branch.
    /// </summary>
    private Soft1LoginTarget PickTarget(IReadOnlyList<Soft1LoginTarget> targets)
    {
        var configured = new Soft1LoginTarget(
            Blank(Options.Company), null, Blank(Options.Branch), null, Blank(Options.Module), null, Blank(Options.RefId), null);

        // All four given: authenticate as told, whether or not the login answer listed it.
        if (configured is { Company: not null, Branch: not null, Module: not null, RefId: not null })
        {
            return configured;
        }

        var matching = targets
            .Where(target => Matches(configured.Company, target.Company)
                && Matches(configured.Branch, target.Branch)
                && Matches(configured.Module, target.Module)
                && Matches(configured.RefId, target.RefId))
            .ToList();

        return matching.Count switch
        {
            1 => matching[0],
            0 => throw new Soft1AuthenticationException(
                "None of the companies this Soft1 web account may use matches Soft1:Company / Branch / Module / RefId. " +
                $"Available: {Describe(targets)}"),
            _ => throw new Soft1AuthenticationException(
                "This Soft1 web account may use several companies/branches; set Soft1:Company, Soft1:Branch, " +
                $"Soft1:Module and Soft1:RefId to one of: {Describe(matching)}")
        };
    }

    private static bool Matches(string? configured, string? actual) =>
        configured is null || string.Equals(configured, actual?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Describe(IReadOnlyList<Soft1LoginTarget> targets)
    {
        if (targets.Count == 0)
        {
            return "none listed";
        }

        var builder = new StringBuilder();
        foreach (var target in targets.Take(20))
        {
            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(CultureInfo.InvariantCulture,
                $"COMPANY={target.Company} ({target.CompanyName}), BRANCH={target.Branch} ({target.BranchName}), " +
                $"MODULE={target.Module} ({target.ModuleName}), REFID={target.RefId} ({target.RefIdName})");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ReadFieldNames(JsonElement info)
    {
        var names = new List<string>();

        if (info.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fields.EnumerateArray())
            {
                names.Add(ReadString(field, "name") ?? string.Empty);
            }
        }

        return names;
    }

    private static IEnumerable<string?[]> ReadRows(JsonElement answer)
    {
        if (!answer.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var values = new string?[row.GetArrayLength()];
            var index = 0;

            foreach (var value in row.EnumerateArray())
            {
                values[index++] = Soft1Values.Text(value);
            }

            yield return values;
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? Soft1Values.Text(value)
            : null;
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        return int.TryParse(ReadString(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Fails with the name of the missing setting rather than letting a request reach Soft1 and come back as an
    /// error that names nothing.
    /// </summary>
    private void ValidateOptions()
    {
        HttpsUriValidator.RequireHttpsOrLoopback(Options.ServiceUrl, "Soft1:ServiceUrl");

        if (string.IsNullOrWhiteSpace(Options.Username) || string.IsNullOrEmpty(Options.Password))
        {
            throw new Soft1AuthenticationException("Soft1:Username / Soft1:Password are not configured.");
        }

        if (string.IsNullOrWhiteSpace(Options.AppId))
        {
            throw new Soft1AuthenticationException("Soft1:AppId is not configured; Soft1 requires the web account's application id.");
        }
    }
}
