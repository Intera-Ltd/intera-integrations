# Asana

Connects INTERA directly to the Asana REST API (`https://app.asana.com/api/1.0`) to monitor workspaces, teams, projects, sections, tasks, subtasks, dependencies, custom fields, status updates, time tracking, portfolios, and goals.

| | |
| --- | --- |
| Catalog id | `intera.asana` |
| Runtime id | `intera-asana-integration` |
| Version | 3.2.0 |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Bundle-only (`deployment.type: none`) |

## Connection configuration (`configSchema`)

| Field | Kind | Required | Description |
| --- | --- | --- | --- |
| `accessToken` | `secret` | yes | Personal access token or Enterprise service account token |
| `workspace` | `string` | no | Workspace name or `gid` (required only when the token belongs to multiple workspaces) |
| `projects` | `string` | no | Comma-separated project `gid`s or exact names (empty: all visible projects) |
| `includeArchivedProjects` | `boolean` | no | Read tasks of archived projects too (default: `false`) |
| `includeSubtasks` | `boolean` | no | Fetch subtasks per parent task (default: `true`) |

## Data sources

- `asana_workspaces` — Workspaces
- `asana_teams` — Teams
- `asana_projects` — Projects
- `asana_sections` — Sections
- `asana_tasks` — Tasks (including subtasks when enabled)
- `asana_task_memberships` — Task project/section memberships
- `asana_task_dependencies` — Task dependencies
- `asana_users` — Workspace users
- `asana_status_updates` — Project status updates
- `asana_custom_fields` — Custom field definitions
- `asana_task_custom_field_values` — Task custom field values
- `asana_task_activity` — Task comments and system stories
- `asana_time_tracking` — Time tracking entries
- `asana_portfolios` — Portfolios
- `asana_portfolio_items` — Portfolio items
- `asana_goals` — Goals

## Build and test locally

```bash
cd integrations/intera/asana/bundle
npm ci
npm test
cd -
node scripts/validate-manifest.mjs integrations/intera/asana
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
