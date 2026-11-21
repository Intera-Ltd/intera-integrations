using Intera.Jira.Api.Clients;
using Intera.Jira.Api.Infrastructure;
using Intera.Jira.Api.Models;
using System.Globalization;
using System.Text.Json;

namespace Intera.Jira.Api.Services;

using Row = Dictionary<string, object?>;

/// <summary>
/// The issue-based sources: issues, the one-row-per-value companions, links, changelog, worklogs and comments. Each
/// starts from one JQL search (<c>POST /rest/api/3/search/jql</c>) whose JQL is built from the scopes and the filter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pushdown is a superset only.</b> The DSL still runs in memory over what comes back, so a condition Jira cannot
/// take costs calls, never rows. What goes to JQL: the projects and issues the filter names (= / IN), the bounds on the
/// source's date fields (widened by a day, see <see cref="Jql"/>), the deployment's and the instance's scope.
/// </para>
/// <para>
/// <b>No implicit date window:</b> a read is bounded by the JQL scopes and what the
/// filter pushes down, and one that would return more than <c>Jira:MaxIssuesPerRead</c> issues is refused (400) rather
/// than truncated. With nothing to restrict by at all, the search falls back to <see cref="Jql.AlwaysTrue"/>, because
/// enhanced search refuses an unbounded query.
/// </para>
/// <para>
/// <b>JQL answers an unknown project, key or id with an error, not with no issues.</b> Projects are therefore matched
/// against the site's list first (an unknown one is dropped, only unknown ones mean no rows); a search refused with
/// "does not exist" while naming issues is run once more without the names, narrowed to their projects instead.
/// </para>
/// </remarks>
public sealed partial class JiraDataService
{
    /// <summary>The issue fields <c>issues</c> reads, besides description, environment, the sprint field and custom fields.</summary>
    private static readonly string[] IssueFields =
    [
        "summary", "issuetype", "parent", "project", "status", "priority", "resolution", "assignee", "reporter", "creator",
        "created", "updated", "duedate", "resolutiondate", "statuscategorychangedate", "labels", "components", "fixVersions",
        "versions", "timeoriginalestimate", "timeestimate", "timespent", "aggregatetimeoriginalestimate", "aggregatetimeestimate",
        "aggregatetimespent", "subtasks", "watches", "votes"
    ];

    /// <summary>A filter field whose bounds go to JQL as <paramref name="JqlField"/>.</summary>
    /// <param name="Upper">Whether an upper bound narrows the search too (it does not for a change, comment or worklog
    /// after which the issue may have been updated again).</param>
    private sealed record DateMapping(string Field, string JqlField, bool Upper);

    /// <summary>How one source's filter maps onto an issue search.</summary>
    private sealed record IssueReadSpec(
        IReadOnlyList<DateMapping> Dates,
        string[] IssueIdFields,
        string[] IssueKeyFields,
        string[] ProjectFields);

    private sealed record IssueSearch(IReadOnlyList<JsonElement> Issues, DateTime FetchedAt, string Jql);

    /// <summary>What a row of an activity source (changelog, worklog, comment) needs from its issue.</summary>
    internal sealed record IssueRef(string Id, string? Key, string? ProjectId, string? ProjectKey);

    /// <summary>Named issues searched one by one when JQL refuses one of them; more fall back to their projects.</summary>
    private const int MaxSingleIssueSearches = 50;

    private static readonly string[] IssueIdFieldNames = ["issue_id"];
    private static readonly string[] IssueKeyFieldNames = ["issue_key"];
    private static readonly string[] ProjectFieldNames = ["project_id", "project_key"];

    private static readonly IssueReadSpec IssuesSpec = new(
        [
            new("created_at", "created", Upper: true),
            new("updated_at", "updated", Upper: true),
            new("resolution_date", "resolved", Upper: true),
            new("due_date", "due", Upper: true),
            new(JiraSources.SourceUpdatedAtField, "updated", Upper: true)
        ],
        IssueIdFieldNames, IssueKeyFieldNames, ProjectFieldNames);

    private static readonly IssueReadSpec IssueValuesSpec = new(
        [new(JiraSources.SourceUpdatedAtField, "updated", Upper: true)],
        IssueIdFieldNames, IssueKeyFieldNames, ProjectFieldNames);

    private static readonly IssueReadSpec LinksSpec = new(
        [], ["source_issue_id"], ["source_issue_key"], ["source_project_key"]);

    private static readonly IssueReadSpec ChangelogSpec = new(
        [new("changed_at", "updated", Upper: false)],
        IssueIdFieldNames, IssueKeyFieldNames, ProjectFieldNames);

    private static readonly IssueReadSpec WorklogSpec = new(
        [new("started_at", "worklogDate", Upper: true)],
        IssueIdFieldNames, IssueKeyFieldNames, ProjectFieldNames);

    private static readonly IssueReadSpec CommentSpec = new(
        [new("created_at", "updated", Upper: false), new("updated_at", "updated", Upper: false)],
        IssueIdFieldNames, IssueKeyFieldNames, ProjectFieldNames);

    // ---- The search -----------------------------------------------------------------------------------------

    /// <summary>
    /// The JQL query for <paramref name="spec"/>, or null when the filter can match no issue at all (it names only
    /// projects the site does not have, or only malformed keys or ids).
    /// </summary>
    private async Task<JqlIssueQuery?> PlanAsync(ReadContext context, IssueReadSpec spec, bool nameIssues, CancellationToken cancellationToken)
    {
        var query = context.Query;
        var pushdown = Intera.Common.Infrastructure.Query.QueryPushdownExtractor.Extract(
            query, null, [.. spec.IssueIdFields, .. spec.IssueKeyFields, .. spec.ProjectFields]);

        var keyValues = spec.IssueKeyFields.SelectMany(pushdown.Values).ToList();
        var idValues = spec.IssueIdFields.SelectMany(pushdown.Values).ToList();
        var keys = keyValues.Where(Jql.IsIssueKey).Select(static key => key.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList();
        var ids = idValues.Where(Jql.IsNumericId).Distinct(StringComparer.Ordinal).ToList();

        // A key or id that is not even well formed matches nothing; a filter naming only such values matches nothing.
        if ((keyValues.Count > 0 && keys.Count == 0) || (idValues.Count > 0 && ids.Count == 0))
        {
            return null;
        }

        var projectValues = spec.ProjectFields.SelectMany(pushdown.Values).ToList();
        if (!nameIssues)
        {
            projectValues.AddRange(keys.Select(Jql.ProjectKeyOf).OfType<string>());
            keys = [];
            ids = [];
        }

        var projects = await NamedProjectsAsync(context, projectValues, cancellationToken).ConfigureAwait(false);
        if (projects is { Count: 0 })
        {
            return null;
        }

        var bounds = new List<JqlDateBound>();
        foreach (var mapping in spec.Dates)
        {
            var (from, to) = Window(query, mapping.Field);
            to = mapping.Upper ? to : null;
            if (from is not null || to is not null)
            {
                bounds.Add(new JqlDateBound(mapping.JqlField, from, to));
            }
        }

        return new JqlIssueQuery(
            context.Scopes,
            [.. (projects ?? []).Select(static project => project.Id)],
            keys,
            ids,
            bounds);
    }

    private async Task<IssueSearch> SearchAsync(ReadContext context, IssueReadSpec spec, IReadOnlyList<string> fields, CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(context, spec, nameIssues: true, cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return new IssueSearch([], DateTime.UtcNow, string.Empty);
        }

        var jql = Jql.Build(plan);
        try
        {
            var found = await client.SearchIssuesAsync(context.Site, jql, fields, cancellationToken).ConfigureAwait(false);
            return new IssueSearch(found.Items, found.FetchedAt, jql);
        }
        catch (JiraRequestException exception) when ((plan.IssueKeys.Count > 0 || plan.IssueIds.Count > 0)
            && exception.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            // JQL refuses an issue key or id that does not exist instead of matching nothing. A lookup names a few:
            // each is searched alone, and the ones Jira refuses are simply absent. Many: the issues' projects are
            // searched instead, and the in-memory pass picks the named issues out of them.
            var singles = plan.IssueKeys.Select(key => plan with { IssueKeys = [key], IssueIds = [] })
                .Concat(plan.IssueIds.Select(id => plan with { IssueKeys = [], IssueIds = [id] }))
                .ToList();

            if (singles.Count <= MaxSingleIssueSearches)
            {
                logger.LogInformation(
                    "Jira refused an issue named in the filter ({Message}); searching the {Count} named issues one by one.",
                    exception.Message, singles.Count);

                var results = await FanOutAsync(singles, single => SearchExistingAsync(context.Site, Jql.Build(single), fields, cancellationToken))
                    .ConfigureAwait(false);

                return new IssueSearch(
                    [.. results.SelectMany(static result => result.Issues).DistinctBy(static issue => JiraJson.String(issue, "id"))],
                    results.Min(static result => result.FetchedAt),
                    string.Join(" | ", results.Select(static result => result.Jql)));
            }

            logger.LogInformation("Jira refused the issues named in the filter ({Message}); searching their projects instead.", exception.Message);

            var fallback = await PlanAsync(context, spec, nameIssues: false, cancellationToken).ConfigureAwait(false);
            if (fallback is null)
            {
                return new IssueSearch([], DateTime.UtcNow, string.Empty);
            }

            var fallbackJql = Jql.Build(fallback);
            var found = await client.SearchIssuesAsync(context.Site, fallbackJql, fields, cancellationToken).ConfigureAwait(false);
            return new IssueSearch(found.Items, found.FetchedAt, fallbackJql);
        }
    }

    /// <summary>A search for one named issue; Jira's "does not exist" is no issue rather than a failure.</summary>
    private async Task<IssueSearch> SearchExistingAsync(JiraSite site, string jql, IReadOnlyList<string> fields, CancellationToken cancellationToken)
    {
        try
        {
            var found = await client.SearchIssuesAsync(site, jql, fields, cancellationToken).ConfigureAwait(false);
            return new IssueSearch(found.Items, found.FetchedAt, jql);
        }
        catch (JiraRequestException exception) when (exception.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            return new IssueSearch([], DateTime.UtcNow, jql);
        }
    }

    // ---- Issues ---------------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadIssuesAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var references = context.References;
        var catalog = await ReadFieldCatalogAsync(context.Site, cancellationToken).ConfigureAwait(false);

        var fields = new List<string>(IssueFields);
        if (references.Needs("description"))
        {
            fields.Add("description");
        }

        if (references.Needs("environment"))
        {
            fields.Add("environment");
        }

        var sprintField = catalog.SprintField;
        if (sprintField is not null && (references.Needs("sprint_ids") || references.Needs("sprint_names")))
        {
            fields.Add(sprintField.Id);
        }

        // Custom fields cost payload on every issue, so only those the answer can depend on are asked for.
        var customFields = references.IsPassthrough
            ? ExposedCustomFields(catalog).ToList()
            : [.. references.Fields.Where(JiraSources.IsCustomFieldColumn).Select(catalog.Find).OfType<JiraFieldInfo>()];
        fields.AddRange(customFields.Select(static field => field.Id));

        var search = await SearchAsync(context, IssuesSpec, fields, cancellationToken).ConfigureAwait(false);

        return [.. search.Issues.Select(issue => IssueRow(context, issue, sprintField, customFields, search.FetchedAt))];
    }

    private static Row IssueRow(ReadContext context, JsonElement issue, JiraFieldInfo? sprintField, IReadOnlyList<JiraFieldInfo> customFields, DateTime fetchedAt)
    {
        var f = JiraJson.Object(issue, "fields");
        var project = JiraJson.Object(f, "project");
        var type = JiraJson.Object(f, "issuetype");
        var parent = JiraJson.Object(f, "parent");
        var status = JiraJson.Object(f, "status");
        var category = JiraJson.Object(status, "statusCategory");
        var priority = JiraJson.Object(f, "priority");
        var resolution = JiraJson.Object(f, "resolution");
        var assignee = JiraJson.Object(f, "assignee");
        var reporter = JiraJson.Object(f, "reporter");
        var creator = JiraJson.Object(f, "creator");
        var components = JiraJson.Array(f, "components").ToList();
        var fixVersions = JiraJson.Array(f, "fixVersions").ToList();
        var versions = JiraJson.Array(f, "versions").ToList();
        var sprints = sprintField is null ? [] : JiraJson.Array(f, sprintField.Id).Where(static s => s.ValueKind == JsonValueKind.Object).ToList();
        var key = JiraJson.String(issue, "key");
        var updated = JiraJson.Timestamp(f, "updated");

        var row = new Row
        {
            ["issue_id"] = JiraJson.String(issue, "id"),
            ["issue_key"] = key,
            ["project_id"] = JiraJson.String(project, "id"),
            ["project_key"] = JiraJson.String(project, "key"),
            ["project_name"] = JiraJson.String(project, "name"),
            ["summary"] = JiraJson.String(f, "summary"),
            ["description"] = AdfText.ToText(JiraJson.Property(f, "description")),
            ["issue_type_id"] = JiraJson.String(type, "id"),
            ["issue_type_name"] = JiraJson.String(type, "name"),
            ["is_subtask"] = JiraJson.Boolean(type, "subtask"),
            ["parent_issue_id"] = JiraJson.String(parent, "id"),
            ["parent_issue_key"] = JiraJson.String(parent, "key"),
            ["status_id"] = JiraJson.String(status, "id"),
            ["status_name"] = JiraJson.String(status, "name"),
            ["status_category"] = JiraJson.String(category, "name"),
            ["status_category_key"] = JiraJson.String(category, "key"),
            ["priority_id"] = JiraJson.String(priority, "id"),
            ["priority_name"] = JiraJson.String(priority, "name"),
            ["resolution_id"] = JiraJson.String(resolution, "id"),
            ["resolution_name"] = JiraJson.String(resolution, "name"),
            ["assignee_account_id"] = JiraJson.String(assignee, "accountId"),
            ["assignee_name"] = JiraJson.String(assignee, "displayName"),
            ["reporter_account_id"] = JiraJson.String(reporter, "accountId"),
            ["reporter_name"] = JiraJson.String(reporter, "displayName"),
            ["creator_account_id"] = JiraJson.String(creator, "accountId"),
            ["creator_name"] = JiraJson.String(creator, "displayName"),
            ["created_at"] = JiraJson.Timestamp(f, "created"),
            ["updated_at"] = updated,
            ["due_date"] = JiraJson.Date(f, "duedate"),
            ["resolution_date"] = JiraJson.Timestamp(f, "resolutiondate"),
            ["status_category_changed_at"] = JiraJson.Timestamp(f, "statuscategorychangedate"),
            ["labels"] = JiraJson.Join(JiraJson.Array(f, "labels").Select(JiraJson.Text)),
            ["component_ids"] = JiraJson.Join(components.Select(static c => JiraJson.String(c, "id"))),
            ["component_names"] = JiraJson.Join(components.Select(static c => JiraJson.String(c, "name"))),
            ["fix_version_ids"] = JiraJson.Join(fixVersions.Select(static v => JiraJson.String(v, "id"))),
            ["fix_version_names"] = JiraJson.Join(fixVersions.Select(static v => JiraJson.String(v, "name"))),
            ["affected_version_ids"] = JiraJson.Join(versions.Select(static v => JiraJson.String(v, "id"))),
            ["affected_version_names"] = JiraJson.Join(versions.Select(static v => JiraJson.String(v, "name"))),
            ["sprint_ids"] = JiraJson.Join(sprints.Select(static s => JiraJson.String(s, "id"))),
            ["sprint_names"] = JiraJson.Join(sprints.Select(static s => JiraJson.String(s, "name"))),
            ["environment"] = AdfText.ToText(JiraJson.Property(f, "environment")),
            ["original_estimate_seconds"] = JiraJson.Long(f, "timeoriginalestimate"),
            ["remaining_estimate_seconds"] = JiraJson.Long(f, "timeestimate"),
            ["time_spent_seconds"] = JiraJson.Long(f, "timespent"),
            ["aggregate_original_estimate_seconds"] = JiraJson.Long(f, "aggregatetimeoriginalestimate"),
            ["aggregate_remaining_estimate_seconds"] = JiraJson.Long(f, "aggregatetimeestimate"),
            ["aggregate_time_spent_seconds"] = JiraJson.Long(f, "aggregatetimespent"),
            ["subtask_count"] = (long)JiraJson.Array(f, "subtasks").Count(),
            ["watch_count"] = JiraJson.Long(JiraJson.Object(f, "watches"), "watchCount"),
            ["vote_count"] = JiraJson.Long(JiraJson.Object(f, "votes"), "votes"),
            ["url"] = key is null ? null : $"{context.Site.Url}/browse/{key}"
        };

        foreach (var field in customFields)
        {
            row[field.Id.ToLowerInvariant()] = ReadCustomValue(field, JiraJson.Property(f, field.Id));
        }

        return Stamp(context, row, updated, fetchedAt);
    }

    // ---- One row per value: labels, components, versions, sprints -------------------------------------------

    private async Task<List<Row>> LoadIssueValuesAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var catalog = await ReadFieldCatalogAsync(context.Site, cancellationToken).ConfigureAwait(false);
        var sprintField = catalog.SprintField;

        string[] valueFields = context.Source.Id switch
        {
            JiraSources.IssueLabels => ["labels"],
            JiraSources.IssueComponents => ["components"],
            JiraSources.IssueVersions => ["fixVersions", "versions"],
            JiraSources.IssueSprints when sprintField is not null => [sprintField.Id],
            JiraSources.IssueSprints => [],
            _ => throw new ArgumentException($"Unknown issue value source '{context.Source.Id}'.")
        };

        // A site without Jira Software has no sprint field, so no issue is in a sprint.
        if (context.Source.Id == JiraSources.IssueSprints && sprintField is null)
        {
            return [];
        }

        var search = await SearchAsync(context, IssueValuesSpec, ["project", "status", "updated", .. valueFields], cancellationToken).ConfigureAwait(false);
        var rows = new List<Row>();

        foreach (var issue in search.Issues)
        {
            var f = JiraJson.Object(issue, "fields");
            var project = JiraJson.Object(f, "project");
            var status = JiraJson.Object(f, "status");
            var updated = JiraJson.Timestamp(f, "updated");

            Row Head() => new()
            {
                ["issue_id"] = JiraJson.String(issue, "id"),
                ["issue_key"] = JiraJson.String(issue, "key"),
                ["project_id"] = JiraJson.String(project, "id"),
                ["project_key"] = JiraJson.String(project, "key"),
                ["status_id"] = JiraJson.String(status, "id"),
                ["status_category_key"] = JiraJson.String(JiraJson.Object(status, "statusCategory"), "key")
            };

            void Add(Row row) => rows.Add(Stamp(context, row, updated, search.FetchedAt));

            switch (context.Source.Id)
            {
                case JiraSources.IssueLabels:
                    foreach (var label in JiraJson.Array(f, "labels").Select(JiraJson.Text).OfType<string>())
                    {
                        var row = Head();
                        row["label"] = label;
                        Add(row);
                    }

                    break;

                case JiraSources.IssueComponents:
                    foreach (var component in JiraJson.Array(f, "components"))
                    {
                        var row = Head();
                        row["component_id"] = JiraJson.String(component, "id");
                        row["component_name"] = JiraJson.String(component, "name");
                        Add(row);
                    }

                    break;

                case JiraSources.IssueVersions:
                    foreach (var (property, relation) in new[] { ("fixVersions", "fix"), ("versions", "affected") })
                    {
                        foreach (var version in JiraJson.Array(f, property))
                        {
                            var row = Head();
                            row["version_id"] = JiraJson.String(version, "id");
                            row["version_name"] = JiraJson.String(version, "name");
                            row["relation"] = relation;
                            Add(row);
                        }
                    }

                    break;

                case JiraSources.IssueSprints:
                    foreach (var sprint in JiraJson.Array(f, sprintField!.Id).Where(static s => s.ValueKind == JsonValueKind.Object))
                    {
                        var row = Head();
                        row["sprint_id"] = JiraJson.String(sprint, "id");
                        row["sprint_name"] = JiraJson.String(sprint, "name");
                        row["sprint_state"] = JiraJson.String(sprint, "state");
                        row["board_id"] = JiraJson.String(sprint, "boardId");
                        Add(row);
                    }

                    break;
            }
        }

        return rows;
    }

    // ---- Links ----------------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadIssueLinksAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var search = await SearchAsync(context, LinksSpec, ["project", "status", "updated", "issuelinks"], cancellationToken).ConfigureAwait(false);
        var rows = new Dictionary<string, Row>(StringComparer.Ordinal);

        foreach (var issue in search.Issues)
        {
            var f = JiraJson.Object(issue, "fields");
            var holder = (
                Id: JiraJson.String(issue, "id"),
                Key: JiraJson.String(issue, "key"),
                Category: JiraJson.String(JiraJson.Object(JiraJson.Object(f, "status"), "statusCategory"), "key"));

            foreach (var link in JiraJson.Array(f, "issuelinks"))
            {
                var linkId = JiraJson.String(link, "id");
                if (linkId is null || rows.ContainsKey(linkId))
                {
                    continue;
                }

                var type = JiraJson.Object(link, "type");
                var outward = JiraJson.Object(link, "outwardIssue");
                var inward = JiraJson.Object(link, "inwardIssue");
                var other = outward ?? inward;
                if (other is null)
                {
                    continue;
                }

                var otherSide = (
                    Id: JiraJson.String(other, "id"),
                    Key: JiraJson.String(other, "key"),
                    Category: JiraJson.String(JiraJson.Object(JiraJson.Object(JiraJson.Object(other, "fields"), "status"), "statusCategory"), "key"));

                // Always the outward reading: on the issue holding an outwardIssue that issue is the source; on the
                // one holding an inwardIssue, the other one is.
                var (source, target) = outward is not null ? (holder, otherSide) : (otherSide, holder);

                rows[linkId] = Stamp(context, new Row
                {
                    ["link_id"] = linkId,
                    ["source_issue_id"] = source.Id,
                    ["source_issue_key"] = source.Key,
                    ["source_project_key"] = Jql.ProjectKeyOf(source.Key),
                    ["source_status_category_key"] = source.Category,
                    ["target_issue_id"] = target.Id,
                    ["target_issue_key"] = target.Key,
                    ["target_project_key"] = Jql.ProjectKeyOf(target.Key),
                    ["target_status_category_key"] = target.Category,
                    ["link_type_id"] = JiraJson.String(type, "id"),
                    ["link_type_name"] = JiraJson.String(type, "name"),
                    ["inward_description"] = JiraJson.String(type, "inward"),
                    ["outward_description"] = JiraJson.String(type, "outward")
                }, null, search.FetchedAt);
            }
        }

        return [.. rows.Values];
    }

    // ---- Changelog ------------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadChangelogAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var search = await SearchAsync(context, ChangelogSpec, ["project"], cancellationToken).ConfigureAwait(false);
        var issues = IssueIndex(search.Issues);
        if (issues.Count == 0)
        {
            return [];
        }

        // Up to ten field ids narrow the read itself (bulk fetch's fieldIds); more are filtered in memory.
        var fieldIds = Keys(context.Query, "field_id");
        var narrowFields = fieldIds.Count is > 0 and <= 10 ? fieldIds.Order(StringComparer.Ordinal).ToList() : [];

        var histories = await client.CachedAsync(
            context.Site,
            $"changelog|{search.Jql}|{string.Join(',', narrowFields)}",
            reference: false,
            async ct =>
            {
                var chunks = await FanOutAsync(issues.Keys.Chunk(1000), chunk => ReadChangelogChunkAsync(context.Site, chunk, narrowFields, ct))
                    .ConfigureAwait(false);
                return new ChangelogRead([.. chunks.SelectMany(static chunk => chunk)], DateTime.UtcNow);
            },
            cancellationToken).ConfigureAwait(false);

        var rows = new List<Row>();
        foreach (var (issueId, history) in histories.Histories)
        {
            issues.TryGetValue(issueId, out var issue);
            var historyId = JiraJson.String(history, "id");
            var author = JiraJson.Object(history, "author");
            var changedAt = JiraJson.Timestamp(history, "created");
            var index = 0;

            foreach (var item in JiraJson.Array(history, "items"))
            {
                rows.Add(Stamp(context, ChangeRow(issueId, issue, historyId, index++, author, changedAt, item), changedAt, histories.FetchedAt));
            }
        }

        return rows;
    }

    private sealed record ChangelogRead(IReadOnlyList<(string IssueId, JsonElement History)> Histories, DateTime FetchedAt);

    private async Task<List<(string IssueId, JsonElement History)>> ReadChangelogChunkAsync(
        JiraSite site,
        IReadOnlyList<string> issueIds,
        IReadOnlyList<string> fieldIds,
        CancellationToken cancellationToken)
    {
        var result = new List<(string, JsonElement)>();
        string? nextPageToken = null;

        do
        {
            var body = new Dictionary<string, object?>
            {
                ["issueIdsOrKeys"] = issueIds,
                ["maxResults"] = 1000
            };

            if (fieldIds.Count > 0)
            {
                body["fieldIds"] = fieldIds;
            }

            if (nextPageToken is not null)
            {
                body["nextPageToken"] = nextPageToken;
            }

            var response = await client.PostAsync(site, "rest/api/3/changelog/bulkfetch", body, cancellationToken).ConfigureAwait(false);

            foreach (var issue in JiraJson.Array(response.Root, "issueChangeLogs"))
            {
                if (JiraJson.String(issue, "issueId") is { } issueId)
                {
                    result.AddRange(JiraJson.Array(issue, "changeHistories").Select(history => (issueId, history)));
                }
            }

            nextPageToken = JiraJson.String(response.Root, "nextPageToken");
        }
        while (nextPageToken is not null);

        return result;
    }

    /// <summary>One changed field of one change, as a row.</summary>
    internal static Row ChangeRow(string issueId, IssueRef? reference, string? historyId, int index, JsonElement? author, DateTime? changedAt, JsonElement item)
    {
        return new Row
        {
            ["change_item_id"] = historyId is null ? null : $"{historyId}:{index.ToString(CultureInfo.InvariantCulture)}",
            ["changelog_id"] = historyId,
            ["issue_id"] = issueId,
            ["issue_key"] = reference?.Key,
            ["project_id"] = reference?.ProjectId,
            ["project_key"] = reference?.ProjectKey,
            ["changed_at"] = changedAt,
            ["author_account_id"] = JiraJson.String(author, "accountId"),
            ["author_name"] = JiraJson.String(author, "displayName"),
            ["field_id"] = JiraJson.String(item, "fieldId"),
            ["field_name"] = JiraJson.String(item, "field"),
            ["field_type"] = JiraJson.String(item, "fieldtype"),
            ["from_value"] = JiraJson.String(item, "from"),
            ["from_display_value"] = JiraJson.String(item, "fromString"),
            ["to_value"] = JiraJson.String(item, "to"),
            ["to_display_value"] = JiraJson.String(item, "toString")
        };
    }

    // ---- Worklogs -------------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadWorklogsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var search = await SearchAsync(context, WorklogSpec, ["project"], cancellationToken).ConfigureAwait(false);
        var issues = IssueIndex(search.Issues);
        if (issues.Count == 0)
        {
            return [];
        }

        // No implicit window: without a bound on started_at every worklog of the issues in scope is read.
        var (from, to) = Window(context.Query, "started_at");

        // The same day of slack on each side as the JQL bounds; the in-memory pass cuts exactly.
        long? startedAfter = from is { } lower ? UnixMilliseconds(lower.AddDays(-1)) : null;
        long? startedBefore = to is { } upper ? UnixMilliseconds(upper.AddDays(2)) : null;

        List<JsonElement> worklogs;
        DateTime fetchedAt;

        if (issues.Count <= Options.WorklogPerIssueMaxIssues)
        {
            RequireFanout(issues.Count, "issues");

            var query = new List<KeyValuePair<string, string>>();
            if (startedAfter is { } after)
            {
                query.Add(Param("startedAfter", after.ToString(CultureInfo.InvariantCulture)));
            }

            if (startedBefore is { } before)
            {
                query.Add(Param("startedBefore", before.ToString(CultureInfo.InvariantCulture)));
            }

            var perIssue = await FanOutAsync(issues.Keys, id => client.GetPagedAsync(
                context.Site, $"rest/api/3/issue/{id}/worklog", query, "worklogs", 5000, reference: false, cancellationToken)).ConfigureAwait(false);

            worklogs = [.. perIssue.SelectMany(static collection => collection.Items)];
            fetchedAt = perIssue.Count > 0 ? perIssue.Min(static collection => collection.FetchedAt) : DateTime.UtcNow;
        }
        else
        {
            // Without a lower bound this is every worklog of the site (since = 0) — refused past the cap rather than
            // truncated, see ReadWorklogsUpdatedSinceAsync.
            var bulk = await ReadWorklogsUpdatedSinceAsync(context.Site, startedAfter ?? 0, cancellationToken).ConfigureAwait(false);
            worklogs = [.. bulk.Items.Where(worklog =>
                JiraJson.String(worklog, "issueId") is { } issueId && issues.ContainsKey(issueId)
                && JiraJson.Timestamp(worklog, "started") is { } started
                && (startedAfter is null || new DateTimeOffset(started).ToUnixTimeMilliseconds() >= startedAfter)
                && (startedBefore is null || new DateTimeOffset(started).ToUnixTimeMilliseconds() < startedBefore))];
            fetchedAt = bulk.FetchedAt;
        }

        return
        [
            .. worklogs.DistinctBy(static worklog => JiraJson.String(worklog, "id")).Select(worklog =>
            {
                var issueId = JiraJson.String(worklog, "issueId");
                var issue = issueId is not null && issues.TryGetValue(issueId, out var found) ? found : null;
                var author = JiraJson.Object(worklog, "author");
                var seconds = JiraJson.Long(worklog, "timeSpentSeconds");
                var updated = JiraJson.Timestamp(worklog, "updated");

                return Stamp(context, new Row
                {
                    ["worklog_id"] = JiraJson.String(worklog, "id"),
                    ["issue_id"] = issueId,
                    ["issue_key"] = issue?.Key,
                    ["project_id"] = issue?.ProjectId,
                    ["project_key"] = issue?.ProjectKey,
                    ["author_account_id"] = JiraJson.String(author, "accountId"),
                    ["author_name"] = JiraJson.String(author, "displayName"),
                    ["update_author_account_id"] = JiraJson.String(JiraJson.Object(worklog, "updateAuthor"), "accountId"),
                    ["started_at"] = JiraJson.Timestamp(worklog, "started"),
                    ["created_at"] = JiraJson.Timestamp(worklog, "created"),
                    ["updated_at"] = updated,
                    ["time_spent_seconds"] = seconds,
                    ["time_spent_hours"] = seconds is { } s ? Math.Round(s / 3600m, 4) : null,
                    ["comment"] = AdfText.ToText(JiraJson.Property(worklog, "comment"))
                }, updated, fetchedAt);
            })
        ];
    }

    /// <summary>
    /// Every worklog of the site updated since <paramref name="sinceMilliseconds"/>: the ids from
    /// <c>GET /worklog/updated</c> (1000 a page, oldest first, the next page starting at the previous one's
    /// <c>until</c>), then the worklogs themselves from <c>POST /worklog/list</c>, 1000 ids a call. Cached per day of
    /// <c>since</c>, so every scope and filter over the same window shares one read.
    /// </summary>
    /// <remarks>
    /// A worklog started inside the window but last updated before it — logged ahead for a future date — is not
    /// among these; Jira also leaves out worklogs updated in the last minute.
    /// </remarks>
    private Task<JiraCollection> ReadWorklogsUpdatedSinceAsync(JiraSite site, long sinceMilliseconds, CancellationToken cancellationToken) =>
        client.CachedAsync(site, $"worklogs-updated|{sinceMilliseconds}", reference: false, async ct =>
        {
            var ids = new List<long>();
            var since = sinceMilliseconds;
            DateTime? fetchedAt = null;

            while (true)
            {
                var page = await client.GetAsync(
                    site, "rest/api/3/worklog/updated", [Param("since", since.ToString(CultureInfo.InvariantCulture))], reference: false, ct)
                    .ConfigureAwait(false);
                fetchedAt ??= page.FetchedAt;

                ids.AddRange(JiraJson.Array(page.Root, "values").Select(static value => JiraJson.Long(value, "worklogId")).OfType<long>());
                if (ids.Count > Options.MaxIssuesPerRead * 10)
                {
                    throw new JiraRequestException(
                        "More worklogs were updated in this window than this deployment reads at once. Narrow the started_at range.");
                }

                var until = JiraJson.Long(page.Root, "until");
                if (JiraJson.Boolean(page.Root, "lastPage") != false || until is not { } next || next <= since)
                {
                    break;
                }

                since = next;
            }

            var lists = await FanOutAsync(ids.Distinct().Chunk(1000), chunk =>
                client.PostAsync(site, "rest/api/3/worklog/list", new Dictionary<string, object?> { ["ids"] = chunk }, ct)).ConfigureAwait(false);

            return new JiraCollection(
                [.. lists.SelectMany(static list => list.Root.ValueKind == JsonValueKind.Array ? list.Root.EnumerateArray().ToList() : [])],
                fetchedAt ?? DateTime.UtcNow);
        }, cancellationToken);

    // ---- Comments -------------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadCommentsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var search = await SearchAsync(context, CommentSpec, ["project"], cancellationToken).ConfigureAwait(false);
        var issues = IssueIndex(search.Issues);

        if (issues.Count > Options.MaxCommentIssues)
        {
            throw new JiraRequestException(
                $"Reading comments would visit {issues.Count} issues, one call each (Jira:MaxCommentIssues is {Options.MaxCommentIssues}). Narrow the filter — a project, an issue or a recent created_at.");
        }

        var perIssue = await FanOutAsync(issues.Values, async issue => (Issue: issue, Comments: await client.GetPagedAsync(
            context.Site, $"rest/api/3/issue/{issue.Id}/comment", null, "comments", 100, reference: false, cancellationToken).ConfigureAwait(false)))
            .ConfigureAwait(false);

        return
        [
            .. perIssue.SelectMany(entry => entry.Comments.Items.Select(comment =>
            {
                var author = JiraJson.Object(comment, "author");
                var visibility = JiraJson.Object(comment, "visibility");
                var body = AdfText.ToText(JiraJson.Property(comment, "body"));
                var updated = JiraJson.Timestamp(comment, "updated");

                return Stamp(context, new Row
                {
                    ["comment_id"] = JiraJson.String(comment, "id"),
                    ["issue_id"] = entry.Issue.Id,
                    ["issue_key"] = entry.Issue.Key,
                    ["project_id"] = entry.Issue.ProjectId,
                    ["project_key"] = entry.Issue.ProjectKey,
                    ["author_account_id"] = JiraJson.String(author, "accountId"),
                    ["author_name"] = JiraJson.String(author, "displayName"),
                    ["update_author_account_id"] = JiraJson.String(JiraJson.Object(comment, "updateAuthor"), "accountId"),
                    ["created_at"] = JiraJson.Timestamp(comment, "created"),
                    ["updated_at"] = updated,
                    ["body"] = body,
                    ["body_length"] = body is null ? 0L : (long)body.Length,
                    ["is_public"] = JiraJson.Boolean(comment, "jsdPublic"),
                    ["visibility_type"] = JiraJson.String(visibility, "type"),
                    ["visibility_value"] = JiraJson.String(visibility, "value")
                }, updated, entry.Comments.FetchedAt);
            }))
        ];
    }

    // ---- Helpers --------------------------------------------------------------------------------------------

    private static Dictionary<string, IssueRef> IssueIndex(IEnumerable<JsonElement> issues)
    {
        var index = new Dictionary<string, IssueRef>(StringComparer.Ordinal);
        foreach (var issue in issues)
        {
            if (JiraJson.String(issue, "id") is not { } id)
            {
                continue;
            }

            var project = JiraJson.Object(JiraJson.Object(issue, "fields"), "project");
            index[id] = new IssueRef(id, JiraJson.String(issue, "key"), JiraJson.String(project, "id"), JiraJson.String(project, "key"));
        }

        return index;
    }

    private static long UnixMilliseconds(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds();
}
