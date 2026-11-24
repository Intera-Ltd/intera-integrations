import type { DataRecord } from '@intera-integrations/shared';

export * from '@intera-integrations/shared/contract-types';

/** Resolved instance config. */
export interface IExcelConfig {
  credentials: IAppCredentials;
  /** Graph id of the drive holding the workbook (`b!…`). */
  driveId: string;
  /** Graph id of the workbook inside that drive (`01…`). */
  itemId: string;
  /** Worksheet name, or null for the first visible worksheet. */
  sheetName: string | null;
  isFirstRowHeader: boolean;
  /** Rows to ignore above the table. */
  skipRows: number;
  identityColumn: string | null;
  /** First day of the week (0 = Sunday … 6 = Saturday) for `snapTo: 'week'`. */
  weekStart: number;
}

/** Entra ID app registration signing in with client credentials. */
export interface IAppCredentials {
  /** Directory (tenant) GUID or verified domain — never `common`, `organizations`, or `consumers`. */
  tenantId: string;
  /** Application (client) GUID. */
  clientId: string;
  /** Client secret Value (not its Secret ID). */
  clientSecret: string;
}

/** One worksheet row, keyed by normalized column name. */
export type SheetRecord = DataRecord;
