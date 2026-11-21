using Intera.Jira.Api.Clients;
using Intera.Jira.Api.Infrastructure;
using System.Text.Json;

namespace Intera.Jira.Api.Services;

using Row = Dictionary<string, object?>;

/// <summary>
/// The Jira Software sources, from the Agile REST API (<c>/rest/agile/1.0</c>), which takes granular
/// <c>*:jira-software</c> scopes only. A site without Jira Software answers these with an error that names the
/// missing scope or product.
/// </summary>
public sealed partial class JiraDataService
{
    /// <summary>The Agile API's page cap.</summary>
    private const int AgilePageSize = 50;

    // ---- Boards ---------------------------------------------------------------------------------------------

    private Task<JiraCollection> ReadBoardsAsync(JiraSite site, string? type, CancellationToken cancellationToken) =>
        client.GetPagedAsync(
            site,
            "rest/agile/1.0/board",
            type is null ? null : [Param("type", type)],
            "values",
            AgilePageSize,
            reference: true,
            cancellationToken);

    private async Task<List<Row>> LoadBoardsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var types = Keys(context.Query, "board_type");
        var boards = await ReadBoardsAsync(context.Site, types.Count == 1 ? types[0].ToLowerInvariant() : null, cancellationToken).ConfigureAwait(false);

        // The board's filter is only in its configuration — one call per board — so it is read only when the
        // answer can depend on it.
        var filters = new Dictionary<string, (string? Id, string? Name)>(StringComparer.Ordinal);
        if (context.References.Needs("filter_id") || context.References.Needs("filter_name"))
        {
            filters = await ReadBoardFiltersAsync(context, boards.Items, cancellationToken).ConfigureAwait(false);
        }

        return
        [
            .. boards.Items.Select(board =>
            {
                var id = JiraJson.String(board, "id");
                var location = JiraJson.Object(board, "location");
                var filter = id is not null && filters.TryGetValue(id, out var known) ? known : default;

                return Stamp(context, new Row
                {
                    ["board_id"] = id,
                    ["board_name"] = JiraJson.String(board, "name"),
                    ["board_type"] = JiraJson.String(board, "type"),
                    ["project_id"] = JiraJson.String(location, "projectId"),
                    ["project_key"] = JiraJson.String(location, "projectKey"),
                    ["project_name"] = JiraJson.String(location, "projectName"),
                    ["filter_id"] = filter.Id,
                    ["filter_name"] = filter.Name,
                    // A board lives in a project or in a user's space; the list says which only by what it fills in.
                    ["location_type"] = JiraJson.String(location, "projectId") is not null ? "project"
                        : JiraJson.String(location, "userAccountId") is not null ? "user"
                        : null,
                    ["location_name"] = JiraJson.String(location, "displayName") ?? JiraJson.String(location, "name"),
                    ["is_private"] = JiraJson.Boolean(board, "isPrivate")
                }, null, boards.FetchedAt);
            })
        ];
    }

    /// <summary>
    /// Board id → its filter (id, name): <c>GET /board/{id}/configuration</c> per board, then the names from
    /// <c>GET /rest/api/3/filter/search?id=…</c>, up to 200 ids a call. A board whose configuration is refused (the
    /// grant lacks <c>read:board-scope.admin:jira-software</c>, or the user cannot see it) keeps empty filter fields
    /// rather than failing the whole list; the refusal is logged.
    /// </summary>
    private async Task<Dictionary<string, (string? Id, string? Name)>> ReadBoardFiltersAsync(
        ReadContext context,
        IReadOnlyList<JsonElement> boards,
        CancellationToken cancellationToken)
    {
        var ids = boards.Select(static board => JiraJson.String(board, "id")).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        RequireFanout(ids.Count, "boards");

        var configurations = await FanOutAsync(ids, async id =>
        {
            try
            {
                var configuration = await client.GetAsync(context.Site, $"rest/agile/1.0/board/{id}/configuration", null, reference: true, cancellationToken)
                    .ConfigureAwait(false);
                return (BoardId: id, FilterId: JiraJson.String(JiraJson.Object(configuration.Root, "filter"), "id"));
            }
            catch (Exception exception) when (exception is JiraAuthorizationException or JiraRequestException)
            {
                logger.LogWarning("Jira refused the configuration of board {BoardId}: {Message}", id, exception.Message);
                return (BoardId: id, FilterId: (string?)null);
            }
        }).ConfigureAwait(false);

        var filterIds = configurations.Select(static entry => entry.FilterId).OfType<string>().Where(Jql.IsNumericId).Distinct(StringComparer.Ordinal).ToList();
        var names = new Dictionary<string, string?>(StringComparer.Ordinal);

        var lists = await FanOutAsync(filterIds.Chunk(200), chunk => client.GetPagedAsync(
            context.Site,
            "rest/api/3/filter/search",
            [.. chunk.Select(id => Param("id", id))],
            "values",
            100,
            reference: true,
            cancellationToken)).ConfigureAwait(false);

        foreach (var filter in lists.SelectMany(static list => list.Items))
        {
            if (JiraJson.String(filter, "id") is { } filterId)
            {
                names[filterId] = JiraJson.String(filter, "name");
            }
        }

        return configurations.ToDictionary(
            static entry => entry.BoardId,
            entry => (entry.FilterId, entry.FilterId is not null && names.TryGetValue(entry.FilterId, out var name) ? name : null),
            StringComparer.Ordinal);
    }

    // ---- Sprints --------------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadSprintsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var sprintIds = Keys(context.Query, "sprint_id").Where(Jql.IsNumericId).ToList();
        var states = Keys(context.Query, "state").Select(static state => state.ToLowerInvariant()).ToList();

        List<(JsonElement Sprint, string? ListedOn, DateTime FetchedAt)> sprints;

        if (sprintIds.Count is > 0 and <= 50 && Names(context.Query, "sprint_id"))
        {
            // A lookup's IN [...]: one call per sprint rather than one per board.
            var read = await FanOutAsync(sprintIds, id => TryGetAsync(context.Site, $"rest/agile/1.0/sprint/{id}", cancellationToken)).ConfigureAwait(false);
            sprints = [.. read.OfType<JiraResponse>().Select(static response => (response.Root, (string?)null, response.FetchedAt))];
        }
        else
        {
            // Every board but a kanban one may hold sprints. Scrum boards do, and so do the boards of team-managed
            // spaces, which report the type "simple" (seen on a live site 2026-10-07: a sprint board of a Scrum
            // space). A "simple" board without sprints answers "The board does not support sprints" — refused
            // below and read as none.
            var boards = await ReadBoardsAsync(context.Site, null, cancellationToken).ConfigureAwait(false);
            var wantedBoards = Keys(context.Query, "board_id", "origin_board_id");
            var boardIds = boards.Items
                .Where(static board => !string.Equals(JiraJson.String(board, "type"), "kanban", StringComparison.OrdinalIgnoreCase))
                .Select(static board => JiraJson.String(board, "id"))
                .OfType<string>()
                .Where(id => wantedBoards.Count == 0 || wantedBoards.Contains(id, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            RequireFanout(boardIds.Count, "sprint boards");

            var stateQuery = states.Count > 0 && states.All(static state => state is "future" or "active" or "closed")
                ? new List<KeyValuePair<string, string>> { Param("state", string.Join(',', states)) }
                : null;

            var read = await FanOutAsync(boardIds, async boardId =>
            {
                try
                {
                    var list = await client.GetPagedAsync(
                        context.Site, $"rest/agile/1.0/board/{boardId}/sprint", stateQuery, "values", AgilePageSize, reference: false, cancellationToken)
                        .ConfigureAwait(false);
                    return list.Items.Select(sprint => (sprint, (string?)boardId, list.FetchedAt)).ToList();
                }
                catch (JiraRequestException exception)
                {
                    logger.LogDebug("Jira refused the sprints of board {BoardId}: {Message}", boardId, exception.Message);
                    return [];
                }
            }).ConfigureAwait(false);

            sprints = [.. read.SelectMany(static list => list)];
        }

        return
        [
            .. sprints
                .Where(static entry => JiraJson.String(entry.Sprint, "id") is not null)
                .GroupBy(static entry => JiraJson.String(entry.Sprint, "id")!, StringComparer.Ordinal)
                .Select(group =>
                {
                    var (sprint, listedOn, fetchedAt) = group.First();
                    var origin = JiraJson.String(sprint, "originBoardId");

                    return Stamp(context, new Row
                    {
                        ["sprint_id"] = group.Key,
                        // One row per sprint: under the board it was created on, else the first board that listed it.
                        ["board_id"] = origin ?? listedOn,
                        ["sprint_name"] = JiraJson.String(sprint, "name"),
                        ["state"] = JiraJson.String(sprint, "state"),
                        ["goal"] = JiraJson.String(sprint, "goal"),
                        ["start_date"] = JiraJson.Timestamp(sprint, "startDate"),
                        ["end_date"] = JiraJson.Timestamp(sprint, "endDate"),
                        ["complete_date"] = JiraJson.Timestamp(sprint, "completeDate"),
                        ["created_date"] = JiraJson.Timestamp(sprint, "createdDate"),
                        ["origin_board_id"] = origin
                    }, null, fetchedAt);
                })
        ];
    }
}
