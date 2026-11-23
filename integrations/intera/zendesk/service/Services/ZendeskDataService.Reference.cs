using Intera.Zendesk.Api.Clients;
using Intera.Zendesk.Api.Infrastructure;
using System.Globalization;

namespace Intera.Zendesk.Api.Services;

using Row = Dictionary<string, object?>;

/// <summary>
/// The sources that are plain lists or small catalogs — users, organizations, groups and their memberships,
/// satisfaction ratings, SLA policies, ticket fields and forms. Each is one paged read, cached for
/// <c>Zendesk:ReferenceCacheSeconds</c> where it is reference data, and users and organizations answer a lookup's
/// <c>IN [...]</c> through <c>show_many</c> instead of reading the whole directory.
/// </summary>
public sealed partial class ZendeskDataService
{
    private static readonly string[] Roles = ["end-user", "agent", "admin"];

    private async Task<List<Row>> LoadUsersAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var ids = NumericIds(Keys(context.Query, "user_id"));
        ZendeskCollection users;

        if (ids.Count > 0)
        {
            users = await ShowManyAsync("api/v2/users/show_many", "users", ids, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // role = / IN goes to the endpoint; a value Zendesk does not know would be refused, so only real roles are sent.
            var roles = Keys(context.Query, "role").Where(role => Roles.Contains(role, StringComparer.OrdinalIgnoreCase)).Select(static role => role.ToLowerInvariant()).ToList();
            users = await client.GetPagedAsync(
                "api/v2/users",
                [.. roles.Select(role => Param("role[]", role))],
                "users",
                PageSize,
                reference: true,
                cancellationToken).ConfigureAwait(false);
        }

        return
        [
            .. users.Items.DistinctBy(static user => ZendeskJson.String(user, "id")).Select(u => Stamp(context, new Row
            {
                ["user_id"] = ZendeskJson.String(u, "id"),
                ["external_id"] = ZendeskJson.String(u, "external_id"),
                ["name"] = ZendeskJson.String(u, "name"),
                ["email"] = ZendeskJson.String(u, "email"),
                ["phone"] = ZendeskJson.String(u, "phone"),
                ["role"] = ZendeskJson.String(u, "role"),
                ["organization_id"] = ZendeskJson.String(u, "organization_id"),
                ["default_group_id"] = ZendeskJson.String(u, "default_group_id"),
                ["active"] = ZendeskJson.Boolean(u, "active"),
                ["suspended"] = ZendeskJson.Boolean(u, "suspended"),
                ["verified"] = ZendeskJson.Boolean(u, "verified"),
                ["time_zone"] = ZendeskJson.String(u, "time_zone"),
                ["locale"] = ZendeskJson.String(u, "locale"),
                ["last_login_at"] = ZendeskJson.Timestamp(u, "last_login_at"),
                ["created_at"] = ZendeskJson.Timestamp(u, "created_at"),
                ["updated_at"] = ZendeskJson.Timestamp(u, "updated_at"),
                ["tags"] = ZendeskJson.JoinArray(u, "tags"),
                ["user_fields"] = ZendeskJson.ObjectText(u, "user_fields")
            }, ZendeskJson.Timestamp(u, "updated_at"), users.FetchedAt))
        ];
    }

    private async Task<List<Row>> LoadOrganizationsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var ids = NumericIds(Keys(context.Query, "organization_id"));
        var organizations = ids.Count > 0
            ? await ShowManyAsync("api/v2/organizations/show_many", "organizations", ids, cancellationToken).ConfigureAwait(false)
            : await client.GetPagedAsync("api/v2/organizations", null, "organizations", PageSize, reference: true, cancellationToken).ConfigureAwait(false);

        return
        [
            .. organizations.Items.DistinctBy(static organization => ZendeskJson.String(organization, "id")).Select(o => Stamp(context, new Row
            {
                ["organization_id"] = ZendeskJson.String(o, "id"),
                ["external_id"] = ZendeskJson.String(o, "external_id"),
                ["name"] = ZendeskJson.String(o, "name"),
                ["domain_names"] = ZendeskJson.JoinArray(o, "domain_names"),
                ["group_id"] = ZendeskJson.String(o, "group_id"),
                ["details"] = ZendeskJson.String(o, "details"),
                ["tags"] = ZendeskJson.JoinArray(o, "tags"),
                ["ticket_restriction"] = ZendeskJson.String(o, "ticket_restriction"),
                ["created_at"] = ZendeskJson.Timestamp(o, "created_at"),
                ["updated_at"] = ZendeskJson.Timestamp(o, "updated_at"),
                ["organization_fields"] = ZendeskJson.ObjectText(o, "organization_fields")
            }, ZendeskJson.Timestamp(o, "updated_at"), organizations.FetchedAt))
        ];
    }

    private async Task<List<Row>> LoadGroupsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var groups = await client.GetPagedAsync("api/v2/groups", null, "groups", PageSize, reference: true, cancellationToken).ConfigureAwait(false);

        return
        [
            .. groups.Items.Select(g => Stamp(context, new Row
            {
                ["group_id"] = ZendeskJson.String(g, "id"),
                ["name"] = ZendeskJson.String(g, "name"),
                ["description"] = ZendeskJson.String(g, "description"),
                ["is_default"] = ZendeskJson.Boolean(g, "default"),
                ["deleted"] = ZendeskJson.Boolean(g, "deleted") ?? false,
                ["is_public"] = ZendeskJson.Boolean(g, "is_public"),
                ["created_at"] = ZendeskJson.Timestamp(g, "created_at"),
                ["updated_at"] = ZendeskJson.Timestamp(g, "updated_at")
            }, ZendeskJson.Timestamp(g, "updated_at"), groups.FetchedAt))
        ];
    }

    private async Task<List<Row>> LoadGroupMembershipsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var memberships = await client.GetPagedAsync(
            "api/v2/group_memberships", null, "group_memberships", PageSize, reference: true, cancellationToken).ConfigureAwait(false);

        return
        [
            .. memberships.Items.Select(m => Stamp(context, new Row
            {
                ["membership_id"] = ZendeskJson.String(m, "id"),
                ["user_id"] = ZendeskJson.String(m, "user_id"),
                ["group_id"] = ZendeskJson.String(m, "group_id"),
                ["is_default"] = ZendeskJson.Boolean(m, "default"),
                ["created_at"] = ZendeskJson.Timestamp(m, "created_at"),
                ["updated_at"] = ZendeskJson.Timestamp(m, "updated_at")
            }, ZendeskJson.Timestamp(m, "updated_at"), memberships.FetchedAt))
        ];
    }

    private async Task<List<Row>> LoadSatisfactionRatingsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var (from, to) = Window(context.Query, "created_at");
        var parameters = new List<KeyValuePair<string, string>>();

        if (from is not null)
        {
            parameters.Add(Param("start_time", StartTime(from).ToString(CultureInfo.InvariantCulture)));
        }

        if (to is { } end)
        {
            parameters.Add(Param("end_time", new DateTimeOffset(end.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        }

        var ratings = await client.GetPagedAsync(
            "api/v2/satisfaction_ratings", parameters, "satisfaction_ratings", PageSize, reference: false, cancellationToken).ConfigureAwait(false);

        return
        [
            .. ratings.Items.Select(r => Stamp(context, new Row
            {
                ["rating_id"] = ZendeskJson.String(r, "id"),
                ["ticket_id"] = ZendeskJson.String(r, "ticket_id"),
                ["requester_id"] = ZendeskJson.String(r, "requester_id"),
                ["assignee_id"] = ZendeskJson.String(r, "assignee_id"),
                ["group_id"] = ZendeskJson.String(r, "group_id"),
                ["score"] = ZendeskJson.String(r, "score"),
                ["comment"] = ZendeskJson.String(r, "comment"),
                ["reason"] = ZendeskJson.String(r, "reason"),
                ["reason_id"] = ZendeskJson.String(r, "reason_code") ?? ZendeskJson.String(r, "reason_id"),
                ["created_at"] = ZendeskJson.Timestamp(r, "created_at"),
                ["updated_at"] = ZendeskJson.Timestamp(r, "updated_at")
            }, ZendeskJson.Timestamp(r, "updated_at"), ratings.FetchedAt))
        ];
    }

    /// <summary>One row per policy metric: a policy carries a target for each (metric, priority) pair.</summary>
    private async Task<List<Row>> LoadSlaPoliciesAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync("api/v2/slas/policies", null, reference: true, cancellationToken).ConfigureAwait(false);
        var rows = new List<Row>();

        foreach (var policy in ZendeskJson.Array(response.Root, "sla_policies"))
        {
            foreach (var metric in ZendeskJson.Array(policy, "policy_metrics"))
            {
                var target = ZendeskJson.Decimal(metric, "target");

                rows.Add(Stamp(context, new Row
                {
                    ["policy_id"] = ZendeskJson.String(policy, "id"),
                    ["title"] = ZendeskJson.String(policy, "title"),
                    ["description"] = ZendeskJson.String(policy, "description"),
                    ["position"] = ZendeskJson.Decimal(policy, "position"),
                    ["metric"] = ZendeskJson.String(metric, "metric"),
                    ["priority"] = ZendeskJson.String(metric, "priority"),
                    ["target_minutes"] = target,
                    ["target_seconds"] = target * 60,
                    ["business_hours"] = ZendeskJson.Boolean(metric, "business_hours"),
                    ["created_at"] = ZendeskJson.Timestamp(policy, "created_at"),
                    ["updated_at"] = ZendeskJson.Timestamp(policy, "updated_at")
                }, ZendeskJson.Timestamp(policy, "updated_at"), response.FetchedAt));
            }
        }

        return rows;
    }

    private async Task<List<Row>> LoadTicketFieldsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var fields = await client.GetPagedAsync("api/v2/ticket_fields", null, "ticket_fields", PageSize, reference: true, cancellationToken).ConfigureAwait(false);

        return
        [
            .. fields.Items.Select(f => Stamp(context, new Row
            {
                ["ticket_field_id"] = ZendeskJson.String(f, "id"),
                ["key"] = ZendeskJson.String(f, "key"),
                ["title"] = ZendeskJson.String(f, "title") ?? ZendeskJson.String(f, "raw_title"),
                ["description"] = ZendeskJson.String(f, "description"),
                ["type"] = ZendeskJson.String(f, "type"),
                ["active"] = ZendeskJson.Boolean(f, "active"),
                ["required"] = ZendeskJson.Boolean(f, "required"),
                ["is_system"] = ZendeskJson.Boolean(f, "removable") is { } removable ? !removable : null,
                ["position"] = ZendeskJson.Decimal(f, "position"),
                ["custom_field_options"] = ZendeskJson.Join(ZendeskJson.Array(f, "custom_field_options").Select(static option => ZendeskJson.String(option, "name"))),
                ["created_at"] = ZendeskJson.Timestamp(f, "created_at"),
                ["updated_at"] = ZendeskJson.Timestamp(f, "updated_at")
            }, ZendeskJson.Timestamp(f, "updated_at"), fields.FetchedAt))
        ];
    }

    private async Task<List<Row>> LoadTicketFormsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        // Not paginated: Zendesk answers every form at once.
        var response = await client.GetAsync("api/v2/ticket_forms", null, reference: true, cancellationToken).ConfigureAwait(false);

        return
        [
            .. ZendeskJson.Array(response.Root, "ticket_forms").Select(f => Stamp(context, new Row
            {
                ["ticket_form_id"] = ZendeskJson.String(f, "id"),
                ["name"] = ZendeskJson.String(f, "name") ?? ZendeskJson.String(f, "raw_name"),
                ["display_name"] = ZendeskJson.String(f, "display_name") ?? ZendeskJson.String(f, "raw_display_name"),
                ["active"] = ZendeskJson.Boolean(f, "active"),
                ["is_default"] = ZendeskJson.Boolean(f, "default"),
                ["end_user_visible"] = ZendeskJson.Boolean(f, "end_user_visible"),
                ["ticket_field_ids"] = ZendeskJson.JoinArray(f, "ticket_field_ids"),
                ["created_at"] = ZendeskJson.Timestamp(f, "created_at"),
                ["updated_at"] = ZendeskJson.Timestamp(f, "updated_at")
            }, ZendeskJson.Timestamp(f, "updated_at"), response.FetchedAt))
        ];
    }
}
