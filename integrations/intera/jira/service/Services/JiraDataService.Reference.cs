using Intera.Jira.Api.Clients;
using Intera.Jira.Api.Infrastructure;
using System.Text.Json;

namespace Intera.Jira.Api.Services;

using Row = Dictionary<string, object?>;

/// <summary>
/// The reference sources — projects, users, fields, issue types, statuses, priorities, versions, components and
/// custom field options. Each is one call or one call per project / field, cached for
/// <c>Jira:ReferenceCacheSeconds</c>, and answers a lookup's <c>IN [...]</c> cheaply.
/// </summary>
public sealed partial class JiraDataService
{
    private const int UserPageSize = 1000;

    // ---- Projects -------------------------------------------------------------------------------------------

    /// <summary>Every project of the site, archived included (the project search caps a page at 100).</summary>
    private Task<JiraCollection> ReadProjectsAsync(JiraSite site, CancellationToken cancellationToken) =>
        client.GetPagedAsync(
            site,
            "rest/api/3/project/search",
            [Param("expand", "description,lead,insight"), Param("status", "live"), Param("status", "archived")],
            "values",
            pageSize: 100,
            reference: true,
            cancellationToken);

    private async Task<List<Row>> LoadProjectsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var projects = await ReadProjectsAsync(context.Site, cancellationToken).ConfigureAwait(false);

        return
        [
            .. projects.Items.Select(p =>
            {
                var category = JiraJson.Object(p, "projectCategory");
                var lead = JiraJson.Object(p, "lead");
                var insight = JiraJson.Object(p, "insight");
                var key = JiraJson.String(p, "key");

                return Stamp(context, new Row
                {
                    ["project_id"] = JiraJson.String(p, "id"),
                    ["project_key"] = key,
                    ["project_name"] = JiraJson.String(p, "name"),
                    ["project_type_key"] = JiraJson.String(p, "projectTypeKey"),
                    ["project_style"] = JiraJson.String(p, "style"),
                    ["project_category_id"] = JiraJson.String(category, "id"),
                    ["project_category_name"] = JiraJson.String(category, "name"),
                    ["lead_account_id"] = JiraJson.String(lead, "accountId"),
                    ["lead_name"] = JiraJson.String(lead, "displayName"),
                    ["description"] = JiraJson.String(p, "description"),
                    ["url"] = key is null ? null : $"{context.Site.Url}/browse/{key}",
                    ["archived"] = JiraJson.Boolean(p, "archived") ?? false,
                    ["simplified"] = JiraJson.Boolean(p, "simplified"),
                    ["is_private"] = JiraJson.Boolean(p, "isPrivate"),
                    ["issue_count"] = JiraJson.Long(insight, "totalIssueCount"),
                    ["last_issue_update_at"] = JiraJson.Timestamp(insight, "lastIssueUpdateTime")
                }, null, projects.FetchedAt);
            })
        ];
    }

    /// <summary>
    /// The projects the filter names through <c>project_id</c> / <c>project_key</c> (= / IN), matched against the
    /// site's list. Null when the filter names none; empty when it names only projects that do not exist — JQL
    /// answers an unknown project with an error rather than with no issues, so those never reach it.
    /// </summary>
    private async Task<List<(string Id, string Key)>?> NamedProjectsAsync(
        ReadContext context,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var projects = await ReadProjectsAsync(context.Site, cancellationToken).ConfigureAwait(false);
        var known = projects.Items
            .Select(static p => (Id: JiraJson.String(p, "id"), Key: JiraJson.String(p, "key")))
            .Where(static p => p.Id is not null && p.Key is not null)
            .Select(static p => (Id: p.Id!, Key: p.Key!))
            .ToList();

        return
        [
            .. known.Where(project => values.Any(value =>
                string.Equals(value, project.Id, StringComparison.Ordinal)
                || string.Equals(value, project.Key, StringComparison.OrdinalIgnoreCase)))
        ];
    }

    /// <summary>The projects a per-project read (versions, components) visits: those named, else all of them.</summary>
    private async Task<List<(string Id, string Key)>> ProjectsToVisitAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var named = await NamedProjectsAsync(context, Keys(context.Query, "project_id", "project_key"), cancellationToken).ConfigureAwait(false);
        if (named is not null)
        {
            return named;
        }

        var projects = await ReadProjectsAsync(context.Site, cancellationToken).ConfigureAwait(false);
        return
        [
            .. projects.Items
                .Select(static p => (Id: JiraJson.String(p, "id"), Key: JiraJson.String(p, "key")))
                .Where(static p => p.Id is not null && p.Key is not null)
                .Select(static p => (p.Id!, p.Key!))
        ];
    }

    // ---- Users ----------------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadUsersAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var accountIds = Keys(context.Query, "account_id");
        IReadOnlyList<JsonElement> users;
        DateTime fetchedAt;

        if (accountIds.Count is > 0 and <= 200)
        {
            // A lookup's IN [...]: the bulk read, 50 ids a call, rather than the whole directory.
            var chunks = await FanOutAsync(accountIds.Chunk(50), chunk => client.GetPagedAsync(
                context.Site,
                "rest/api/3/user/bulk",
                [.. chunk.Select(id => Param("accountId", id))],
                "values",
                pageSize: 50,
                reference: true,
                cancellationToken)).ConfigureAwait(false);

            users = [.. chunks.SelectMany(static chunk => chunk.Items)];
            fetchedAt = chunks.Count > 0 ? chunks.Min(static chunk => chunk.FetchedAt) : DateTime.UtcNow;
        }
        else
        {
            var all = await client.GetPagedAsync(context.Site, "rest/api/3/users/search", null, null, UserPageSize, reference: true, cancellationToken)
                .ConfigureAwait(false);
            users = all.Items;
            fetchedAt = all.FetchedAt;
        }

        return
        [
            .. users
                .DistinctBy(static user => JiraJson.String(user, "accountId"))
                .Select(user => Stamp(context, new Row
                {
                    ["account_id"] = JiraJson.String(user, "accountId"),
                    ["display_name"] = JiraJson.String(user, "displayName"),
                    ["email"] = JiraJson.String(user, "emailAddress"),
                    ["active"] = JiraJson.Boolean(user, "active"),
                    ["account_type"] = JiraJson.String(user, "accountType"),
                    ["timezone"] = JiraJson.String(user, "timeZone"),
                    ["locale"] = JiraJson.String(user, "locale")
                }, null, fetchedAt))
        ];
    }

    // ---- Fields, issue types, statuses, priorities ----------------------------------------------------------

    private async Task<List<Row>> LoadFieldsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var catalog = await ReadFieldCatalogAsync(context.Site, cancellationToken).ConfigureAwait(false);
        var exposed = ExposedCustomFields(catalog).Select(static field => field.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. catalog.Fields.Select(field => Stamp(context, new Row
            {
                ["field_id"] = field.Id,
                ["field_key"] = field.Key,
                ["field_name"] = field.Name,
                ["custom"] = field.Custom,
                ["field_type"] = field.SchemaType,
                ["items_type"] = field.ItemsType,
                ["searchable"] = field.Searchable,
                ["navigable"] = field.Navigable,
                ["orderable"] = field.Orderable,
                ["custom_type"] = field.CustomType,
                ["custom_id"] = field.CustomId,
                ["clause_names"] = JiraJson.Join(field.ClauseNames),
                ["issue_column"] = exposed.Contains(field.Id) ? field.Id.ToLowerInvariant() : null
            }, null, catalog.FetchedAt))
        ];
    }

    private async Task<List<Row>> LoadIssueTypesAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(context.Site, "rest/api/3/issuetype", null, reference: true, cancellationToken).ConfigureAwait(false);

        return
        [
            .. Elements(response.Root).Select(type =>
            {
                var scope = JiraJson.Object(type, "scope");
                return Stamp(context, new Row
                {
                    ["issue_type_id"] = JiraJson.String(type, "id"),
                    ["name"] = JiraJson.String(type, "name"),
                    ["description"] = JiraJson.String(type, "description"),
                    ["subtask"] = JiraJson.Boolean(type, "subtask"),
                    ["hierarchy_level"] = JiraJson.Long(type, "hierarchyLevel"),
                    ["scope_type"] = JiraJson.String(scope, "type"),
                    ["scope_project_id"] = JiraJson.String(JiraJson.Object(scope, "project"), "id")
                }, null, response.FetchedAt);
            })
        ];
    }

    private async Task<List<Row>> LoadStatusesAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(context.Site, "rest/api/3/status", null, reference: true, cancellationToken).ConfigureAwait(false);

        return
        [
            .. Elements(response.Root).Select(status =>
            {
                var category = JiraJson.Object(status, "statusCategory");
                var scope = JiraJson.Object(status, "scope");
                return Stamp(context, new Row
                {
                    ["status_id"] = JiraJson.String(status, "id"),
                    ["name"] = JiraJson.String(status, "name"),
                    ["description"] = JiraJson.String(status, "description"),
                    ["status_category_id"] = JiraJson.String(category, "id"),
                    ["status_category_key"] = JiraJson.String(category, "key"),
                    ["status_category_name"] = JiraJson.String(category, "name"),
                    ["scope_type"] = JiraJson.String(scope, "type"),
                    ["scope_project_id"] = JiraJson.String(JiraJson.Object(scope, "project"), "id")
                }, null, response.FetchedAt);
            })
        ];
    }

    /// <remarks>
    /// <c>GET /rest/api/3/priority</c> is marked deprecated in favour of <c>/priority/search</c>, but the latter needs
    /// the admin scope <c>manage:jira-configuration</c>, which a read-only integration should not ask for.
    /// </remarks>
    private async Task<List<Row>> LoadPrioritiesAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(context.Site, "rest/api/3/priority", null, reference: true, cancellationToken).ConfigureAwait(false);

        return
        [
            .. Elements(response.Root).Select(priority => Stamp(context, new Row
            {
                ["priority_id"] = JiraJson.String(priority, "id"),
                ["name"] = JiraJson.String(priority, "name"),
                ["description"] = JiraJson.String(priority, "description"),
                ["status_color"] = JiraJson.String(priority, "statusColor"),
                ["is_default"] = JiraJson.Boolean(priority, "isDefault")
            }, null, response.FetchedAt))
        ];
    }

    // ---- Versions and components ----------------------------------------------------------------------------

    private async Task<List<Row>> LoadVersionsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var projects = await ProjectsToVisitAsync(context, cancellationToken).ConfigureAwait(false);
        var projectKeys = projects.ToDictionary(static p => p.Id, static p => p.Key, StringComparer.Ordinal);
        var versionIds = Keys(context.Query, "version_id").Where(Jql.IsNumericId).ToList();

        List<(JsonElement Version, DateTime FetchedAt)> versions;
        if (versionIds.Count is > 0 and <= 50 && Names(context.Query, "version_id"))
        {
            // A lookup's IN [...]: one call per version rather than one per project.
            var read = await FanOutAsync(versionIds, id => TryGetAsync(context.Site, $"rest/api/3/version/{id}", cancellationToken)).ConfigureAwait(false);
            versions = [.. read.OfType<JiraResponse>().Select(static response => (response.Root, response.FetchedAt))];
        }
        else
        {
            RequireFanout(projects.Count, "projects");
            var read = await FanOutAsync(projects, project => client.GetPagedAsync(
                context.Site, $"rest/api/3/project/{project.Id}/version", null, "values", Options.PageSize, reference: true, cancellationToken)).ConfigureAwait(false);
            versions = [.. read.SelectMany(static collection => collection.Items.Select(item => (item, collection.FetchedAt)))];
        }

        return
        [
            .. versions.Select(entry =>
            {
                var v = entry.Version;
                var projectId = JiraJson.String(v, "projectId");
                return Stamp(context, new Row
                {
                    ["version_id"] = JiraJson.String(v, "id"),
                    ["project_id"] = projectId,
                    ["project_key"] = projectId is not null && projectKeys.TryGetValue(projectId, out var key) ? key : null,
                    ["name"] = JiraJson.String(v, "name"),
                    ["description"] = JiraJson.String(v, "description"),
                    ["start_date"] = JiraJson.Date(v, "startDate"),
                    ["release_date"] = JiraJson.Date(v, "releaseDate"),
                    ["released"] = JiraJson.Boolean(v, "released"),
                    ["archived"] = JiraJson.Boolean(v, "archived"),
                    ["overdue"] = JiraJson.Boolean(v, "overdue") ?? false
                }, null, entry.FetchedAt);
            })
        ];
    }

    private async Task<List<Row>> LoadComponentsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var projects = await ProjectsToVisitAsync(context, cancellationToken).ConfigureAwait(false);
        var componentIds = Keys(context.Query, "component_id").Where(Jql.IsNumericId).ToList();

        // (component, the project's id and key as far as known, fetched at)
        List<(JsonElement Component, string? ProjectId, string? ProjectKey, DateTime FetchedAt)> components;
        if (componentIds.Count is > 0 and <= 50 && Names(context.Query, "component_id"))
        {
            var allProjects = await ReadProjectsAsync(context.Site, cancellationToken).ConfigureAwait(false);
            var idsByKey = allProjects.Items
                .Where(static p => JiraJson.String(p, "key") is not null)
                .ToDictionary(static p => JiraJson.String(p, "key")!, static p => JiraJson.String(p, "id"), StringComparer.OrdinalIgnoreCase);

            var read = await FanOutAsync(componentIds, id => TryGetAsync(context.Site, $"rest/api/3/component/{id}", cancellationToken)).ConfigureAwait(false);
            components =
            [
                .. read.OfType<JiraResponse>().Select(response =>
                {
                    var key = JiraJson.String(response.Root, "project");
                    return (response.Root, key is not null && idsByKey.TryGetValue(key, out var id) ? id : JiraJson.String(response.Root, "projectId"), key, response.FetchedAt);
                })
            ];
        }
        else
        {
            RequireFanout(projects.Count, "projects");
            var read = await FanOutAsync(projects, async project => (Project: project, Collection: await client.GetPagedAsync(
                context.Site, $"rest/api/3/project/{project.Id}/component", null, "values", Options.PageSize, reference: true, cancellationToken).ConfigureAwait(false)))
                .ConfigureAwait(false);
            components =
            [
                .. read.SelectMany(static entry => entry.Collection.Items.Select(item =>
                    (item, (string?)entry.Project.Id, (string?)entry.Project.Key, entry.Collection.FetchedAt)))
            ];
        }

        return
        [
            .. components.Select(entry =>
            {
                var c = entry.Component;
                var lead = JiraJson.Object(c, "lead");
                return Stamp(context, new Row
                {
                    ["component_id"] = JiraJson.String(c, "id"),
                    ["project_id"] = entry.ProjectId,
                    ["project_key"] = entry.ProjectKey ?? JiraJson.String(c, "project"),
                    ["component_name"] = JiraJson.String(c, "name"),
                    ["description"] = JiraJson.String(c, "description"),
                    ["lead_account_id"] = JiraJson.String(lead, "accountId"),
                    ["lead_name"] = JiraJson.String(lead, "displayName"),
                    ["assignee_type"] = JiraJson.String(c, "assigneeType"),
                    ["issue_count"] = JiraJson.Long(c, "issueCount")
                }, null, entry.FetchedAt);
            })
        ];
    }

    // ---- Custom field options -------------------------------------------------------------------------------

    private async Task<List<Row>> LoadCustomFieldOptionsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var catalog = await ReadFieldCatalogAsync(context.Site, cancellationToken).ConfigureAwait(false);
        var named = Keys(context.Query, "field_id");

        var fields = catalog.Fields
            .Where(static field => field.Custom && (field.SchemaType is "option" or "option-with-child" || field.ItemsType == "option"))
            .Where(field => named.Count == 0 || named.Contains(field.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
        RequireFanout(fields.Count, "select-list custom fields");

        // The context and option endpoints answer only a Jira administrator (Administer Jira) holding the granular field
        // scopes; a refusal is reported as exactly that, since there is nothing else to fix.
        try
        {
            return await ReadCustomFieldOptionsAsync(context, fields, cancellationToken).ConfigureAwait(false);
        }
        catch (JiraAuthorizationException exception)
        {
            throw new JiraAuthorizationException(
                "Custom field options can only be read when the user who authorised this deployment is a Jira administrator "
                + "(the Administer Jira global permission) and the grant holds read:field:jira, read:field.option:jira and "
                + "read:custom-field-contextual-configuration:jira. Authorise again as an administrator (GET /oauth/authorize-url), "
                + $"or do not use this source. Jira answered: {exception.Message}");
        }
    }

    private async Task<List<Row>> ReadCustomFieldOptionsAsync(ReadContext context, List<JiraFieldInfo> fields, CancellationToken cancellationToken)
    {
        var perField = await FanOutAsync(fields, async field =>
        {
            JiraCollection contexts;
            try
            {
                contexts = await client.GetPagedAsync(
                    context.Site, $"rest/api/3/field/{Uri.EscapeDataString(field.Id)}/context", null, "values", 50, reference: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JiraRequestException exception)
            {
                // A field of a team-managed space is listed by /field but has no contexts the API serves: Jira answers
                // "The custom field was not found" (live site, 2026-10-07). One such field must not fail the source.
                logger.LogInformation("Skipping the options of custom field {FieldId}: {Message}", field.Id, exception.Message);
                return [];
            }

            var perContext = await FanOutAsync(contexts.Items, async fieldContext =>
            {
                var contextId = JiraJson.String(fieldContext, "id");
                var optionsRead = await client.GetPagedAsync(
                    context.Site,
                    $"rest/api/3/field/{Uri.EscapeDataString(field.Id)}/context/{Uri.EscapeDataString(contextId ?? string.Empty)}/option",
                    null,
                    "values",
                    100,
                    reference: true,
                    cancellationToken).ConfigureAwait(false);

                return optionsRead.Items.Select(option => Stamp(context, new Row
                {
                    ["field_id"] = field.Id,
                    ["field_name"] = field.Name,
                    ["context_id"] = contextId,
                    ["context_name"] = JiraJson.String(fieldContext, "name"),
                    ["option_id"] = JiraJson.String(option, "id"),
                    ["option_value"] = JiraJson.String(option, "value"),
                    ["parent_option_id"] = JiraJson.String(option, "optionId"),
                    ["disabled"] = JiraJson.Boolean(option, "disabled") ?? false
                }, null, optionsRead.FetchedAt)).ToList();
            }).ConfigureAwait(false);

            return perContext.SelectMany(static rows => rows).ToList();
        }).ConfigureAwait(false);

        return [.. perField.SelectMany(static rows => rows)];
    }

    // ---- Helpers --------------------------------------------------------------------------------------------

    private static IEnumerable<JsonElement> Elements(JsonElement root) =>
        root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : [];

    /// <summary>A GET whose 404 (an id that does not exist, or is not visible) is no row rather than a failure.</summary>
    private async Task<JiraResponse?> TryGetAsync(JiraSite site, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetAsync(site, path, null, reference: true, cancellationToken).ConfigureAwait(false);
        }
        catch (JiraRequestException exception)
        {
            logger.LogDebug(exception, "Jira refused {Path}; read as no row.", path);
            return null;
        }
    }
}
