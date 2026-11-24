import type { DataRecord } from '@intera-integrations/shared';

export * from '@intera-integrations/shared/contract-types';

/** Resolved instance config. */
export interface IGsheetConfig {
  /** Spreadsheet id, extracted from a pasted URL when needed. */
  spreadsheetId: string;
  /** Configured tab name, or null when chosen by {@link sheetGid} or position. */
  sheetName: string | null;
  /** `gid` from a pasted spreadsheet URL, used when `sheet_name` is empty. */
  sheetGid: number | null;
  credentials: IServiceAccount;
  isFirstRowHeader: boolean;
  /** Rows to ignore above the table. */
  skipRows: number;
  identityColumn: string | null;
  /** First day of the week (0 = Sunday … 6 = Saturday) for `snapTo: 'week'`. */
  weekStart: number;
}

/** Service-account credentials used to sign JWT bearer assertions. */
export interface IServiceAccount {
  clientEmail: string;
  /** PEM-encoded RSA private key. */
  privateKey: string;
  /** OAuth 2.0 token endpoint URL from the key file. */
  tokenUri: string;
}

/** One sheet row, keyed by normalized column name. */
export type SheetRecord = DataRecord;
