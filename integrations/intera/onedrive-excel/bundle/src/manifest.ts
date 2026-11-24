import {
  CLIENT_ID_CONFIG_FIELD,
  CLIENT_SECRET_CONFIG_FIELD,
  DATA_SOURCE_CAPABILITY,
  DEFAULT_CACHE_TTL_SEC,
  DIRECT_API_KIND,
  DRIVE_ID_CONFIG_FIELD,
  FIRST_ROW_HEADER_CONFIG_FIELD,
  IDENTITY_COLUMN_CONFIG_FIELD,
  INTEGRATION_ID,
  INTEGRATION_NAME,
  INTEGRATION_VERSION,
  ITEM_ID_CONFIG_FIELD,
  SHEET_NAME_CONFIG_FIELD,
  SKIP_ROWS_CONFIG_FIELD,
  TENANT_ID_CONFIG_FIELD,
  WEEK_START_CONFIG_FIELD,
} from './constants';
import type { IIntegrationManifest } from './types';

export const manifest: IIntegrationManifest = {
  id: INTEGRATION_ID,
  name: INTEGRATION_NAME,
  version: INTEGRATION_VERSION,
  requires: { core: '>=0.5.25' },
  compatibility: { integration_contract: 2 },
  kind: DIRECT_API_KIND,
  capabilities: [DATA_SOURCE_CAPABILITY],
  defaultCacheTtlSec: DEFAULT_CACHE_TTL_SEC,
  author: 'Intera',
  icon: '📗',
  // Authored clean spreadsheet grid icon in Excel brand green #217346; build copies assets/ into dist/.
  iconFile: 'assets/icon.svg',
  description:
    'Reads one worksheet of an Excel workbook (.xlsx) kept in OneDrive for ' +
    'Business or a SharePoint library. The columns are taken from the sheet ' +
    'itself when the instance is saved, so one integration serves any ' +
    'workbook — a manually maintained price list, a reconciliation sheet, or ' +
    'an operational report someone keeps in OneDrive.',
  types: ['core'],
  configSchema: [
    {
      name: TENANT_ID_CONFIG_FIELD,
      label:
        'Microsoft 365 tenant — the Directory (tenant) ID of the app ' +
        'registration, or a verified domain such as contoso.onmicrosoft.com',
      kind: 'string',
      required: true,
    },
    {
      name: CLIENT_ID_CONFIG_FIELD,
      label: 'Application (client) ID of the Entra ID app registration',
      kind: 'string',
      required: true,
    },
    {
      name: CLIENT_SECRET_CONFIG_FIELD,
      label:
        "Client secret — the secret's Value, not its Secret ID. The app needs " +
        'the Files.SelectedOperations.Selected (or Sites.Selected) application ' +
        'permission with admin consent, and read access granted on this workbook.',
      kind: 'secret',
      required: true,
    },
    {
      name: DRIVE_ID_CONFIG_FIELD,
      label: 'Drive ID of the workbook (b!…), as Microsoft Graph reports it',
      kind: 'string',
      required: true,
    },
    {
      name: ITEM_ID_CONFIG_FIELD,
      label: 'Item ID of the workbook (01…), as Microsoft Graph reports it',
      kind: 'string',
      required: true,
    },
    {
      name: SHEET_NAME_CONFIG_FIELD,
      label: 'Worksheet name (empty: the first visible worksheet)',
      kind: 'string',
    },
    {
      name: FIRST_ROW_HEADER_CONFIG_FIELD,
      label: 'First row is a header',
      kind: 'boolean',
      defaultValue: true,
    },
    {
      name: SKIP_ROWS_CONFIG_FIELD,
      label: 'Rows to skip above the table — a title line, a note, a blank row',
      kind: 'number',
      defaultValue: 0,
    },
    {
      name: WEEK_START_CONFIG_FIELD,
      label:
        'First day of the week — used by date filters that snap to the start of a week',
      kind: 'enum',
      options: [
        'monday',
        'tuesday',
        'wednesday',
        'thursday',
        'friday',
        'saturday',
        'sunday',
      ],
      defaultValue: 'monday',
    },
    {
      name: IDENTITY_COLUMN_CONFIG_FIELD,
      label: 'Identity column (leave empty for a metric-only source)',
      kind: 'string',
    },
  ],
};
