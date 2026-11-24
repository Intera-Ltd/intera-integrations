import {
  CREDENTIALS_CONFIG_FIELD,
  DATA_SOURCE_CAPABILITY,
  DEFAULT_CACHE_TTL_SEC,
  DIRECT_API_KIND,
  FIRST_ROW_HEADER_CONFIG_FIELD,
  IDENTITY_COLUMN_CONFIG_FIELD,
  INTEGRATION_ID,
  INTEGRATION_NAME,
  INTEGRATION_VERSION,
  SHEET_NAME_CONFIG_FIELD,
  SKIP_ROWS_CONFIG_FIELD,
  SPREADSHEET_CONFIG_FIELD,
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
  icon: '📊',
  description:
    'Reads a Google spreadsheet as datasources, one datasource per tab. The ' +
    'columns are taken from each tab itself when the instance is saved, so ' +
    'one integration serves any sheet — a manually maintained price list, a ' +
    'reconciliation sheet, or a report someone keeps in Google Drive.',
  types: ['core'],
  // Simple Icons "googlesheets" (CC0), filled with its brand colour #34A853.
  iconFile: 'assets/icon.svg',
  configSchema: [
    {
      name: SPREADSHEET_CONFIG_FIELD,
      label:
        'Spreadsheet — paste its URL, or just the id from the middle of that URL',
      kind: 'string',
      required: true,
    },
    {
      name: SHEET_NAME_CONFIG_FIELD,
      label:
        'Main tab (empty: the tab the pasted URL pointed at, else the first ' +
        'one). Every other tab becomes a datasource of its own as well',
      kind: 'string',
    },
    {
      name: CREDENTIALS_CONFIG_FIELD,
      label:
        'Service account key — the whole JSON file from Google Cloud. Share ' +
        'the spreadsheet with the client_email it contains (Viewer is enough).',
      kind: 'secret',
      required: true,
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
