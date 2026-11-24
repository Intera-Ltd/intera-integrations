# Excel (OneDrive / SharePoint)

Connects INTERA directly to Microsoft Graph (`https://graph.microsoft.com/v1.0`) using an Entra ID application registration, reading `.xlsx` workbooks stored in OneDrive for Business or SharePoint Online libraries without any intermediate container service or third-party npm dependencies.

| | |
| --- | --- |
| Catalog id | `intera.onedrive-excel` |
| Runtime id | `intera-onedrive-excel-integration` |
| Version | 3.3.0 |
| Integration Contract | 2 |
| Query Contract | 1.5.0 |
| Minimum INTERA | 0.5.25 |
| License | Apache-2.0 |
| Deployment | Bundle-only (`deployment.type: none`) |

## Network requirements

Because this integration runs inside the INTERA Core bundle runtime (`deployment.type: none`), the Core host needs outbound HTTPS (`443`) to:

- `login.microsoftonline.com` — Microsoft Entra ID OAuth 2.0 v2.0 token endpoint (`client_credentials` grant)
- `graph.microsoft.com` — Microsoft Graph v1.0 drive item metadata and download redirect
- `<tenant>.sharepoint.com` / `<tenant>-my.sharepoint.com` — SharePoint / OneDrive for Business pre-authenticated workbook download host

Personal Microsoft accounts (`consumers`) are not supported because Microsoft Graph app-only client-credentials authentication applies exclusively to Microsoft 365 / Entra ID directories.

## Entra ID setup (least privilege)

Avoid granting tenant-wide `Files.Read.All`. Use Microsoft Graph's item-scoped **`Files.SelectedOperations.Selected`** (or site-scoped **`Sites.Selected`**) application permission so the app registration can read only the specific workbook(s) or site(s) explicitly granted to it:

### 1. Register the application and grant consent

1. In the [Microsoft Entra admin center](https://entra.microsoft.com/), go to **Identity → Applications → App registrations → New registration** (single-tenant in your directory).
2. Note the **Application (client) ID** (`client_id`) and **Directory (tenant) ID** (`tenant_id`).
3. Go to **Certificates & secrets → Client secrets → New client secret** and copy the secret **Value** (`client_secret` — shown once at creation; do not copy the Secret ID).
4. Go to **API permissions → Add a permission → Microsoft Graph → Application permissions**, select **`Files.SelectedOperations.Selected`** (or **`Sites.Selected`** if granting read access at the SharePoint site level), and click **Grant admin consent**.

### 2. Locate the workbook's `drive_id` and `item_id`

Signed in to [Graph Explorer](https://developer.microsoft.com/en-us/graph/graph-explorer) as a user who can read the workbook:

- From your own OneDrive for Business:
  ```http
  GET https://graph.microsoft.com/v1.0/me/drive/root:/path/to/Workbook.xlsx?$select=id,parentReference
  ```
- Or from a SharePoint / OneDrive sharing link (`u!<base64url-encoded-sharing-url>`):
  ```http
  GET https://graph.microsoft.com/v1.0/shares/u!<encoded-url>/driveItem?$select=id,parentReference
  ```

In the JSON response, `parentReference.driveId` is `drive_id` (`b!…`) and `id` is `item_id` (`01…`).

### 3. Grant the application read access to the workbook (or site)

- **Per-workbook grant (`Files.SelectedOperations.Selected`)** — as a site/library owner or SharePoint administrator:
  ```http
  POST https://graph.microsoft.com/v1.0/drives/{drive-id}/items/{item-id}/permissions
  Content-Type: application/json

  {
    "roles": ["read"],
    "grantedToV2": {
      "application": {
        "id": "<client-id>"
      }
    }
  }
  ```
- **Per-site grant (`Sites.Selected`)** — alternatively, grant `"roles": ["read"]` to the app on `POST /sites/{site-id}/permissions` to cover all libraries in one dedicated SharePoint site.

## Connection configuration (`configSchema`)

| Field | Kind | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `tenant_id` | `string` | yes | — | Entra ID Directory (tenant) GUID or verified domain (e.g. `contoso.onmicrosoft.com`; never `common`, `organizations`, or `consumers`) |
| `client_id` | `string` | yes | — | Entra ID Application (client) GUID |
| `client_secret` | `secret` | yes | — | Client secret **Value** (not the Secret ID) |
| `drive_id` | `string` | yes | — | Microsoft Graph Drive ID (`b!…`) containing the workbook |
| `item_id` | `string` | yes | — | Microsoft Graph Item ID (`01…`) of the `.xlsx` workbook |
| `sheet_name` | `string` | no | — | Worksheet name (empty selects the first visible worksheet; an explicitly named worksheet is read even if hidden) |
| `is_first_row_header` | `boolean` | no | `true` | Treat the first non-skipped row as column headers (when `false`, columns are named `column_1`, `column_2`, …) |
| `skip_rows` | `number` | no | `0` | Number of rows above the table header/data to ignore (`0`–`10000`) |
| `week_start` | `enum` | no | `monday` | First day of the week (`monday`..`sunday`) used by `snapTo: 'week'` relative-date filters and `dateDiff` |
| `identity_column` | `string` | no | — | Normalized column name to mark with `identity: true` (empty for metric-only sources) |

## Dynamic worksheet column provisioning & bounds

- **Data source (`excel-rows`)**: when an instance is saved, `provisionInstance` signs in, verifies access on `GET /drives/{drive_id}/items/{item_id}`, downloads and parses the `.xlsx` workbook structure, resolves the target worksheet (`sheet_name` or first visible worksheet), and samples up to 500 data rows to infer column names and types.
- **Column naming & typing**: header cells are normalized to lowercase Unicode letters/digits separated by underscores (preserving Latin, Cyrillic, and other scripts, e.g. `Invoice Date` → `invoice_date`, `Дата заказа` → `дата_заказа`), with duplicate headers suffixed `_2`, `_3`, … Columns are typed as `number`, `date`, `boolean`, or `string` using SpreadsheetML cell types, `cellXfs` number-format codes (supporting both 1900 and 1904 Excel date systems while keeping elapsed durations such as `[h]:mm` numeric), and text parsers.
- **Format & resource bounds**: legacy `.xls`, encrypted/sensitivity-labeled OLE containers, and `.xlsb` binary workbooks are rejected with descriptive errors. Downloads are capped at `64 MiB` compressed, JSON responses at `32 MiB`, individual inflated ZIP parts at `256 MiB`, and returned worksheet rows at `100,000`. Downloaded workbooks are cached in memory (up to 4 entries, LRU) keyed by SHA-256 credential fingerprint, drive/item ID, and `cTag`/`eTag` only after each call's metadata authorization check succeeds.

## Build and test locally

```bash
cd integrations/intera/onedrive-excel/bundle
npm install
npm test
cd -
node scripts/validate-manifest.mjs integrations/intera/onedrive-excel
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
