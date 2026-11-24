# Google Sheets

Connects INTERA directly to the Google Sheets API v4 (`https://sheets.googleapis.com/v4/spreadsheets`) using a Google Cloud service account, exposing spreadsheet tabs as typed data sources without any intermediate container service.

| | |
| --- | --- |
| Catalog id | `intera.google-sheets` |
| Runtime id | `intera-gsheets-integration` |
| Version | 1.3.1 |
| Integration Contract | 2 |
| Query Contract | 1.5.0 |
| Minimum INTERA | 0.5.25 |
| License | Apache-2.0 |
| Deployment | Bundle-only (`deployment.type: none`) |

## Network requirements

Because this integration runs inside the INTERA Core bundle runtime (`deployment.type: none`), the Core host needs outbound HTTPS (`443`) to:

- `oauth2.googleapis.com` — OAuth 2.0 JWT bearer token exchange (RFC 7523)
- `sheets.googleapis.com` — Google Sheets API v4 (read-only scope `https://www.googleapis.com/auth/spreadsheets.readonly`)

## Google Cloud Service Account setup

1. Open the [Google Cloud Console](https://console.cloud.google.com/) and select or create a project.
2. Go to **APIs & Services → Library**, search for **Google Sheets API**, and click **Enable**.
3. Go to **IAM & Admin → Service Accounts → Create Service Account**:
   - Give the service account a descriptive name (no project-level IAM roles are required).
   - Open the created service account → **Keys → Add Key → Create new key → JSON** and download the JSON key file.
4. Open the target Google Spreadsheet in Google Sheets, click **Share**, and grant **Viewer** access to the service account's `client_email` address from the JSON key file.
5. Paste the spreadsheet URL (or spreadsheet ID) into `spreadsheet` and the entire JSON key file contents into `service_account_json`.

Domain-wide delegation (`sub`) is intentionally not used: sharing individual spreadsheets with `client_email` as Viewer scopes access strictly to the shared files.

## Connection configuration (`configSchema`)

| Field | Kind | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `spreadsheet` | `string` | yes | — | Full Google Sheets URL (`https://docs.google.com/spreadsheets/d/...`) or bare spreadsheet ID |
| `sheet_name` | `string` | no | — | Primary tab title (empty uses the `gid` from a pasted URL if present, otherwise the first tab) |
| `service_account_json` | `secret` | yes | — | Complete JSON key file downloaded from Google Cloud for the service account |
| `is_first_row_header` | `boolean` | no | `true` | Treat the first non-skipped row as column headers (when `false`, columns are named `column_1`, `column_2`, …) |
| `skip_rows` | `number` | no | `0` | Number of rows above the table header/data to ignore (`0`–`10000`) |
| `week_start` | `enum` | no | `monday` | First day of the week (`monday`..`sunday`) used by `snapTo: 'week'` relative-date filters and `dateDiff` |
| `identity_column` | `string` | no | — | Normalized column name to mark with `identity: true` (required on the primary tab when set; optional on secondary tabs) |

## Dynamic tab & column provisioning

When an integration instance is created or updated, `provisionInstance` connects to Google Sheets, inspects the spreadsheet metadata, and samples up to 500 data rows and cell number formats across tabs:

- **Primary tab (`gsheet-rows`)**: resolves to `sheet_name` when configured, else the `#gid=...` from a pasted spreadsheet URL (falling back to the first tab if that `gid` no longer exists), else the first tab. An empty or header-only primary tab, or a missing `identity_column` on it, fails provisioning with an actionable message.
- **Additional tabs (`gsheet-tab-<gid>`)**: every other `GRID` tab containing tabular data is automatically provisioned as its own data source on the `gsheet-rows` template, keyed by Google's stable numeric `sheetId` (`gid`) so renaming a tab preserves saved metrics and widgets. Non-grid chart sheets (`OBJECT`), empty tabs, and header-only secondary tabs are skipped cleanly.
- **Column naming & typing**: header text is normalized to lowercase Unicode letters/digits separated by underscores (preserving Latin, Cyrillic, and other scripts, e.g. `Bill Cycle Start` → `bill_cycle_start`, `Дата заказа` → `дата_заказа`), with duplicate headers suffixed `_2`, `_3`, … Columns are typed as `number`, `date`, `boolean`, or `string` from both cell values and Google Sheets number formats (`DATE`, `DATE_TIME`, `TIME`).
- **Row bounds**: queries read at most `100,000` data rows per tab via bounded A1 ranges and enforce a `64 MiB` response payload cap. Re-save the instance after adding tabs or modifying header rows to refresh provisioned schemas.

## Build and test locally

```bash
cd integrations/intera/google-sheets/bundle
npm install
npm test
cd -
node scripts/validate-manifest.mjs integrations/intera/google-sheets
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
