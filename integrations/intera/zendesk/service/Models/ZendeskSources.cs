using Intera.Common.Infrastructure.Query;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Intera.Zendesk.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ZendeskFieldType>))]
public enum ZendeskFieldType
{
    Text,
    Number,
    Date,
    Flag
}

/// <param name="Lookup">Source id whose rows name this id, for the plugin's field lookup.</param>
/// <param name="LookupValue">Field of <paramref name="Lookup"/> holding the id.</param>
/// <param name="LookupLabel">Field of <paramref name="Lookup"/> holding the label.</param>
public sealed record ZendeskField(
    string Name,
    ZendeskFieldType Type,
    string Label,
    string? Lookup = null,
    string? LookupValue = null,
    string? LookupLabel = null);

/// <param name="CustomFields">
/// The source's rows also carry the account's custom fields as <c>custom_field_NNN</c> columns, which the allowlist
/// admits by pattern (only <c>tickets</c>).
/// </param>
public sealed record ZendeskSource(string Id, string Name, string Description, IReadOnlyList<ZendeskField> Fields, bool CustomFields = false)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, InMemoryQueryField> Allowlist { get; } = CustomFields
        ? new CustomFieldAllowlist(InMemoryQueryFieldsBuilder.Build([.. Fields.Select(static field => field.Name)]))
        : InMemoryQueryFieldsBuilder.Build([.. Fields.Select(static field => field.Name)]);
}

/// <summary>
/// Every data source this API serves: its id, its fields and their types. The one list the allowlists, the row
/// projection and the plugin's field descriptors (<c>GET /api/Schema/sources</c>) are all built from, so the three
/// cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ids are text</b>: a lookup and its target must have the same type, and Zendesk ids are 64-bit numbers. Each
/// source names its own id <c>&lt;entity&gt;_id</c> (<c>ticket_id</c>, <c>user_id</c>, …) rather than the bare <c>id</c> of
/// Zendesk's JSON — one canonical name per field across sources, so a lookup can point at it.
/// </para>
/// <para>
/// <b>List-valued fields</b> (tags, domain names, field ids) are one comma-joined text column, like the Jira and Zoho
/// sources; a user's and an organization's custom fields are one JSON text column (<c>user_fields</c>,
/// <c>organization_fields</c>), because their keys are free text rather than ids.
/// </para>
/// <para>
/// Every source ends with the same four metadata fields (<see cref="Metadata"/>).
/// </para>
/// </remarks>
public static class ZendeskSources
{
    public const string Tickets = "tickets";
    public const string TicketMetrics = "ticket_metrics";
    public const string TicketMetricEvents = "ticket_metric_events";
    public const string TicketComments = "ticket_comments";
    public const string TicketFields = "ticket_fields";
    public const string TicketForms = "ticket_forms";
    public const string Users = "users";
    public const string Organizations = "organizations";
    public const string Groups = "groups";
    public const string GroupMemberships = "group_memberships";
    public const string SatisfactionRatings = "satisfaction_ratings";
    public const string SlaPolicies = "sla_policies";

    public const string SubdomainField = "zendesk_subdomain";
    public const string SourceSystemField = "source_system";
    public const string SourceUpdatedAtField = "source_updated_at";
    public const string SyncedAtField = "intera_synced_at";

    /// <summary>The prefix of a ticket custom field's column: Zendesk's own field id, <c>custom_field_360012345678</c>.</summary>
    public const string CustomFieldPrefix = "custom_field_";

    private static ZendeskField T(string name, string label) => new(name, ZendeskFieldType.Text, label);

    private static ZendeskField N(string name, string label) => new(name, ZendeskFieldType.Number, label);

    private static ZendeskField D(string name, string label) => new(name, ZendeskFieldType.Date, label);

    private static ZendeskField F(string name, string label) => new(name, ZendeskFieldType.Flag, label);

    private static ZendeskField User(string name, string label) => new(name, ZendeskFieldType.Text, label, Users, "user_id", "name");

    private static ZendeskField Organization() =>
        new("organization_id", ZendeskFieldType.Text, "Organization ID", Organizations, "organization_id", "name");

    private static ZendeskField Group() => new("group_id", ZendeskFieldType.Text, "Group ID", Groups, "group_id", "name");

    private static ZendeskField Ticket(string name = "ticket_id", string label = "Ticket ID") =>
        new(name, ZendeskFieldType.Text, label, Tickets, "ticket_id", "subject");

    private static ZendeskField Form() =>
        new("ticket_form_id", ZendeskFieldType.Text, "Ticket form ID", TicketForms, "ticket_form_id", "name");

    /// <summary>The four fields every source ends with.</summary>
    public static readonly IReadOnlyList<ZendeskField> Metadata =
    [
        T(SubdomainField, "Zendesk account"),
        T(SourceSystemField, "Source system"),
        D(SourceUpdatedAtField, "Updated in Zendesk"),
        D(SyncedAtField, "Read from Zendesk")
    ];

    private const string BoundNote =
        " There is no implicit date window: a read is bounded by the filter, and one past `Zendesk:MaxRecordsPerRead` (200 000) records is refused.";

    private static ZendeskSource Source(string id, string name, string description, IEnumerable<ZendeskField> fields, bool customFields = false) =>
        new(id, name, description, [.. fields, .. Metadata], customFields);

    private static IEnumerable<ZendeskField> MinuteFields(string stem, string label) =>
    [
        N($"{stem}_business_minutes", $"{label}, business minutes"),
        N($"{stem}_calendar_minutes", $"{label}, calendar minutes")
    ];

    public static readonly IReadOnlyList<ZendeskSource> All =
    [
        Source(Tickets, "Tickets",
            "Support tickets (`GET /api/v2/tickets`; with `Zendesk:UseIncrementalExport` the Incremental Export API, and a bound on `created_at` / `updated_at` is pushed to the Search export or to the export's `start_time`). Deleted tickets are not returned." + BoundNote +
            " Custom fields are `custom_field_<field id>` columns: `Zendesk:TicketCustomFields` names those a whole-row read carries; a query naming one always gets it.",
            [
                Ticket(), T("external_id", "External ID"), T("subject", "Subject"), T("description", "Description"), T("status", "Status"),
                T("custom_status_id", "Custom status ID"), T("priority", "Priority"), T("type", "Type"), User("requester_id", "Requester ID"),
                User("submitter_id", "Submitter ID"), User("assignee_id", "Assignee ID"), Group(), Organization(),
                Ticket("problem_id", "Problem ticket ID"), Form(), T("brand_id", "Brand ID"), T("recipient", "Recipient"), T("tags", "Tags"),
                T("via_channel", "Channel"), T("satisfaction_score", "Satisfaction score"), D("created_at", "Created"), D("updated_at", "Updated"),
                D("due_at", "Due"), F("has_incidents", "Has incidents"), F("is_public", "Has public comments"), T("url", "Agent URL")
            ],
            customFields: true),

        Source(TicketMetrics, "Ticket metrics",
            "Zendesk's own calculated metrics, one row per ticket: first reply, resolution, requester and agent wait, on-hold time (business and calendar minutes), reopens, replies and assignments (`GET /api/v2/ticket_metrics`). With `Zendesk:UseIncrementalExport` a lower bound on `created_at` / `solved_at` is answered by the incremental ticket export with the `metric_sets` sideload; `ticket_id` = / IN (up to `Zendesk:MaxFanoutTickets`) reads `GET /tickets/{id}/metrics`." + BoundNote,
            [
                T("metric_id", "Metric ID"), Ticket(), D("created_at", "Created"), D("updated_at", "Updated"), D("assigned_at", "Assigned"),
                D("initially_assigned_at", "First assigned"), D("solved_at", "Solved"), D("status_updated_at", "Status changed"),
                D("assignee_updated_at", "Assignee updated"), D("requester_updated_at", "Requester updated"),
                D("latest_comment_added_at", "Latest comment"), N("assignee_stations", "Assignee stations"), N("group_stations", "Group stations"),
                N("reopens", "Reopens"), N("replies", "Public agent replies"),
                .. MinuteFields("reply_time", "First reply time"),
                .. MinuteFields("first_resolution", "First resolution time"),
                .. MinuteFields("full_resolution", "Full resolution time"),
                .. MinuteFields("requester_wait", "Requester wait time"),
                .. MinuteFields("agent_wait", "Agent wait time"),
                .. MinuteFields("on_hold", "On-hold time")
            ]),

        Source(TicketMetricEvents, "Ticket metric events",
            "The history behind the metrics — SLA applications, pauses, fulfilments and **breaches** (`type` = `breach`) for `reply_time`, `agent_work_time`, `requester_wait_time`, `resolution_time` and `group_ownership_time` (`GET /api/v2/incremental/ticket_metric_events`, a stream ascending in `time`). A lower bound on `time` is the stream's `start_time`, an upper one stops reading; `ticket_id` = / IN reads `GET /tickets/{id}/metric_events`. Zendesk allows this endpoint 10 calls a minute." + BoundNote,
            [
                T("event_id", "Event ID"), Ticket(), T("metric", "Metric"), T("type", "Event type"), T("instance_id", "Metric instance"),
                D("time", "Time"), F("deleted", "Deleted"), T("sla_policy_id", "SLA policy ID"), T("sla_policy_title", "SLA policy"),
                N("sla_target_minutes", "SLA target, minutes"), F("sla_business_hours", "SLA in business hours"),
                N("status_calendar_minutes", "Elapsed, calendar minutes"), N("status_business_minutes", "Elapsed, business minutes")
            ]),

        Source(TicketComments, "Ticket comments",
            "**Heavy and sensitive.** Public replies and private internal notes, one call per ticket (`GET /api/v2/tickets/{id}/comments`) — there is no account-wide comment list. A read needs `ticket_id` = / IN, or a lower bound on `created_at` (the tickets updated since are read); at most `Zendesk:MaxFanoutTickets` tickets a read.",
            [
                T("comment_id", "Comment ID"), Ticket(), T("audit_id", "Audit ID"), User("author_id", "Author ID"), F("public", "Public"),
                T("type", "Type"), T("body", "Body"), T("plain_body", "Plain body"), D("created_at", "Created"), T("via_channel", "Channel"),
                N("attachment_count", "Attachments")
            ]),

        Source(TicketFields, "Ticket fields",
            "System and custom ticket fields (`GET /api/v2/ticket_fields`). `ticket_field_id` is the number in a `custom_field_<id>` column of `tickets`.",
            [
                T("ticket_field_id", "Ticket field ID"), T("key", "Key"), T("title", "Title"), T("description", "Description"), T("type", "Type"),
                F("active", "Active"), F("required", "Required"), F("is_system", "System field"), N("position", "Position"),
                T("custom_field_options", "Options"), D("created_at", "Created"), D("updated_at", "Updated")
            ]),

        Source(TicketForms, "Ticket forms",
            "Ticket forms (`GET /api/v2/ticket_forms`) with the ids of the fields they show.",
            [
                Form(), T("name", "Name"), T("display_name", "Display name"), F("active", "Active"), F("is_default", "Default"),
                F("end_user_visible", "Visible to end users"), T("ticket_field_ids", "Ticket field IDs"), D("created_at", "Created"), D("updated_at", "Updated")
            ]),

        Source(Users, "Users",
            "Customers and support staff (`GET /api/v2/users`); `role` is `end-user`, `agent` or `admin`. A filter on `role` (= / IN) narrows the read." + BoundNote,
            [
                User("user_id", "User ID"), T("external_id", "External ID"), T("name", "Name"), T("email", "Email"), T("phone", "Phone"), T("role", "Role"),
                Organization(), new("default_group_id", ZendeskFieldType.Text, "Default group ID", Groups, "group_id", "name"),
                F("active", "Active"), F("suspended", "Suspended"), F("verified", "Verified"), T("time_zone", "Time zone"), T("locale", "Locale"),
                D("last_login_at", "Last login"), D("created_at", "Created"), D("updated_at", "Updated"), T("tags", "Tags"), T("user_fields", "User fields (JSON)")
            ]),

        Source(Organizations, "Organizations",
            "Customer companies (`GET /api/v2/organizations`).",
            [
                Organization(), T("external_id", "External ID"), T("name", "Name"), T("domain_names", "Domain names"), Group(), T("details", "Details"),
                T("tags", "Tags"), T("ticket_restriction", "Ticket restriction"), D("created_at", "Created"), D("updated_at", "Updated"),
                T("organization_fields", "Organization fields (JSON)")
            ]),

        Source(Groups, "Groups",
            "Support teams / queues (`GET /api/v2/groups`).",
            [
                Group(), T("name", "Name"), T("description", "Description"), F("is_default", "Default"), F("deleted", "Deleted"), F("is_public", "Public"),
                D("created_at", "Created"), D("updated_at", "Updated")
            ]),

        Source(GroupMemberships, "Group memberships",
            "Which agent belongs to which group (`GET /api/v2/group_memberships`).",
            [
                T("membership_id", "Membership ID"), User("user_id", "User ID"), Group(), F("is_default", "Default group of the user"),
                D("created_at", "Created"), D("updated_at", "Updated")
            ]),

        Source(SatisfactionRatings, "Satisfaction ratings",
            "Customer satisfaction (CSAT) per ticket (`GET /api/v2/satisfaction_ratings`); `score` is good, bad, offered, unoffered. A window on `created_at` is passed to the endpoint as `start_time` / `end_time`. Needs the Customer Satisfaction feature of the plan." + BoundNote,
            [
                T("rating_id", "Rating ID"), Ticket(), User("requester_id", "Requester ID"), User("assignee_id", "Assignee ID"), Group(),
                T("score", "Score"), T("comment", "Comment"), T("reason", "Reason"), T("reason_id", "Reason ID"),
                D("created_at", "Created"), D("updated_at", "Updated")
            ]),

        Source(SlaPolicies, "SLA policies",
            "SLA targets, one row per policy metric (`GET /api/v2/slas/policies`): the metric, the priority and the target in minutes. Needs a plan with SLAs.",
            [
                T("policy_id", "Policy ID"), T("title", "Title"), T("description", "Description"), N("position", "Position"), T("metric", "Metric"),
                T("priority", "Priority"), N("target_minutes", "Target, minutes"), N("target_seconds", "Target, seconds"),
                F("business_hours", "Business hours"), D("created_at", "Created"), D("updated_at", "Updated")
            ])
    ];

    public static readonly IReadOnlyDictionary<string, ZendeskSource> ById =
        All.ToDictionary(static source => source.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="name"/> is a custom field column (<c>custom_field_</c> and digits).</summary>
    public static bool IsCustomFieldColumn(string name) =>
        name.Length > CustomFieldPrefix.Length
        && name.StartsWith(CustomFieldPrefix, StringComparison.OrdinalIgnoreCase)
        && name.AsSpan(CustomFieldPrefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;

    /// <summary>
    /// A row carrying exactly the source's fields — a missing one reads null, and nothing a loader kept for its own
    /// joins leaks out. A source with custom fields keeps its <c>custom_field_</c> columns too.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Project(ZendeskSource source, IReadOnlyDictionary<string, object?> row)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(row);

        var projected = new Dictionary<string, object?>(source.Fields.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var field in source.Fields)
        {
            projected[field.Name] = row.TryGetValue(field.Name, out var value) ? value : null;
        }

        if (source.CustomFields)
        {
            foreach (var (name, value) in row)
            {
                if (IsCustomFieldColumn(name))
                {
                    projected[name] = value;
                }
            }
        }

        return projected;
    }
}

/// <summary>
/// The static allowlist, plus any <c>custom_field_NNN</c> name: the set differs per account and the engine only ever
/// reads a field as a dictionary key, so admitting the pattern compiles nothing — a custom field the account lacks
/// reads null.
/// </summary>
internal sealed class CustomFieldAllowlist(IReadOnlyDictionary<string, InMemoryQueryField> fields) : IReadOnlyDictionary<string, InMemoryQueryField>
{
    public InMemoryQueryField this[string key] => TryGetValue(key, out var field) ? field : throw new KeyNotFoundException(key);

    public IEnumerable<string> Keys => fields.Keys.Append(ZendeskSources.CustomFieldPrefix + "*");

    public IEnumerable<InMemoryQueryField> Values => fields.Values;

    public int Count => fields.Count;

    public bool ContainsKey(string key) => TryGetValue(key, out _);

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out InMemoryQueryField value)
    {
        if (fields.TryGetValue(key, out value))
        {
            return true;
        }

        if (ZendeskSources.IsCustomFieldColumn(key))
        {
            var name = key.ToLower(CultureInfo.InvariantCulture);
            value = new InMemoryQueryField(name, row => row.TryGetValue(name, out var cell) ? cell : null);
            return true;
        }

        value = null;
        return false;
    }

    public IEnumerator<KeyValuePair<string, InMemoryQueryField>> GetEnumerator() => fields.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
