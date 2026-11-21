using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Zendesk.Api.Clients;
using Intera.Zendesk.Api.Infrastructure;
using Intera.Zendesk.Api.Models;
using Intera.Zendesk.Api.Options;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace Intera.Zendesk.Api.Services;

using Row = Dictionary<string, object?>;
using Rows = IReadOnlyList<IReadOnlyDictionary<string, object?>>;

/// <summary>One ticket custom field as <c>GET /api/Schema/custom-fields</c> reports it, for the plugin's field list.</summary>
/// <param name="Name">The column: <c>custom_field_</c> and Zendesk's field id.</param>
/// <param name="Label">The field's title in Zendesk.</param>
public sealed record ZendeskCustomFieldDescriptor(string Name, string Label, ZendeskFieldType Type, string? CustomType);

public interface IZendeskDataService
{
    /// <summary>The rows of one data source, narrowed by what Zendesk can narrow.</summary>
    Task<Rows> LoadAsync(ZendeskSource source, QueryRequest? query, CancellationToken cancellationToken);

    /// <summary>The ticket custom fields a whole-row read of <c>tickets</c> carries — what the plugin appends to its fields.</summary>
    Task<IReadOnlyList<ZendeskCustomFieldDescriptor>> GetTicketCustomFieldsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Builds the rows of every data source out of the Zendesk Support REST API.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written against Zendesk's published API reference without access to a Zendesk account</b> — the project's
/// CLAUDE.md lists what that leaves to confirm.
/// </para>
/// <para>
/// Every row carries the four metadata fields (<see cref="ZendeskSources.Metadata"/>); <c>intera_synced_at</c> is when
/// the answer the row came from was fetched, which a cached answer keeps.
/// </para>
/// </remarks>
public sealed partial class ZendeskDataService(
    ZendeskClient client,
    IOptionsMonitor<ZendeskOptions> options) : IZendeskDataService
{
    private static readonly string[] MetricStems = ["reply_time", "first_resolution", "full_resolution", "requester_wait", "agent_wait", "on_hold"];

    private ZendeskOptions Options => options.CurrentValue;

    private int PageSize => Math.Clamp(Options.PageSize, 1, 100);

    public async Task<Rows> LoadAsync(ZendeskSource source, QueryRequest? query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var context = new ReadContext(query, source, QueryFieldReferences.Of(query), Account());

        IReadOnlyList<Row> rows = source.Id switch
        {
            ZendeskSources.Tickets => await LoadTicketsAsync(context, cancellationToken).ConfigureAwait(false),
            ZendeskSources.TicketMetrics => await LoadMetricsAsync(context, cancellationToken).ConfigureAwait(false),
            ZendeskSources.TicketMetricEvents => await LoadMetricEventsAsync(context, cancellationToken).ConfigureAwait(false),
            ZendeskSources.TicketComments => await LoadCommentsAsync(context, cancellationToken).ConfigureAwait(false),
            ZendeskSources.Users => await LoadUsersAsync(context, cancellationToken).ConfigureAwait(false),
            ZendeskSources.Organizations => await LoadOrganizationsAsync(context, cancellationToken).ConfigureAwait(false),
            ZendeskSources.Groups => await LoadGroupsAsync(context, cancellationToken).ConfigureAwait(false),
            ZendeskSources.GroupMemberships => await LoadGroupMembershipsAsync(context, cancellationToken).ConfigureAwait(false),
            ZendeskSources.SatisfactionRatings => await LoadSatisfactionRatingsAsync(context, cancellationToken).ConfigureAwait(false),
            ZendeskSources.SlaPolicies => await LoadSlaPoliciesAsync(context, cancellationToken).ConfigureAwait(false),
            ZendeskSources.TicketFields => await LoadTicketFieldsAsync(context, cancellationToken).ConfigureAwait(false),
            ZendeskSources.TicketForms => await LoadTicketFormsAsync(context, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException($"Unknown Zendesk data source '{source.Id}'.")
        };

        return [.. rows.Select(row => ZendeskSources.Project(source, row))];
    }

    public async Task<IReadOnlyList<ZendeskCustomFieldDescriptor>> GetTicketCustomFieldsAsync(CancellationToken cancellationToken)
    {
        var catalog = await ReadFieldCatalogAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. ExposedCustomFields(catalog.Fields, WantedCustomFields(Options.TicketCustomFields))
                .Select(static field => new ZendeskCustomFieldDescriptor(
                    ZendeskSources.CustomFieldPrefix + field.Id, field.Title ?? field.Id, CustomFieldType(field), field.Type))
        ];
    }

    /// <param name="Account">What identifies this account in <c>zendesk_subdomain</c>.</param>
    private sealed record ReadContext(QueryRequest? Query, ZendeskSource Source, QueryFieldReferences References, string Account);

    /// <summary>One ticket field of the account, as <c>GET /api/v2/ticket_fields</c> describes it.</summary>
    internal sealed record ZendeskFieldInfo(string Id, string? Title, string? Type, bool Custom, bool Active);

    private sealed record FieldCatalog(IReadOnlyList<ZendeskFieldInfo> Fields, DateTime FetchedAt)
    {
        public ZendeskFieldInfo? Find(string id) => Fields.FirstOrDefault(field => string.Equals(field.Id, id, StringComparison.Ordinal));
    }

    private string Account() =>
        string.IsNullOrWhiteSpace(Options.Subdomain) ? client.BaseUri.Host : Options.Subdomain.Trim().ToLowerInvariant();

    // ---- Tickets --------------------------------------------------------------------------------------------

    /// <summary>
    /// The tickets a query needs. Ids named by the filter go to <c>show_many</c>; otherwise the incremental export
    /// (when enabled) is read from the earliest time the filter allows, else the Search export for a window on
    /// <c>created_at</c> / <c>updated_at</c>, else the whole list. Every path returns a superset — the DSL runs after.
    /// </summary>
    private async Task<ZendeskCollection> ReadTicketsAsync(
        QueryRequest? query,
        DateOnly? updatedFrom,
        bool withMetrics,
        CancellationToken cancellationToken)
    {
        var ids = NumericIds(Keys(query, "ticket_id"));
        if (ids.Count > 0 && !withMetrics)
        {
            return await ShowManyAsync("api/v2/tickets/show_many", "tickets", ids, cancellationToken).ConfigureAwait(false);
        }

        var (createdFrom, createdTo) = Window(query, "created_at");
        var (updatedLower, updatedTo) = Window(query, "updated_at");
        var lower = Latest(createdFrom, updatedLower, updatedFrom);

        if (Options.UseIncrementalExport)
        {
            var parameters = new List<KeyValuePair<string, string>> { Param("start_time", StartTime(lower).ToString(CultureInfo.InvariantCulture)) };
            if (withMetrics)
            {
                parameters.Add(Param("include", "metric_sets"));
            }

            return await client.GetStreamAsync(
                "api/v2/incremental/tickets/cursor", parameters, "tickets", withMetrics ? "metric_sets" : null, null, null, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!withMetrics && (lower is not null || createdTo is not null || updatedTo is not null))
        {
            var text = ZendeskSearch.TicketQuery(createdFrom, createdTo, Latest(updatedLower, updatedFrom), updatedTo);

            return await client.GetPagedAsync(
                "api/v2/search/export",
                [Param("query", text), Param("filter[type]", "ticket")],
                "results",
                1000,
                reference: false,
                cancellationToken).ConfigureAwait(false);
        }

        return await client.GetPagedAsync("api/v2/tickets", null, "tickets", PageSize, reference: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<Row>> LoadTicketsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var tickets = await ReadTicketsAsync(context.Query, null, withMetrics: false, cancellationToken).ConfigureAwait(false);

        var exposed = await ExposedForQueryAsync(context, cancellationToken).ConfigureAwait(false);
        var baseUrl = client.BaseUri.AbsoluteUri;

        return
        [
            .. tickets.Items
                .Where(static t => !string.Equals(ZendeskJson.String(t, "status"), "deleted", StringComparison.OrdinalIgnoreCase))
                .Select(t =>
                {
                    var id = ZendeskJson.String(t, "id");
                    var row = new Row
                    {
                        ["ticket_id"] = id,
                        ["external_id"] = ZendeskJson.String(t, "external_id"),
                        ["subject"] = ZendeskJson.String(t, "subject") ?? ZendeskJson.String(t, "raw_subject"),
                        ["description"] = ZendeskJson.String(t, "description"),
                        ["status"] = ZendeskJson.String(t, "status"),
                        ["custom_status_id"] = ZendeskJson.String(t, "custom_status_id"),
                        ["priority"] = ZendeskJson.String(t, "priority"),
                        ["type"] = ZendeskJson.String(t, "type"),
                        ["requester_id"] = ZendeskJson.String(t, "requester_id"),
                        ["submitter_id"] = ZendeskJson.String(t, "submitter_id"),
                        ["assignee_id"] = ZendeskJson.String(t, "assignee_id"),
                        ["group_id"] = ZendeskJson.String(t, "group_id"),
                        ["organization_id"] = ZendeskJson.String(t, "organization_id"),
                        ["problem_id"] = ZendeskJson.String(t, "problem_id"),
                        ["ticket_form_id"] = ZendeskJson.String(t, "ticket_form_id"),
                        ["brand_id"] = ZendeskJson.String(t, "brand_id"),
                        ["recipient"] = ZendeskJson.String(t, "recipient"),
                        ["tags"] = ZendeskJson.JoinArray(t, "tags"),
                        ["via_channel"] = ZendeskJson.String(ZendeskJson.Object(t, "via"), "channel"),
                        ["satisfaction_score"] = ZendeskJson.String(ZendeskJson.Object(t, "satisfaction_rating"), "score"),
                        ["created_at"] = ZendeskJson.Timestamp(t, "created_at"),
                        ["updated_at"] = ZendeskJson.Timestamp(t, "updated_at"),
                        ["due_at"] = ZendeskJson.Timestamp(t, "due_at"),
                        ["has_incidents"] = ZendeskJson.Boolean(t, "has_incidents"),
                        ["is_public"] = ZendeskJson.Boolean(t, "is_public"),
                        ["url"] = id is null ? null : $"{baseUrl}agent/tickets/{id}"
                    };

                    foreach (var field in ZendeskJson.Array(t, "custom_fields"))
                    {
                        if (ZendeskJson.String(field, "id") is { } fieldId && exposed.TryGetValue(fieldId, out var info))
                        {
                            row[ZendeskSources.CustomFieldPrefix + fieldId] = ReadCustomValue(info, ZendeskJson.Property(field, "value"));
                        }
                    }

                    return Stamp(context, row, ZendeskJson.Timestamp(t, "updated_at"), tickets.FetchedAt);
                })
        ];
    }

    // ---- Ticket custom fields -------------------------------------------------------------------------------

    private Task<FieldCatalog> ReadFieldCatalogAsync(CancellationToken cancellationToken) =>
        client.CachedAsync("field-catalog", reference: true, async ct =>
        {
            var fields = await client.GetPagedAsync("api/v2/ticket_fields", null, "ticket_fields", PageSize, reference: true, ct).ConfigureAwait(false);

            return new FieldCatalog(
                [
                    .. fields.Items
                        .Select(static field => (Id: ZendeskJson.String(field, "id"), Field: field))
                        .Where(static pair => pair.Id is not null)
                        .Select(static pair => new ZendeskFieldInfo(
                            pair.Id!,
                            ZendeskJson.String(pair.Field, "title") ?? ZendeskJson.String(pair.Field, "raw_title"),
                            ZendeskJson.String(pair.Field, "type"),
                            ZendeskJson.Boolean(pair.Field, "removable") == true,
                            ZendeskJson.Boolean(pair.Field, "active") ?? true))
                ],
                fields.FetchedAt);
        }, cancellationToken);

    /// <summary>
    /// The custom fields a read carries, by field id: for a whole-row read those <c>Zendesk:TicketCustomFields</c> names
    /// (ids or titles; every one for <c>*</c>; none when empty), for any other query exactly the
    /// <c>custom_field_NNN</c> columns it references. The catalog is read only when one of them is wanted.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, ZendeskFieldInfo>> ExposedForQueryAsync(ReadContext context, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<string> referenced = context.References.IsPassthrough
            ? []
            : [.. context.References.Fields.Where(ZendeskSources.IsCustomFieldColumn).Select(static name => name[ZendeskSources.CustomFieldPrefix.Length..])];
        var wanted = context.References.IsPassthrough ? WantedCustomFields(Options.TicketCustomFields) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (referenced.Count == 0 && wanted.Count == 0)
        {
            return new Dictionary<string, ZendeskFieldInfo>();
        }

        var catalog = await ReadFieldCatalogAsync(cancellationToken).ConfigureAwait(false);
        var result = ExposedCustomFields(catalog.Fields, wanted).ToDictionary(static field => field.Id, StringComparer.Ordinal);

        foreach (var id in referenced)
        {
            // A referenced column the catalog does not know still reads as text: the field may be inactive or newly created.
            result[id] = catalog.Find(id) ?? new ZendeskFieldInfo(id, null, null, true, true);
        }

        return result;
    }

    internal static HashSet<string> WantedCustomFields(string? setting) =>
        (setting ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static IEnumerable<ZendeskFieldInfo> ExposedCustomFields(IEnumerable<ZendeskFieldInfo> fields, IReadOnlySet<string> wanted)
    {
        var all = wanted.Contains("*");

        return fields.Where(field =>
            field.Custom
            && field.Active
            && (all || wanted.Contains(field.Id) || (field.Title is { } title && wanted.Contains(title))));
    }

    /// <summary>
    /// How a custom field's value is typed as a column. A number, a date and a checkbox keep their type; everything
    /// else (text, a drop-down, a multi-select, a lookup) is text — reading text as a number would drop every row
    /// that is not one, and only the second loses data.
    /// </summary>
    internal static ZendeskFieldType CustomFieldType(ZendeskFieldInfo field) => field.Type switch
    {
        "integer" or "decimal" => ZendeskFieldType.Number,
        "date" => ZendeskFieldType.Date,
        "checkbox" => ZendeskFieldType.Flag,
        _ => ZendeskFieldType.Text
    };

    internal static object? ReadCustomValue(ZendeskFieldInfo field, JsonElement? value)
    {
        if (value is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        switch (CustomFieldType(field))
        {
            case ZendeskFieldType.Number:
                return ZendeskJson.ToDecimal(element);
            case ZendeskFieldType.Date:
                return ZendeskJson.ParseDate(ZendeskJson.Text(element));
            case ZendeskFieldType.Flag:
                return ZendeskJson.ToBoolean(element);
        }

        return element.ValueKind == JsonValueKind.Array
            ? ZendeskJson.Join(element.EnumerateArray().Select(ZendeskJson.Text))
            : ZendeskJson.Text(element);
    }

    // ---- Ticket metrics -------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadMetricsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var ids = NumericIds(Keys(context.Query, "ticket_id"));
        IReadOnlyList<JsonElement> metrics;
        DateTime fetchedAt;

        if (ids.Count > 0)
        {
            RequireFanout(ids.Count, "tickets");
            var responses = await FanOutAsync(ids, id => client.TryGetAsync($"api/v2/tickets/{id}/metrics", null, cancellationToken)).ConfigureAwait(false);
            var present = responses.OfType<ZendeskResponse>().ToList();

            metrics = [.. present.Select(static response => ZendeskJson.Object(response.Root, "ticket_metric")).OfType<JsonElement>()];
            fetchedAt = present.Count > 0 ? present.Min(static response => response.FetchedAt) : DateTime.UtcNow;
        }
        else if (Options.UseIncrementalExport && Latest(Window(context.Query, "created_at").From, Window(context.Query, "solved_at").From) is { } lower)
        {
            // A ticket created or solved on or after T was updated on or after T, so the export from T holds its metric set.
            var tickets = await ReadTicketsAsync(null, lower, withMetrics: true, cancellationToken).ConfigureAwait(false);
            metrics = tickets.Extra;
            fetchedAt = tickets.FetchedAt;
        }
        else
        {
            var all = await client.GetPagedAsync("api/v2/ticket_metrics", null, "ticket_metrics", PageSize, reference: false, cancellationToken).ConfigureAwait(false);
            metrics = all.Items;
            fetchedAt = all.FetchedAt;
        }

        return
        [
            .. metrics.Select(m =>
            {
                var row = new Row
                {
                    ["metric_id"] = ZendeskJson.String(m, "id"),
                    ["ticket_id"] = ZendeskJson.String(m, "ticket_id"),
                    ["created_at"] = ZendeskJson.Timestamp(m, "created_at"),
                    ["updated_at"] = ZendeskJson.Timestamp(m, "updated_at"),
                    ["assigned_at"] = ZendeskJson.Timestamp(m, "assigned_at"),
                    ["initially_assigned_at"] = ZendeskJson.Timestamp(m, "initially_assigned_at"),
                    ["solved_at"] = ZendeskJson.Timestamp(m, "solved_at"),
                    ["status_updated_at"] = ZendeskJson.Timestamp(m, "status_updated_at"),
                    ["assignee_updated_at"] = ZendeskJson.Timestamp(m, "assignee_updated_at"),
                    ["requester_updated_at"] = ZendeskJson.Timestamp(m, "requester_updated_at"),
                    ["latest_comment_added_at"] = ZendeskJson.Timestamp(m, "latest_comment_added_at"),
                    ["assignee_stations"] = ZendeskJson.Decimal(m, "assignee_stations"),
                    ["group_stations"] = ZendeskJson.Decimal(m, "group_stations"),
                    ["reopens"] = ZendeskJson.Decimal(m, "reopens"),
                    ["replies"] = ZendeskJson.Decimal(m, "replies")
                };

                foreach (var stem in MetricStems)
                {
                    var property = stem == "reply_time" ? "reply_time_in_minutes" : $"{stem}_time_in_minutes";
                    var pair = ZendeskJson.Object(m, property);
                    row[$"{stem}_business_minutes"] = ZendeskJson.Decimal(pair, "business");
                    row[$"{stem}_calendar_minutes"] = ZendeskJson.Decimal(pair, "calendar");
                }

                return Stamp(context, row, ZendeskJson.Timestamp(m, "updated_at"), fetchedAt);
            })
        ];
    }

    // ---- Ticket metric events -------------------------------------------------------------------------------

    private async Task<List<Row>> LoadMetricEventsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var ids = NumericIds(Keys(context.Query, "ticket_id"));
        IReadOnlyList<JsonElement> events;
        DateTime fetchedAt;

        if (ids.Count > 0)
        {
            RequireFanout(ids.Count, "tickets");
            // One call a ticket, and not a list: the answer is an object with an array per metric
            // ({"agent_work_time":[…],"reply_time":[…]}), unpaged — verified on a live account 2026-10-14.
            var responses = await FanOutAsync(ids, id => client.TryGetAsync($"api/v2/tickets/{id}/metric_events", null, cancellationToken)).ConfigureAwait(false);
            var present = responses.OfType<ZendeskResponse>().ToList();

            events = [.. present.SelectMany(static response => response.Root.ValueKind == JsonValueKind.Object
                ? response.Root.EnumerateObject().Where(static property => property.Value.ValueKind == JsonValueKind.Array).SelectMany(static property => property.Value.EnumerateArray())
                : [])];
            fetchedAt = present.Count > 0 ? present.Min(static response => response.FetchedAt) : DateTime.UtcNow;
        }
        else
        {
            var (from, to) = Window(context.Query, "time");
            var until = to is { } day ? day.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : (DateTime?)null;

            var stream = await client.GetStreamAsync(
                "api/v2/incremental/ticket_metric_events",
                [Param("start_time", StartTime(from).ToString(CultureInfo.InvariantCulture))],
                "ticket_metric_events",
                null,
                until?.Ticks.ToString(CultureInfo.InvariantCulture),
                until is { } limit ? page => page.Count > 0 && ZendeskJson.Timestamp(page[^1], "time") > limit : null,
                cancellationToken).ConfigureAwait(false);

            events = stream.Items;
            fetchedAt = stream.FetchedAt;
        }

        return
        [
            .. events.Select(e =>
            {
                var sla = ZendeskJson.Object(e, "sla");
                var policy = ZendeskJson.Object(sla, "policy");
                var status = ZendeskJson.Object(e, "status");

                return Stamp(context, new Row
                {
                    ["event_id"] = ZendeskJson.String(e, "id"),
                    ["ticket_id"] = ZendeskJson.String(e, "ticket_id"),
                    ["metric"] = ZendeskJson.String(e, "metric"),
                    ["type"] = ZendeskJson.String(e, "type"),
                    ["instance_id"] = ZendeskJson.String(e, "instance_id"),
                    ["time"] = ZendeskJson.Timestamp(e, "time"),
                    ["deleted"] = ZendeskJson.Boolean(e, "deleted") ?? false,
                    ["sla_policy_id"] = ZendeskJson.String(policy, "id"),
                    ["sla_policy_title"] = ZendeskJson.String(policy, "title"),
                    ["sla_target_minutes"] = ZendeskJson.Decimal(sla, "target"),
                    ["sla_business_hours"] = ZendeskJson.Boolean(sla, "business_hours"),
                    ["status_calendar_minutes"] = ZendeskJson.Decimal(status, "calendar"),
                    ["status_business_minutes"] = ZendeskJson.Decimal(status, "business")
                }, ZendeskJson.Timestamp(e, "time"), fetchedAt);
            })
        ];
    }

    // ---- Ticket comments ------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadCommentsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var ids = NumericIds(Keys(context.Query, "ticket_id"));

        if (ids.Count == 0)
        {
            var (createdFrom, _) = Window(context.Query, "created_at");
            if (createdFrom is null)
            {
                throw new ZendeskRequestException(
                    "ticket_comments has no account-wide list (Zendesk serves comments one ticket at a time): filter on ticket_id (= / IN) or give created_at a lower bound, so the tickets updated since can be read.");
            }

            // A comment created on or after T makes its ticket updated on or after T.
            var tickets = await ReadTicketsAsync(null, createdFrom, withMetrics: false, cancellationToken).ConfigureAwait(false);
            ids = [.. tickets.Items.Select(static t => ZendeskJson.String(t, "id")).OfType<string>().Distinct(StringComparer.Ordinal)];
        }

        RequireFanout(ids.Count, "tickets");

        var collections = await FanOutAsync(
            ids,
            id => client.TryGetPagedAsync($"api/v2/tickets/{id}/comments", "comments", PageSize, cancellationToken)).ConfigureAwait(false);

        var rows = new List<Row>();
        for (var index = 0; index < ids.Count; index++)
        {
            var collection = collections[index];
            rows.AddRange(collection.Items.Select(c => Stamp(context, new Row
            {
                ["comment_id"] = ZendeskJson.String(c, "id"),
                ["ticket_id"] = ids[index],
                ["audit_id"] = ZendeskJson.String(c, "audit_id"),
                ["author_id"] = ZendeskJson.String(c, "author_id"),
                ["public"] = ZendeskJson.Boolean(c, "public"),
                ["type"] = ZendeskJson.String(c, "type"),
                ["body"] = ZendeskJson.String(c, "body"),
                ["plain_body"] = ZendeskJson.String(c, "plain_body"),
                ["created_at"] = ZendeskJson.Timestamp(c, "created_at"),
                ["via_channel"] = ZendeskJson.String(ZendeskJson.Object(c, "via"), "channel"),
                ["attachment_count"] = (decimal)ZendeskJson.Array(c, "attachments").Count()
            }, ZendeskJson.Timestamp(c, "created_at"), collection.FetchedAt)));
        }

        return rows;
    }

    // ---- Helpers --------------------------------------------------------------------------------------------

    /// <summary>Adds the four metadata fields every source ends with.</summary>
    private static Row Stamp(ReadContext context, Row row, DateTime? updatedAt, DateTime fetchedAt)
    {
        row[ZendeskSources.SubdomainField] = context.Account;
        row[ZendeskSources.SourceSystemField] = "zendesk";
        row[ZendeskSources.SourceUpdatedAtField] = updatedAt;
        row[ZendeskSources.SyncedAtField] = fetchedAt;
        return row;
    }

    /// <summary>The bounds the filter's top-level AND chain puts on <paramref name="field"/>.</summary>
    private static (DateOnly? From, DateOnly? To) Window(QueryRequest? query, string field)
    {
        var pushdown = QueryPushdownExtractor.Extract(query, field, []);
        return (pushdown.From, pushdown.To);
    }

    /// <summary>The values the filter names for <paramref name="field"/> through = / IN, or empty.</summary>
    private static List<string> Keys(QueryRequest? query, string field)
    {
        var pushdown = QueryPushdownExtractor.Extract(query, null, [field]);
        return [.. pushdown.Values(field).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The ids among <paramref name="values"/> — Zendesk ids are digits, and an id goes into a URL path.</summary>
    internal static List<string> NumericIds(IEnumerable<string> values) =>
        [.. values.Select(static value => value.Trim()).Where(static value => value.Length is > 0 and <= 19 && value.All(char.IsAsciiDigit)).Distinct(StringComparer.Ordinal)];

    /// <summary>The latest of the bounds that are set — each is necessary, so the tightest still keeps every match.</summary>
    private static DateOnly? Latest(params DateOnly?[] bounds)
    {
        DateOnly? latest = null;
        foreach (var bound in bounds)
        {
            if (bound is { } day && (latest is null || day > latest))
            {
                latest = day;
            }
        }

        return latest;
    }

    /// <summary>
    /// Unix seconds for an export's <c>start_time</c>: a day before the bound (the bound was resolved in the caller's
    /// time zone, which this service does not know), never in the future — the incremental exports refuse a
    /// <c>start_time</c> less than a minute old — and 0 with no bound.
    /// </summary>
    internal static long StartTime(DateOnly? lower, DateTime? now = null)
    {
        if (lower is not { } day)
        {
            return 0;
        }

        var start = new DateTimeOffset(day.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var ceiling = new DateTimeOffset(now ?? DateTime.UtcNow).ToUnixTimeSeconds() - 120;

        return Math.Clamp(start, 0, Math.Max(0, ceiling));
    }

    private static KeyValuePair<string, string> Param(string name, string value) => new(name, value);

    /// <summary>Reads records by id, 100 to a call, from a <c>show_many</c> endpoint.</summary>
    private async Task<ZendeskCollection> ShowManyAsync(string path, string itemsProperty, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var responses = await FanOutAsync(
            ids.Chunk(100),
            chunk => client.GetAsync(path, [Param("ids", string.Join(',', chunk))], reference: false, cancellationToken)).ConfigureAwait(false);

        return new ZendeskCollection(
            [.. responses.SelectMany(response => ZendeskJson.Array(response.Root, itemsProperty))],
            [],
            responses.Count > 0 ? responses.Min(static response => response.FetchedAt) : DateTime.UtcNow);
    }

    /// <summary>Runs <paramref name="load"/> for every item, at most <c>MaxParallelRequests</c> at a time (the client's gate).</summary>
    private static async Task<List<T>> FanOutAsync<TItem, T>(IEnumerable<TItem> items, Func<TItem, Task<T>> load)
    {
        var tasks = items.Select(load).ToList();
        return [.. await Task.WhenAll(tasks).ConfigureAwait(false)];
    }

    private void RequireFanout(int count, string what)
    {
        if (count > Options.MaxFanoutTickets)
        {
            throw new ZendeskRequestException(
                $"This read would make one call for each of {count} {what} (Zendesk:MaxFanoutTickets is {Options.MaxFanoutTickets}). Narrow the filter.");
        }
    }
}

/// <summary>The Search API query text for a ticket read.</summary>
internal static class ZendeskSearch
{
    /// <summary>
    /// <c>type:ticket created&gt;… updated&lt;…</c> for the bounds given. Zendesk reads a date in the searching user's
    /// time zone and its comparison operators are strict, so each bound is moved two days outward: the result is a
    /// superset, and the query DSL removes the rest.
    /// </summary>
    public static string TicketQuery(DateOnly? createdFrom, DateOnly? createdTo, DateOnly? updatedFrom, DateOnly? updatedTo)
    {
        var parts = new List<string> { "type:ticket" };

        AddBound(parts, "created", ">", createdFrom, -2);
        AddBound(parts, "created", "<", createdTo, 2);
        AddBound(parts, "updated", ">", updatedFrom, -2);
        AddBound(parts, "updated", "<", updatedTo, 2);

        return string.Join(' ', parts);
    }

    private static void AddBound(List<string> parts, string field, string op, DateOnly? day, int shiftDays)
    {
        if (day is { } value)
        {
            parts.Add($"{field}{op}{value.AddDays(shiftDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }
    }
}
