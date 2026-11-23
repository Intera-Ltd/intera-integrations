using Intera.Common.Infrastructure.Query;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Intera.Jira.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<JiraFieldType>))]
public enum JiraFieldType
{
    Text,
    Number,
    Date,
    Flag
}

/// <param name="Lookup">Source id whose rows name this id, for the plugin's field lookup.</param>
/// <param name="LookupValue">Field of <paramref name="Lookup"/> holding the id.</param>
/// <param name="LookupLabel">Field of <paramref name="Lookup"/> holding the label.</param>
public sealed record JiraField(
    string Name,
    JiraFieldType Type,
    string Label,
    string? Lookup = null,
    string? LookupValue = null,
    string? LookupLabel = null);

/// <param name="CustomFields">
/// The source's rows also carry the site's custom fields as <c>customfield_NNNNN</c> columns, which the allowlist
/// admits by pattern (only <c>issues</c>).
/// </param>
public sealed record JiraSource(string Id, string Name, string Description, IReadOnlyList<JiraField> Fields, bool CustomFields = false)
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
/// <b>Ids are text</b>, Agile ones (board, sprint) included although the Agile API sends numbers: a lookup and its
/// target must have the same type, and every other Jira id is a numeric string. <b>List-valued fields</b> (labels,
/// components, versions, sprints) are one comma-joined text column on <c>issues</c>, and one row per value in the
/// companion sources (<c>issue_labels</c>, <c>issue_components</c>, <c>issue_versions</c>, <c>issue_sprints</c>),
/// which are served from the same search and cost no extra call.
/// </para>
/// <para>
/// Every source ends with the same five metadata fields (<see cref="Metadata"/>): which site the row came from, that it
/// came from Jira, when Jira last changed it, and when this service read it.
/// </para>
/// </remarks>
public static class JiraSources
{
    public const string Projects = "projects";
    public const string Issues = "issues";
    public const string IssueChangelog = "issue_changelog";
    public const string IssueWorklogs = "issue_worklogs";
    public const string IssueLinks = "issue_links";
    public const string IssueLabels = "issue_labels";
    public const string IssueComponents = "issue_components";
    public const string IssueVersions = "issue_versions";
    public const string IssueSprints = "issue_sprints";
    public const string Comments = "comments";
    public const string Boards = "boards";
    public const string Sprints = "sprints";
    public const string Users = "users";
    public const string Versions = "versions";
    public const string Components = "components";
    public const string Fields = "fields";
    public const string IssueTypes = "issue_types";
    public const string Statuses = "statuses";
    public const string Priorities = "priorities";
    public const string CustomFieldOptions = "custom_field_options";

    public const string CloudIdField = "jira_cloud_id";
    public const string SiteUrlField = "jira_site_url";
    public const string SourceSystemField = "source_system";
    public const string SourceUpdatedAtField = "source_updated_at";
    public const string SyncedAtField = "intera_synced_at";

    /// <summary>The prefix of a custom field's column: Jira's own field id, <c>customfield_10037</c>.</summary>
    public const string CustomFieldPrefix = "customfield_";

    private static JiraField T(string name, string label) => new(name, JiraFieldType.Text, label);

    private static JiraField N(string name, string label) => new(name, JiraFieldType.Number, label);

    private static JiraField D(string name, string label) => new(name, JiraFieldType.Date, label);

    private static JiraField F(string name, string label) => new(name, JiraFieldType.Flag, label);

    private static JiraField Project(string name = "project_id", string label = "Project ID") =>
        new(name, JiraFieldType.Text, label, Projects, "project_id", "project_name");

    private static JiraField User(string name, string label) =>
        new(name, JiraFieldType.Text, label, Users, "account_id", "display_name");

    private static JiraField Issue(string name = "issue_id", string label = "Issue ID") =>
        new(name, JiraFieldType.Text, label, Issues, "issue_id", "issue_key");

    private static JiraField Status(string name = "status_id", string label = "Status ID") =>
        new(name, JiraFieldType.Text, label, Statuses, "status_id", "name");

    private static JiraField Priority() => new("priority_id", JiraFieldType.Text, "Priority ID", Priorities, "priority_id", "name");

    private static JiraField IssueType() => new("issue_type_id", JiraFieldType.Text, "Issue type ID", IssueTypes, "issue_type_id", "name");

    private static JiraField Board(string name = "board_id", string label = "Board ID") =>
        new(name, JiraFieldType.Text, label, Boards, "board_id", "board_name");

    private static JiraField Sprint() => new("sprint_id", JiraFieldType.Text, "Sprint ID", Sprints, "sprint_id", "sprint_name");

    private static JiraField Version() => new("version_id", JiraFieldType.Text, "Version ID", Versions, "version_id", "name");

    private static JiraField Component() => new("component_id", JiraFieldType.Text, "Component ID", Components, "component_id", "component_name");

    private static JiraField FieldRef() => new("field_id", JiraFieldType.Text, "Field ID", Fields, "field_id", "field_name");

    /// <summary>What every row of an issue-derived source starts with.</summary>
    private static JiraField[] IssueHead() =>
    [
        Issue(), T("issue_key", "Issue key"), Project(), T("project_key", "Project key")
    ];

    /// <summary>The five fields every source ends with.</summary>
    public static readonly IReadOnlyList<JiraField> Metadata =
    [
        T(CloudIdField, "Jira cloud ID"),
        T(SiteUrlField, "Jira site"),
        T(SourceSystemField, "Source system"),
        D(SourceUpdatedAtField, "Updated in Jira"),
        D(SyncedAtField, "Read from Jira")
    ];

    private const string BoundNote =
        " There is no implicit date window: a read is bounded by the JQL scopes and the filter, and one past `Jira:MaxIssuesPerRead` (20 000) issues is refused.";

    private static JiraSource Source(string id, string name, string description, IEnumerable<JiraField> fields, bool customFields = false) =>
        new(id, name, description, [.. fields, .. Metadata], customFields);

    public static readonly IReadOnlyList<JiraSource> All =
    [
        Source(Projects, "Projects",
            "The site's projects (`GET /rest/api/3/project/search`, archived included): key, type, style (classic / next-gen), category, lead, issue count.",
            [
                Project(), T("project_key", "Project key"), T("project_name", "Project name"), T("project_type_key", "Project type"),
                T("project_style", "Project style"), T("project_category_id", "Category ID"), T("project_category_name", "Category"),
                User("lead_account_id", "Lead account ID"), T("lead_name", "Lead"), T("description", "Description"), T("url", "URL"),
                F("archived", "Archived"), F("simplified", "Team-managed"), F("is_private", "Private"),
                N("issue_count", "Issue count"), D("last_issue_update_at", "Last issue update")
            ]),

        Source(Issues, "Issues",
            "Issues from a JQL search (`POST /rest/api/3/search/jql`) within the deployment's and the instance's JQL scope. Bounds on `created_at`, `updated_at`, `resolution_date` and `due_date` and the projects, keys and ids the filter names go to Jira as JQL." + BoundNote +
            " `description` and `environment` are plain text flattened from Atlassian Document Format; `labels`, components, versions and sprints are comma-joined (one row per value in the `issue_*` sources). Custom fields are columns named by their Jira id (`customfield_10037`) — `fields` lists them; a query that names one gets it, a whole-row read only those `Jira:IssueCustomFields` lists.",
            [
                .. IssueHead(), T("project_name", "Project name"), T("summary", "Summary"), T("description", "Description"),
                IssueType(), T("issue_type_name", "Issue type"), F("is_subtask", "Sub-task"),
                Issue("parent_issue_id", "Parent issue ID"), T("parent_issue_key", "Parent issue key"),
                Status(), T("status_name", "Status"), T("status_category", "Status category"), T("status_category_key", "Status category key"),
                Priority(), T("priority_name", "Priority"), T("resolution_id", "Resolution ID"), T("resolution_name", "Resolution"),
                User("assignee_account_id", "Assignee account ID"), T("assignee_name", "Assignee"),
                User("reporter_account_id", "Reporter account ID"), T("reporter_name", "Reporter"),
                User("creator_account_id", "Creator account ID"), T("creator_name", "Creator"),
                D("created_at", "Created"), D("updated_at", "Updated"), D("due_date", "Due date"), D("resolution_date", "Resolved"),
                D("status_category_changed_at", "Status category changed"),
                T("labels", "Labels"), T("component_ids", "Component IDs"), T("component_names", "Components"),
                T("fix_version_ids", "Fix version IDs"), T("fix_version_names", "Fix versions"),
                T("affected_version_ids", "Affected version IDs"), T("affected_version_names", "Affected versions"),
                T("sprint_ids", "Sprint IDs"), T("sprint_names", "Sprints"), T("environment", "Environment"),
                N("original_estimate_seconds", "Original estimate, s"), N("remaining_estimate_seconds", "Remaining estimate, s"),
                N("time_spent_seconds", "Time spent, s"), N("aggregate_original_estimate_seconds", "Original estimate with sub-tasks, s"),
                N("aggregate_remaining_estimate_seconds", "Remaining estimate with sub-tasks, s"), N("aggregate_time_spent_seconds", "Time spent with sub-tasks, s"),
                N("subtask_count", "Sub-tasks"), N("watch_count", "Watchers"), N("vote_count", "Votes"), T("url", "URL")
            ],
            customFields: true),

        Source(IssueChangelog, "Issue changelog",
            "Every change of every issue in scope (`POST /rest/api/3/changelog/bulkfetch`, 1000 issues a call): one row per changed field, oldest first. `changelog_id` is the change, `change_item_id` the row (a change can touch several fields). A lower bound on `changed_at` reads only issues updated since; `field_id` = / IN (up to 10) narrows the read." + BoundNote +
            " The whole history of each issue read is returned, so time-in-status can be computed from it.",
            [
                T("change_item_id", "Change item ID"), T("changelog_id", "Changelog ID"), .. IssueHead(), D("changed_at", "Changed"),
                User("author_account_id", "Author account ID"), T("author_name", "Author"), FieldRef(), T("field_name", "Field"),
                T("field_type", "Field type"), T("from_value", "From value"), T("from_display_value", "From"),
                T("to_value", "To value"), T("to_display_value", "To")
            ]),

        Source(IssueWorklogs, "Issue worklogs",
            "Time logged on issues in scope. Bounds on `started_at` go to the search as `worklogDate`; up to `Jira:WorklogPerIssueMaxIssues` issues are read one by one (`GET /issue/{id}/worklog`), more through `GET /worklog/updated` + `POST /worklog/list`." +
            BoundNote + " Without a bound on `started_at` every worklog of the issues in scope is read. `comment` is plain text.",
            [
                T("worklog_id", "Worklog ID"), .. IssueHead(), User("author_account_id", "Author account ID"), T("author_name", "Author"),
                User("update_author_account_id", "Updated by account ID"), D("started_at", "Started"), D("created_at", "Created"),
                D("updated_at", "Updated"), N("time_spent_seconds", "Time spent, s"), N("time_spent_hours", "Time spent, h"), T("comment", "Comment")
            ]),

        Source(IssueLinks, "Issue links",
            "Links between issues in scope (`issuelinks` of the search), one row per link, always read in its outward direction: `source` *outward description* `target` — e.g. DEV-1 *blocks* DEV-2. A link between two issues in scope appears once. Filters on the source issue, its project key and `source_updated_at` go to the search." + BoundNote,
            [
                T("link_id", "Link ID"), Issue("source_issue_id", "Source issue ID"), T("source_issue_key", "Source issue key"),
                T("source_project_key", "Source project key"), T("source_status_category_key", "Source status category key"),
                Issue("target_issue_id", "Target issue ID"), T("target_issue_key", "Target issue key"), T("target_project_key", "Target project key"),
                T("target_status_category_key", "Target status category key"), T("link_type_id", "Link type ID"), T("link_type_name", "Link type"),
                T("inward_description", "Inward description"), T("outward_description", "Outward description")
            ]),

        Source(IssueLabels, "Issue labels",
            "One row per label of every issue in scope — the same search as `issues`, so filters work the same way." + BoundNote,
            [.. IssueHead(), Status(), T("status_category_key", "Status category key"), T("label", "Label")]),

        Source(IssueComponents, "Issue components",
            "One row per component of every issue in scope — the same search as `issues`." + BoundNote,
            [.. IssueHead(), Status(), T("status_category_key", "Status category key"), Component(), T("component_name", "Component")]),

        Source(IssueVersions, "Issue versions",
            "One row per fix version (`relation` = `fix`) and affected version (`affected`) of every issue in scope — the same search as `issues`." + BoundNote,
            [
                .. IssueHead(), Status(), T("status_category_key", "Status category key"), Version(), T("version_name", "Version"),
                T("relation", "Relation")
            ]),

        Source(IssueSprints, "Issue sprints",
            "One row per sprint of every issue in scope (the Jira Software sprint field) — the same search as `issues`. An issue carried over appears once per sprint it was in." + BoundNote,
            [
                .. IssueHead(), Status(), T("status_category_key", "Status category key"), Sprint(), T("sprint_name", "Sprint"),
                T("sprint_state", "Sprint state"), Board()
            ]),

        Source(Comments, "Comments",
            "**Heavy and potentially sensitive — not meant to be on by default.** Every comment of every issue in scope, one call per issue (`GET /issue/{id}/comment`), at most `Jira:MaxCommentIssues` issues a read. A lower bound on `created_at` / `updated_at` reads only issues updated since." + BoundNote + " `body` is plain text.",
            [
                T("comment_id", "Comment ID"), .. IssueHead(), User("author_account_id", "Author account ID"), T("author_name", "Author"),
                User("update_author_account_id", "Updated by account ID"), D("created_at", "Created"), D("updated_at", "Updated"),
                T("body", "Body"), N("body_length", "Body length"), F("is_public", "Public (service desk)"),
                T("visibility_type", "Visibility type"), T("visibility_value", "Visible to")
            ]),

        Source(Boards, "Boards",
            "Jira Software boards (`GET /rest/agile/1.0/board`): scrum, kanban, simple, with the project they sit in. `filter_id` / `filter_name` cost one call per board (board configuration), made only when a query needs them.",
            [
                Board(), T("board_name", "Board"), T("board_type", "Board type"), Project(), T("project_key", "Project key"),
                T("project_name", "Project name"), T("filter_id", "Filter ID"), T("filter_name", "Filter"),
                T("location_type", "Location type"), T("location_name", "Location"), F("is_private", "Private")
            ]),

        Source(Sprints, "Sprints",
            "Sprints of every board that can hold them — scrum, and the `simple` boards of team-managed spaces — (`GET /rest/agile/1.0/board/{id}/sprint`), one row per sprint: a sprint shown on several boards appears once, under the board it was created on (`board_id` = `origin_board_id`). `state` is future, active or closed. Filters on `board_id`, `state` and `sprint_id` narrow the read.",
            [
                Sprint(), Board(), T("sprint_name", "Sprint"), T("state", "State"), T("goal", "Goal"), D("start_date", "Start"),
                D("end_date", "End"), D("complete_date", "Completed"), D("created_date", "Created"), Board("origin_board_id", "Origin board ID")
            ]),

        Source(Users, "Users",
            "Every Atlassian account the site knows (`GET /rest/api/3/users/search`), active or not, apps and customers included. Join on `account_id`: `email` is often hidden by the user's privacy settings.",
            [
                User("account_id", "Account ID"), T("display_name", "Name"), T("email", "Email"), F("active", "Active"),
                T("account_type", "Account type"), T("timezone", "Time zone"), T("locale", "Locale")
            ]),

        Source(Versions, "Versions",
            "Project releases (`GET /rest/api/3/project/{id}/version`), one call per project; a filter on `project_id` / `project_key` reads only those projects.",
            [
                Version(), Project(), T("project_key", "Project key"), T("name", "Name"), T("description", "Description"),
                D("start_date", "Start date"), D("release_date", "Release date"), F("released", "Released"), F("archived", "Archived"),
                F("overdue", "Overdue")
            ]),

        Source(Components, "Components",
            "Project components (`GET /rest/api/3/project/{id}/component`), one call per project; a filter on `project_id` / `project_key` reads only those projects.",
            [
                Component(), Project(), T("project_key", "Project key"), T("component_name", "Component"), T("description", "Description"),
                User("lead_account_id", "Lead account ID"), T("lead_name", "Lead"), T("assignee_type", "Default assignee"),
                N("issue_count", "Issue count")
            ]),

        Source(Fields, "Fields",
            "Every system and custom field of the site (`GET /rest/api/3/field`). `issue_column` is the column a custom field has in `issues` (its id) when that source carries it.",
            [
                FieldRef(), T("field_key", "Field key"), T("field_name", "Field"), F("custom", "Custom"), T("field_type", "Type"),
                T("items_type", "Item type"), F("searchable", "Searchable"), F("navigable", "Navigable"), F("orderable", "Orderable"),
                T("custom_type", "Custom type"), T("custom_id", "Custom ID"), T("clause_names", "JQL names"), T("issue_column", "Column in issues")
            ]),

        Source(IssueTypes, "Issue types",
            "Issue types (`GET /rest/api/3/issuetype`). `hierarchy_level` is 1 for an epic, 0 for a standard type, -1 for a sub-task; a team-managed project's own types carry `scope_project_id`.",
            [
                IssueType(), T("name", "Name"), T("description", "Description"), F("subtask", "Sub-task"), N("hierarchy_level", "Hierarchy level"),
                T("scope_type", "Scope"), Project("scope_project_id", "Scope project ID")
            ]),

        Source(Statuses, "Statuses",
            "Statuses of the site's active workflows (`GET /rest/api/3/status`) with their category (`new` / `indeterminate` / `done` = To Do / In Progress / Done).",
            [
                Status(), T("name", "Name"), T("description", "Description"), T("status_category_id", "Status category ID"),
                T("status_category_key", "Status category key"), T("status_category_name", "Status category"),
                T("scope_type", "Scope"), Project("scope_project_id", "Scope project ID")
            ]),

        Source(Priorities, "Priorities",
            "Issue priorities (`GET /rest/api/3/priority`).",
            [Priority(), T("name", "Name"), T("description", "Description"), T("status_color", "Colour"), F("is_default", "Default")]),

        Source(CustomFieldOptions, "Custom field options",
            "The options of every select-list custom field, per context (`GET /rest/api/3/field/{id}/context` and `…/context/{id}/option`). **Needs the authorising user to be a Jira administrator** and the granular field scopes; a filter on `field_id` reads only those fields. A cascading option's child carries `parent_option_id`.",
            [
                FieldRef(), T("field_name", "Field"), T("context_id", "Context ID"), T("context_name", "Context"),
                T("option_id", "Option ID"), T("option_value", "Option"), T("parent_option_id", "Parent option ID"), F("disabled", "Disabled")
            ])
    ];

    public static readonly IReadOnlyDictionary<string, JiraSource> ById =
        All.ToDictionary(static source => source.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="name"/> is a custom field column (<c>customfield_</c> and digits).</summary>
    public static bool IsCustomFieldColumn(string name) =>
        name.Length > CustomFieldPrefix.Length
        && name.StartsWith(CustomFieldPrefix, StringComparison.OrdinalIgnoreCase)
        && name.AsSpan(CustomFieldPrefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;

    /// <summary>
    /// A row carrying exactly the source's fields — a missing one reads null, and nothing a loader kept for its own
    /// joins leaks out. A source with custom fields keeps its <c>customfield_</c> columns too.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Project(JiraSource source, IReadOnlyDictionary<string, object?> row)
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
/// The static allowlist, plus any <c>customfield_NNNNN</c> name: the set differs per site and the engine only
/// ever reads a field as a dictionary key, so admitting the pattern compiles nothing — a custom field the site
/// lacks reads null. The same view <c>ZohoCustomFields</c> builds for Zoho's <c>cf_</c> columns.
/// </summary>
internal sealed class CustomFieldAllowlist(IReadOnlyDictionary<string, InMemoryQueryField> fields) : IReadOnlyDictionary<string, InMemoryQueryField>
{
    public InMemoryQueryField this[string key] => TryGetValue(key, out var field) ? field : throw new KeyNotFoundException(key);

    public IEnumerable<string> Keys => fields.Keys.Append(JiraSources.CustomFieldPrefix + "*");

    public IEnumerable<InMemoryQueryField> Values => fields.Values;

    public int Count => fields.Count;

    public bool ContainsKey(string key) => TryGetValue(key, out _);

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out InMemoryQueryField value)
    {
        if (fields.TryGetValue(key, out value))
        {
            return true;
        }

        if (JiraSources.IsCustomFieldColumn(key))
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
