import {
  MAX_RESPONSE_BYTES,
  REQUEST_TIMEOUT_MS,
  SHEETS_API_BASE,
} from '../constants';
import type { IGsheetConfig } from '../types';
import {
  extractPemSecrets,
  fetchBoundedText,
  redactSecrets,
  type IBoundedTextResponse,
} from './http';
import { getAccessToken, invalidateAccessToken } from './token-cache';

/**
 * Google Sheets API v4 read client.
 *
 * Reading a tab combines `values.get` (`UNFORMATTED_VALUE` + `SERIAL_NUMBER`)
 * with a bounded `spreadsheets.get` number-format sample so date columns are
 * identified reliably regardless of the spreadsheet's display locale.
 */

export interface ISheetProperties {
  title: string;
  sheetId: number;
  /** `GRID` for cell tabs; `OBJECT` for standalone chart sheets. */
  sheetType: string;
  rowCount: number;
  columnCount: number;
}

export interface ISpreadsheetProperties {
  title: string;
  sheets: ISheetProperties[];
}

/** Google's `NumberFormatType`. `null` = the cell carries no explicit format. */
export type TNumberFormatType =
  | 'TEXT'
  | 'NUMBER'
  | 'PERCENT'
  | 'CURRENCY'
  | 'DATE'
  | 'TIME'
  | 'DATE_TIME'
  | 'SCIENTIFIC';

/** One range whose number formats {@link getNumberFormats} reads. */
export interface IFormatRange {
  sheetId: number;
  a1Range: string;
  /** 1-based top-left of `a1Range`, which the answer is aligned to. */
  firstRow: number;
  firstColumn: number;
}

/** Maximum A1 ranges passed in query parameters of a single batch request. */
const MAX_RANGES_PER_REQUEST = 40;

export async function getSpreadsheetProperties(
  config: IGsheetConfig,
  signal?: AbortSignal,
): Promise<ISpreadsheetProperties> {
  const payload = await request<{
    properties?: { title?: string };
    sheets?: Array<{
      properties?: {
        title?: string;
        sheetId?: number;
        sheetType?: string;
        gridProperties?: { rowCount?: number; columnCount?: number };
      };
    }>;
  }>(
    config,
    `/${encodeURIComponent(config.spreadsheetId)}` +
      '?fields=properties.title,sheets.properties(title,sheetId,sheetType,gridProperties(rowCount,columnCount))',
    signal,
  );

  const sheets: ISheetProperties[] = (payload.sheets ?? []).map((sheet) => ({
    title: sheet.properties?.title ?? '',
    sheetId: sheet.properties?.sheetId ?? 0,
    sheetType: sheet.properties?.sheetType ?? 'GRID',
    rowCount: sheet.properties?.gridProperties?.rowCount ?? 0,
    columnCount: sheet.properties?.gridProperties?.columnCount ?? 0,
  }));

  return { title: payload.properties?.title ?? '', sheets };
}

/**
 * Cell values of an A1 range, row-major. Google omits trailing empty cells and
 * trailing empty rows, so rows are ragged.
 */
export async function getValues(
  config: IGsheetConfig,
  a1Range: string,
  signal?: AbortSignal,
): Promise<unknown[][]> {
  const payload = await request<{ values?: unknown[][] }>(
    config,
    `/${encodeURIComponent(config.spreadsheetId)}/values/${encodeURIComponent(a1Range)}` +
      '?majorDimension=ROWS&valueRenderOption=UNFORMATTED_VALUE' +
      '&dateTimeRenderOption=SERIAL_NUMBER',
    signal,
  );
  return payload.values ?? [];
}

/**
 * Cell values of several A1 ranges in request order, batched up to 40 ranges
 * per call so multi-tab provisioning stays well within per-minute read quotas.
 */
export async function batchGetValues(
  config: IGsheetConfig,
  a1Ranges: string[],
  signal?: AbortSignal,
): Promise<unknown[][][]> {
  const result: unknown[][][] = [];
  for (const chunk of chunks(a1Ranges, MAX_RANGES_PER_REQUEST)) {
    const payload = await request<{
      valueRanges?: Array<{ values?: unknown[][] }>;
    }>(
      config,
      `/${encodeURIComponent(config.spreadsheetId)}/values:batchGet?` +
        chunk.map((range) => `ranges=${encodeURIComponent(range)}`).join('&') +
        '&majorDimension=ROWS&valueRenderOption=UNFORMATTED_VALUE' +
        '&dateTimeRenderOption=SERIAL_NUMBER',
      signal,
    );
    for (let index = 0; index < chunk.length; index++) {
      result.push(payload.valueRanges?.[index]?.values ?? []);
    }
  }
  return result;
}

/**
 * Number-format types of sampled cells for each tab range, keyed by `sheetId`
 * and aligned to `firstRow` / `firstColumn` so non-zero `GridData.startRow`
 * offsets never skew column inference.
 */
export async function getNumberFormats(
  config: IGsheetConfig,
  ranges: IFormatRange[],
  signal?: AbortSignal,
): Promise<Map<number, Array<Array<TNumberFormatType | null>>>> {
  const result = new Map<number, Array<Array<TNumberFormatType | null>>>();
  for (const chunk of chunks(ranges, MAX_RANGES_PER_REQUEST)) {
    const payload = await request<{
      sheets?: Array<{
        properties?: { sheetId?: number };
        data?: IGridData[];
      }>;
    }>(
      config,
      `/${encodeURIComponent(config.spreadsheetId)}?` +
        chunk
          .map((range) => `ranges=${encodeURIComponent(range.a1Range)}`)
          .join('&') +
        '&includeGridData=true' +
        '&fields=sheets(properties(sheetId),data(startRow,startColumn,rowData(values(effectiveFormat(numberFormat(type))))))',
      signal,
    );

    for (const sheet of payload.sheets ?? []) {
      const sheetId = sheet.properties?.sheetId ?? 0;
      const range = chunk.find((candidate) => candidate.sheetId === sheetId);
      if (range !== undefined) {
        result.set(sheetId, alignFormats(sheet.data?.[0], range));
      }
    }
  }
  return result;
}

/** `'Tab'!A5:H504` — bounded A1 range for a sheet table. */
export function buildRange(
  title: string,
  firstRow: number,
  lastRow: number,
  columnCount: number,
): string {
  const lastColumn = columnLetter(Math.max(1, columnCount));
  return `${quoteTabName(title)}!A${firstRow}:${lastColumn}${lastRow}`;
}

/** Always single-quotes tab names and escapes internal single quotes as `''`. */
export function quoteTabName(title: string): string {
  return `'${title.replace(/'/g, "''")}'`;
}

/** 1 → A, 26 → Z, 27 → AA. */
export function columnLetter(columnNumber: number): string {
  let remaining = Math.max(1, Math.floor(columnNumber));
  let letters = '';
  while (remaining > 0) {
    const index = (remaining - 1) % 26;
    letters = String.fromCharCode(65 + index) + letters;
    remaining = Math.floor((remaining - 1) / 26);
  }
  return letters;
}

interface IGridData {
  startRow?: number;
  startColumn?: number;
  rowData?: Array<{
    values?: Array<{
      effectiveFormat?: { numberFormat?: { type?: string } };
    }>;
  }>;
}

function alignFormats(
  grid: IGridData | undefined,
  range: IFormatRange,
): Array<Array<TNumberFormatType | null>> {
  const rowOffset = (grid?.startRow ?? 0) - (range.firstRow - 1);
  const columnOffset = (grid?.startColumn ?? 0) - (range.firstColumn - 1);

  const matrix: Array<Array<TNumberFormatType | null>> = [];
  for (let index = 0; index < rowOffset; index++) {
    matrix.push([]);
  }
  for (const row of grid?.rowData ?? []) {
    const cells: Array<TNumberFormatType | null> = new Array(
      Math.max(0, columnOffset),
    ).fill(null);
    for (const cell of row.values ?? []) {
      const type = cell.effectiveFormat?.numberFormat?.type;
      cells.push(typeof type === 'string' ? (type as TNumberFormatType) : null);
    }
    matrix.push(cells);
  }
  return matrix;
}

function chunks<T>(items: T[], size: number): T[][] {
  const result: T[][] = [];
  for (let index = 0; index < items.length; index += size) {
    result.push(items.slice(index, index + size));
  }
  return result;
}

async function request<T>(
  config: IGsheetConfig,
  pathAndQuery: string,
  signal?: AbortSignal,
): Promise<T> {
  const pemSecrets = extractPemSecrets(config.credentials.privateKey);
  for (let attempt = 0; ; attempt++) {
    const token = await getAccessToken(config.credentials, signal);
    const secrets = [...pemSecrets, token];
    const response = await send(
      `${SHEETS_API_BASE}${pathAndQuery}`,
      token,
      secrets,
      signal,
    );

    if (response.status === 401 && attempt === 0) {
      invalidateAccessToken(config.credentials);
      continue;
    }
    if (!response.ok) {
      throw new Error(
        describeApiFailure(response.status, response.body, config, secrets),
      );
    }

    try {
      return JSON.parse(response.body) as T;
    } catch {
      throw new Error(
        `Google Sheets answered ${response.status} with a non-JSON body`,
      );
    }
  }
}

function send(
  url: string,
  token: string,
  secrets: readonly string[],
  signal?: AbortSignal,
): Promise<IBoundedTextResponse> {
  return fetchBoundedText(
    url,
    {
      method: 'GET',
      headers: {
        authorization: `Bearer ${token}`,
        accept: 'application/json',
      },
    },
    {
      timeoutMs: REQUEST_TIMEOUT_MS,
      maxBytes: MAX_RESPONSE_BYTES,
      timeoutMessage: `Google Sheets did not answer within ${REQUEST_TIMEOUT_MS} ms`,
      unreachablePrefix: 'Cannot reach the Google Sheets API: ',
      sizeLabel: 'Google Sheets API',
      signal,
      secrets,
    },
  );
}

function describeApiFailure(
  status: number,
  body: string,
  config: IGsheetConfig,
  secrets: readonly string[],
): string {
  let message = '';
  try {
    const parsed = JSON.parse(body) as { error?: { message?: unknown } };
    if (typeof parsed.error?.message === 'string') {
      message = redactSecrets(parsed.error.message, secrets);
    }
  } catch {
    // Never include raw non-JSON upstream response bodies in thrown errors.
    message = '';
  }

  if (status === 403) {
    return (
      `Google refused the read (403). Share the spreadsheet with ${config.credentials.clientEmail} ` +
      `(Viewer is enough), or enable the Google Sheets API for the service account's project. ${message}`.trimEnd()
    );
  }
  if (status === 404) {
    return (
      `Spreadsheet ${config.spreadsheetId} does not exist, or is not visible to ` +
      `${config.credentials.clientEmail}. ${message}`.trimEnd()
    );
  }
  if (status === 429) {
    return (
      'Google Sheets read quota exceeded (429). The limit is per minute and ' +
      'per project, so it clears by itself; raising the instance cache TTL is ' +
      `the fix if it keeps happening. ${message}`.trimEnd()
    );
  }
  if (status === 400) {
    return `Google Sheets rejected the request (400). ${message}`.trimEnd();
  }
  if (status === 401) {
    return (
      'Google refused the access token (401) twice, with a freshly issued ' +
      `token the second time — the service-account key is no longer valid. ${message}`.trimEnd()
    );
  }
  return `Google Sheets answered ${status}. ${message}`.trimEnd();
}
