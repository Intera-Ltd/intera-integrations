using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Jira.Api.Clients;
using Intera.Jira.Api.Infrastructure;
using Intera.Jira.Api.Models;
using Intera.Jira.Api.Options;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Intera.Jira.Api.Services;

using Row = Dictionary<string, object?>;
using Rows = IReadOnlyList<IReadOnlyDictionary<string, object?>>;

/// <summary>One custom field as <c>GET /api/Schema/custom-fields</c> reports it, for the plugin's field list.</summary>
/// <param name="Name">The column: Jira's field id, <c>customfield_10037</c>.</param>
/// <param name="Label">The field's name in Jira.</param>
public sealed record JiraCustomFieldDescriptor(string Name, string Label, JiraFieldType Type, string? CustomType);

public interface IJiraDataService
{
    /// <summary>
    /// The rows of one data source for the site the request names, narrowed by what Jira can narrow and by the
    /// instance's JQL scope.
    /// </summary>
    Task<Rows> LoadAsync(JiraSource source, QueryRequest? query, string? site, string? instanceScope, CancellationToken cancellationToken);

    /// <summary>The custom fields <c>issues</c> carries on a passthrough read — what the plugin appends to its fields.</summary>
    Task<IReadOnlyList<JiraCustomFieldDescriptor>> GetIssueCustomFieldsAsync(string? site, CancellationToken cancellationToken);
}

/// <summary>
/// Builds the rows of every data source out of the Jira Cloud platform and Agile REST APIs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written against Atlassian's OpenAPI specifications (platform v3 and Agile 1.0, read 2026-10-07) without access to
/// a live site</b> — the project's CLAUDE.md lists what that leaves to confirm.
/// </para>
/// <para>
/// Every row carries the five metadata fields (<see cref="JiraSources.Metadata"/>); <c>intera_synced_at</c> is when
/// the answer the row came from was fetched, which a cached answer keeps.
/// </para>
/// </remarks>
public sealed partial class JiraDataService(
    JiraClient client,
    IOptionsMonitor<JiraOptions> options,
    ILogger<JiraDataService> logger) : IJiraDataService
{
    /// <summary>The Jira Software sprint field's custom type — its id differs per site.</summary>
    internal const string SprintFieldType = "com.pyxis.greenhopper.jira:gh-sprint";

    private JiraOptions Options => options.CurrentValue;

    public async Task<Rows> LoadAsync(JiraSource source, QueryRequest? query, string? site, string? instanceScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var scopes = new List<string>(2);
        if (Jql.ValidateScope(Options.JqlScope, "Jira:JqlScope") is { } deploymentScope)
        {
            scopes.Add(deploymentScope);
        }

        if (Jql.ValidateScope(instanceScope, "the integration instance") is { } ownScope)
        {
            scopes.Add(ownScope);
        }

        var resolved = await client.ResolveSiteAsync(site, cancellationToken).ConfigureAwait(false);
        var context = new ReadContext(resolved, query, source, QueryFieldReferences.Of(query), scopes);

        IReadOnlyList<Row> rows = source.Id switch
        {
            JiraSources.Projects => await LoadProjectsAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.Users => await LoadUsersAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.Fields => await LoadFieldsAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.IssueTypes => await LoadIssueTypesAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.Statuses => await LoadStatusesAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.Priorities => await LoadPrioritiesAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.Versions => await LoadVersionsAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.Components => await LoadComponentsAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.CustomFieldOptions => await LoadCustomFieldOptionsAsync(context, cancellationToken).ConfigureAwait(false),

            JiraSources.Issues => await LoadIssuesAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.IssueLabels or JiraSources.IssueComponents or JiraSources.IssueVersions or JiraSources.IssueSprints =>
                await LoadIssueValuesAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.IssueLinks => await LoadIssueLinksAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.IssueChangelog => await LoadChangelogAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.IssueWorklogs => await LoadWorklogsAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.Comments => await LoadCommentsAsync(context, cancellationToken).ConfigureAwait(false),

            JiraSources.Boards => await LoadBoardsAsync(context, cancellationToken).ConfigureAwait(false),
            JiraSources.Sprints => await LoadSprintsAsync(context, cancellationToken).ConfigureAwait(false),

            _ => throw new ArgumentException($"Unknown Jira data source '{source.Id}'.")
        };

        return [.. rows.Select(row => JiraSources.Project(source, row))];
    }

    public async Task<IReadOnlyList<JiraCustomFieldDescriptor>> GetIssueCustomFieldsAsync(string? site, CancellationToken cancellationToken)
    {
        var resolved = await client.ResolveSiteAsync(site, cancellationToken).ConfigureAwait(false);
        var catalog = await ReadFieldCatalogAsync(resolved, cancellationToken).ConfigureAwait(false);

        return
        [
            .. ExposedCustomFields(catalog).Select(static field =>
                new JiraCustomFieldDescriptor(field.Id.ToLowerInvariant(), field.Name ?? field.Id, CustomFieldType(field), field.CustomType))
        ];
    }

    /// <param name="Scopes">The deployment's and the instance's JQL scopes, already validated.</param>
    private sealed record ReadContext(
        JiraSite Site,
        QueryRequest? Query,
        JiraSource Source,
        QueryFieldReferences References,
        IReadOnlyList<string> Scopes);

    /// <summary>One field of the site, as <c>GET /rest/api/3/field</c> describes it.</summary>
    internal sealed record JiraFieldInfo(
        string Id,
        string? Key,
        string? Name,
        bool Custom,
        string? SchemaType,
        string? ItemsType,
        string? CustomType,
        string? CustomId,
        string? System,
        bool? Searchable,
        bool? Navigable,
        bool? Orderable,
        IReadOnlyList<string> ClauseNames);

    private sealed record FieldCatalog(IReadOnlyList<JiraFieldInfo> Fields, DateTime FetchedAt)
    {
        public JiraFieldInfo? SprintField { get; } = Fields.FirstOrDefault(static field =>
            string.Equals(field.CustomType, SprintFieldType, StringComparison.Ordinal));

        public JiraFieldInfo? Find(string id) =>
            Fields.FirstOrDefault(field => string.Equals(field.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    // ---- Shared reads ---------------------------------------------------------------------------------------

    private Task<FieldCatalog> ReadFieldCatalogAsync(JiraSite site, CancellationToken cancellationToken) =>
        client.CachedAsync(site, "field-catalog", reference: true, async ct =>
        {
            var response = await client.GetAsync(site, "rest/api/3/field", null, reference: true, ct).ConfigureAwait(false);
            var fields = new List<JiraFieldInfo>();

            foreach (var field in response.Root.ValueKind == JsonValueKind.Array ? response.Root.EnumerateArray().ToList() : [])
            {
                if (JiraJson.String(field, "id") is not { } id)
                {
                    continue;
                }

                var schema = JiraJson.Object(field, "schema");
                fields.Add(new JiraFieldInfo(
                    id,
                    JiraJson.String(field, "key"),
                    JiraJson.String(field, "name"),
                    JiraJson.Boolean(field, "custom") ?? false,
                    JiraJson.String(schema, "type"),
                    JiraJson.String(schema, "items"),
                    JiraJson.String(schema, "custom"),
                    JiraJson.String(schema, "customId"),
                    JiraJson.String(schema, "system"),
                    JiraJson.Boolean(field, "searchable"),
                    JiraJson.Boolean(field, "navigable"),
                    JiraJson.Boolean(field, "orderable"),
                    [.. JiraJson.Array(field, "clauseNames").Select(JiraJson.Text).OfType<string>()]));
            }

            return new FieldCatalog(fields, response.FetchedAt);
        }, cancellationToken);

    /// <summary>
    /// The custom fields a passthrough read of <c>issues</c> carries: those <c>Jira:IssueCustomFields</c> names (ids or
    /// names), every one for <c>*</c>, none when it is empty. The sprint field is left out — <c>sprint_ids</c> already
    /// carries it.
    /// </summary>
    private IEnumerable<JiraFieldInfo> ExposedCustomFields(FieldCatalog catalog)
    {
        var wanted = Options.IssueCustomFields
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ExposedCustomFields(catalog.Fields, wanted);
    }

    internal static IEnumerable<JiraFieldInfo> ExposedCustomFields(IEnumerable<JiraFieldInfo> fields, IReadOnlySet<string> wanted)
    {
        var all = wanted.Contains("*");

        return fields.Where(field =>
            field.Custom
            && JiraSources.IsCustomFieldColumn(field.Id)
            && !string.Equals(field.CustomType, SprintFieldType, StringComparison.Ordinal)
            && (all || wanted.Contains(field.Id) || (field.Name is { } name && wanted.Contains(name))));
    }

    /// <summary>
    /// How a custom field's value is typed as a column. A number, a date and a date-time keep their type; everything
    /// else (text, a select list, a user, a list of anything) is text — comparing a number as text misorders, but
    /// reading text as a number would drop every row that is not one, and only the second loses data.
    /// </summary>
    internal static JiraFieldType CustomFieldType(JiraFieldInfo field) => field.SchemaType switch
    {
        "number" => JiraFieldType.Number,
        "date" or "datetime" => JiraFieldType.Date,
        _ => JiraFieldType.Text
    };

    /// <summary>Reads a custom field's value the way <see cref="CustomFieldType"/> types it.</summary>
    internal static object? ReadCustomValue(JiraFieldInfo field, JsonElement? value)
    {
        if (value is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        switch (field.SchemaType)
        {
            case "number":
                return element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number) ? number : null;
            case "date":
                return JiraJson.ParseDate(JiraJson.Text(element));
            case "datetime":
                return JiraJson.ParseTimestamp(JiraJson.Text(element));
        }

        return element.ValueKind == JsonValueKind.Array
            ? JiraJson.Join(element.EnumerateArray().Select(ReadableText))
            : ReadableText(element);
    }

    /// <summary>
    /// What a person reads in a value: a select option its <c>value</c> (a cascading one <c>parent / child</c>), a user
    /// its account id, a version or component its name, a rich-text document its words, a scalar its text.
    /// </summary>
    internal static string? ReadableText(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (string.Equals(JiraJson.String(element, "type"), "doc", StringComparison.Ordinal))
                {
                    return AdfText.ToText(element);
                }

                if (JiraJson.String(element, "accountId") is { } accountId)
                {
                    return accountId;
                }

                if (JiraJson.String(element, "value") is { } optionValue)
                {
                    return JiraJson.Object(element, "child") is { } child && JiraJson.String(child, "value") is { } childValue
                        ? $"{optionValue} / {childValue}"
                        : optionValue;
                }

                return JiraJson.String(element, "name")
                    ?? JiraJson.String(element, "displayName")
                    ?? JiraJson.String(element, "key")
                    ?? JiraJson.String(element, "id");

            case JsonValueKind.Array:
                return JiraJson.Join(element.EnumerateArray().Select(ReadableText));

            default:
                return JiraJson.Text(element);
        }
    }

    // ---- Helpers --------------------------------------------------------------------------------------------

    /// <summary>Adds the five metadata fields every source ends with.</summary>
    private static Row Stamp(ReadContext context, Row row, DateTime? updatedAt, DateTime fetchedAt)
    {
        row[JiraSources.CloudIdField] = context.Site.Id;
        row[JiraSources.SiteUrlField] = context.Site.Url;
        row[JiraSources.SourceSystemField] = "jira";
        row[JiraSources.SourceUpdatedAtField] = updatedAt;
        row[JiraSources.SyncedAtField] = fetchedAt;
        return row;
    }

    /// <summary>The bounds the filter's top-level AND chain puts on <paramref name="field"/>.</summary>
    private static (DateOnly? From, DateOnly? To) Window(QueryRequest? query, string field)
    {
        var pushdown = QueryPushdownExtractor.Extract(query, field, []);
        return (pushdown.From, pushdown.To);
    }

    /// <summary>The values the filter names for any of <paramref name="fields"/> through = / IN, or empty.</summary>
    private static List<string> Keys(QueryRequest? query, params string[] fields)
    {
        var pushdown = QueryPushdownExtractor.Extract(query, null, fields);
        return [.. fields.SelectMany(pushdown.Values).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Whether the filter names <paramref name="field"/> through = / IN at all.</summary>
    private static bool Names(QueryRequest? query, string field) =>
        QueryPushdownExtractor.Extract(query, null, [field]).Keys.ContainsKey(field);

    private static KeyValuePair<string, string> Param(string name, string value) => new(name, value);

    /// <summary>Runs <paramref name="load"/> for every item, at most <c>MaxParallelRequests</c> at a time (the client's gate).</summary>
    private static async Task<List<T>> FanOutAsync<TItem, T>(IEnumerable<TItem> items, Func<TItem, Task<T>> load)
    {
        var tasks = items.Select(load).ToList();
        return [.. await Task.WhenAll(tasks).ConfigureAwait(false)];
    }

    private void RequireFanout(int count, string what)
    {
        if (count > Options.MaxFanout)
        {
            throw new JiraRequestException(
                $"This read would make one call for each of {count} {what} (Jira:MaxFanout is {Options.MaxFanout}). Narrow the filter.");
        }
    }
}
