import {
  INTEGRATION_ID,
  MAX_DATA_ROWS,
  SCHEMA_SAMPLE_ROWS,
} from '../constants';
import {
  batchGetValues,
  buildRange,
  getNumberFormats,
  getSpreadsheetProperties,
  getValues,
  type IFormatRange,
  type ISheetProperties,
  type TNumberFormatType,
} from '../google/sheets-client';
import type { IGsheetConfig, SheetRecord } from '../types';
import { buildSchema, type ISheetSchema } from './sheet-schema';
import { convertCell } from './sheet-values';

export interface ISheetTable {
  schema: ISheetSchema;
  rows: SheetRecord[];
  title: string;
  truncated: boolean;
}

export interface ISheetSample {
  schema: ISheetSchema;
  tab: ISheetProperties;
  sampledRows: number;
}

export interface IBookSample {
  primary: ISheetSample;
  others: ISheetSample[];
}

/**
 * Reads a full spreadsheet tab (up to {@link MAX_DATA_ROWS}) as typed records.
 * Reads the primary configured tab when `sheetId` is `null`, or the secondary
 * tab matching `sheetId`.
 */
export async function readTable(
  config: IGsheetConfig,
  sheetId: number | null,
  signal?: AbortSignal,
): Promise<ISheetTable> {
  const properties = await getSpreadsheetProperties(config, signal);
  const tab =
    sheetId === null
      ? resolveTab(config, properties.sheets)
      : resolveTabById(config, properties.sheets, sheetId);
  const layout = planLayout(config, tab);
  const read = splitRows(
    config,
    layout,
    await getValues(config, tableRange(layout, MAX_DATA_ROWS), signal),
  );

  const range = formatRange(read);
  const formats =
    range === null
      ? []
      : ((await getNumberFormats(config, [range], signal)).get(tab.sheetId) ??
        []);
  const schema = schemaOf(config, read, formats, sheetId === null);

  const truncated = read.dataRows.length >= MAX_DATA_ROWS;
  if (truncated) {
    console.warn(
      `[${INTEGRATION_ID}] tab "${tab.title}" of spreadsheet ` +
        `${config.spreadsheetId} was read up to the ${MAX_DATA_ROWS}-row limit; ` +
        'rows below that are not in the answer.',
    );
  }

  return {
    schema,
    title: tab.title,
    truncated,
    rows: toRecords(read.dataRows, schema),
  };
}

/**
 * Samples the primary tab and all additional grid tabs in batched API calls for
 * instance provisioning.
 */
export async function readBookSample(
  config: IGsheetConfig,
  signal?: AbortSignal,
): Promise<IBookSample> {
  const properties = await getSpreadsheetProperties(config, signal);
  const primaryLayout = planLayout(
    config,
    resolveTab(config, properties.sheets),
  );
  const otherLayouts = properties.sheets
    .filter(
      (tab) =>
        tab.sheetId !== primaryLayout.tab.sheetId &&
        tab.sheetType === 'GRID' &&
        !isPastEnd(config, tab),
    )
    .map((tab) => planLayout(config, tab));

  const layouts = [primaryLayout, ...otherLayouts];
  const values = await batchGetValues(
    config,
    layouts.map((layout) => tableRange(layout, SCHEMA_SAMPLE_ROWS)),
    signal,
  );
  const [primary, ...others] = layouts.map((layout, index) =>
    splitRows(config, layout, values[index]),
  );

  assertSampleable(config, primary);
  const tables = others.filter((read) => isTable(config, read));

  const ranges = [primary, ...tables]
    .map(formatRange)
    .filter((range): range is IFormatRange => range !== null);
  const formats =
    ranges.length === 0
      ? new Map<number, Array<Array<TNumberFormatType | null>>>()
      : await getNumberFormats(config, ranges, signal);

  const sampleOf = (
    read: IReadRows,
    isConfiguredTab: boolean,
  ): ISheetSample => ({
    tab: read.layout.tab,
    sampledRows: read.dataRows.length,
    schema: schemaOf(
      config,
      read,
      formats.get(read.layout.tab.sheetId) ?? [],
      isConfiguredTab,
    ),
  });

  return {
    primary: sampleOf(primary, true),
    others: tables.map((read) => sampleOf(read, false)),
  };
}

/**
 * Resolves the primary tab: `sheet_name` when configured, else `#gid=...` from
 * a pasted URL (falling back to the first tab if stale), else the first tab.
 */
export function resolveTab(
  config: IGsheetConfig,
  sheets: ISheetProperties[],
): ISheetProperties {
  if (sheets.length === 0) {
    throw new Error(`Spreadsheet ${config.spreadsheetId} has no tabs`);
  }

  if (config.sheetName !== null) {
    const byName = sheets.find((sheet) => sheet.title === config.sheetName);
    if (byName === undefined) {
      throw new Error(
        `Tab "${config.sheetName}" is not in this spreadsheet. Available: ` +
          sheets.map((sheet) => sheet.title).join(', '),
      );
    }
    return byName;
  }

  if (config.sheetGid !== null) {
    const byGid = sheets.find((sheet) => sheet.sheetId === config.sheetGid);
    if (byGid !== undefined) {
      return byGid;
    }
  }

  return sheets[0];
}

interface ITableLayout {
  tab: ISheetProperties;
  firstRow: number;
  firstDataRow: number;
}

interface IReadRows {
  layout: ITableLayout;
  values: unknown[][];
  headerRow: unknown[] | null;
  dataRows: unknown[][];
}

function resolveTabById(
  config: IGsheetConfig,
  sheets: ISheetProperties[],
  sheetId: number,
): ISheetProperties {
  const tab = sheets.find((sheet) => sheet.sheetId === sheetId);
  if (tab === undefined) {
    throw new Error(
      `Tab with gid ${sheetId} is no longer in spreadsheet ${config.spreadsheetId}. ` +
        'Re-save the instance to refresh its list of tabs.',
    );
  }
  return tab;
}

function planLayout(
  config: IGsheetConfig,
  tab: ISheetProperties,
): ITableLayout {
  if (isPastEnd(config, tab)) {
    throw new Error(
      `skip_rows (${config.skipRows}) is past the end of tab "${tab.title}", which has ${tab.rowCount} rows`,
    );
  }
  const firstRow = config.skipRows + 1;
  return {
    tab,
    firstRow,
    firstDataRow: firstRow + (config.isFirstRowHeader ? 1 : 0),
  };
}

function isPastEnd(config: IGsheetConfig, tab: ISheetProperties): boolean {
  return tab.rowCount > 0 && config.skipRows + 1 > tab.rowCount;
}

function tableRange(layout: ITableLayout, rowBudget: number): string {
  const { tab } = layout;
  return buildRange(
    tab.title,
    layout.firstRow,
    clampRow(tab, layout.firstDataRow + rowBudget - 1),
    tab.columnCount,
  );
}

function clampRow(tab: ISheetProperties, row: number): number {
  return tab.rowCount > 0 ? Math.min(row, tab.rowCount) : row;
}

function splitRows(
  config: IGsheetConfig,
  layout: ITableLayout,
  values: unknown[][],
): IReadRows {
  if (!config.isFirstRowHeader) {
    return { layout, values, headerRow: null, dataRows: values };
  }
  return {
    layout,
    values,
    headerRow: values[0] ?? [],
    dataRows: values.slice(1),
  };
}

function assertSampleable(config: IGsheetConfig, read: IReadRows): void {
  const { title } = read.layout.tab;
  if (read.values.length === 0) {
    throw new Error(
      `Tab "${title}" is empty from row ${read.layout.firstRow} down`,
    );
  }
  if (config.isFirstRowHeader && read.dataRows.length === 0) {
    throw new Error(
      `Tab "${title}" has a header row but no data rows. Turn ` +
        '"is_first_row_header" off if the first row is data.',
    );
  }
}

function isTable(config: IGsheetConfig, read: IReadRows): boolean {
  if (read.values.length === 0) {
    return false;
  }
  if (config.isFirstRowHeader && read.dataRows.length === 0) {
    return false;
  }
  return read.values.some((row) => row.length > 0);
}

function formatRange(read: IReadRows): IFormatRange | null {
  const sampleRows = Math.min(read.dataRows.length, SCHEMA_SAMPLE_ROWS);
  if (sampleRows === 0) {
    return null;
  }
  const { tab, firstDataRow } = read.layout;
  return {
    sheetId: tab.sheetId,
    a1Range: buildRange(
      tab.title,
      firstDataRow,
      clampRow(tab, firstDataRow + sampleRows - 1),
      tab.columnCount,
    ),
    firstRow: firstDataRow,
    firstColumn: 1,
  };
}

function schemaOf(
  config: IGsheetConfig,
  read: IReadRows,
  formats: Array<Array<TNumberFormatType | null>>,
  isConfiguredTab: boolean,
): ISheetSchema {
  return buildSchema({
    rows: read.dataRows,
    headerRow: read.headerRow,
    formats,
    identityColumn: config.identityColumn,
    identityRequired: isConfiguredTab,
  });
}

function toRecords(
  dataRows: unknown[][],
  schema: ISheetSchema,
): SheetRecord[] {
  const types = schema.fields.map((field) => field.type);

  return dataRows.map((row) => {
    const record: SheetRecord = {};
    for (let index = 0; index < schema.names.length; index++) {
      record[schema.names[index]] = convertCell(
        row[index],
        types[index],
        schema.dateKinds[index],
      );
    }
    return record;
  });
}
