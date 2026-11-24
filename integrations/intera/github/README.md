# GitHub

Connects INTERA directly to the GitHub REST API (`https://api.github.com` or GitHub Enterprise Server `/api/v3`) using either a fine-grained Personal Access Token or a GitHub App installation to monitor repositories, Actions workflows and runs, deployments, commits, pull requests, issues, and security alerts.

| | |
| --- | --- |
| Catalog id | `intera.github` |
| Runtime id | `intera-github-integration` |
| Version | 3.2.1 |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Bundle-only (`deployment.type: none`) |

## Connection configuration (`configSchema`)

| Field | Kind | Required | Description |
| --- | --- | --- | --- |
| `owner` | `string` | yes | GitHub organization or user login |
| `accessToken` | `secret` | no | Personal access token (mutually exclusive with `appId` + `appPrivateKey`) |
| `appId` | `string` | no | GitHub App ID or Client ID |
| `appPrivateKey` | `secret` | no | GitHub App PEM private key |
| `installationId` | `string` | no | GitHub App installation ID (empty: auto-detected on `owner`) |
| `repositories` | `string` | no | Comma-separated repository names (empty: all visible repositories) |
| `includeArchivedRepositories` | `boolean` | no | Include contents of archived repositories (default: `false`) |
| `historyDays` | `number` | no | Default lookback window in days when no filter lower bound is set (default: `30`) |
| `apiUrl` | `string` | no | GitHub Enterprise Server API root (`https://<host>/api/v3`) |

## Data sources

- `github_repositories` — Repositories
- `github_workflow_runs` — Actions workflow runs
- `github_workflows` — Actions workflows
- `github_deployments` — Deployments and latest deployment statuses
- `github_commits` — Commits
- `github_pull_requests` — Pull requests and reviews
- `github_issues` — Issues
- `github_dependabot_alerts` — Dependabot vulnerability alerts
- `github_code_scanning_alerts` — Code scanning alerts
- `github_secret_scanning_alerts` — Secret scanning alerts
- `github_actions_artifacts` — Actions build artifacts

## Build and test locally

```bash
cd integrations/intera/github/bundle
npm ci
npm test
cd -
node scripts/validate-manifest.mjs integrations/intera/github
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
