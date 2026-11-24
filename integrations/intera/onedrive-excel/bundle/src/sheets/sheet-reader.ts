import {
  INTEGRATION_ID,
  MAX_DATA_ROWS,
  SCHEMA_SAMPLE_ROWS,
} from '../constants';
import {
  downloadWorkbook,
  getDriveItem,
  type IDriveItem,
} from '../graph/drive-client';
import type { IExcelConfig, SheetRecord } from '../types';
import {
  openWorkbook,
  type IWorkbook,
  type IWorksheetRef,
} from '../xlsx/workbook';
import { readWorksheetGrid, type IWorksheetGrid } from '../xlsx/worksheet';
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
  title: string;
  fileName: string;
  sampledRows: number;
}

export interface IOpenedWorkbook {
  item: IDriveItem;
  workbook: IWorkbook;
}

/** Downloads (or reuses cached) workbook bytes and parses workbook structure. */
export async function openConfiguredWorkbook(
  config: IExcelConfig,
  signal?: AbortSignal,
): Promise<IOpenedWorkbook> {
  const item = await getDriveItem(config, signal);
  const bytes = await downloadWorkbook(config, item, signal);
  return { item, workbook: openWorkbook(bytes, item.name) };
}

/**
 * Resolves the target worksheet: `sheet_name` when configured (even if hidden),
 * otherwise the first visible worksheet.
 */
export function resolveSheet(
  config: IExcelConfig,
  workbook: IWorkbook,
  fileName: string,
): IWorksheetRef {
  const { sheets } = workbook;
  if (sheets.length === 0) {
    throw new Error(`"${fileName}" has no worksheets`);
  }

  if (config.sheetName !== null) {
    const byName = sheets.find((sheet) => sheet.name === config.sheetName);
    if (byName === undefined) {
      throw new Error(
        `Worksheet "${config.sheetName}" is not in "${fileName}". Available: ` +
          sheets.map((sheet) => sheet.name).join(', '),
      );
    }
    return byName;
  }

  const visible = sheets.find((sheet) => sheet.state === 'visible');
  return visible ?? sheets[0];
}

/** Reads the configured worksheet (up to {@link MAX_DATA_ROWS}) as typed records. */
export async function readTable(
  config: IExcelConfig,
  signal?: AbortSignal,
): Promise<ISheetTable> {
  const opened = await openConfiguredWorkbook(config, signal);
  const read = readRows(config, opened, MAX_DATA_ROWS);
  const schema = buildSchemaFor(config, read);
  const title = read.layout.sheet.name;

  const truncated = read.dataRows.length >= MAX_DATA_ROWS;
  if (truncated) {
    console.warn(
      `[${INTEGRATION_ID}] worksheet "${title}" of "${opened.item.name}" was ` +
        `read up to the ${MAX_DATA_ROWS}-row limit; rows below that are not in the answer.`,
    );
  }

  return {
    schema,
    title,
    truncated,
    rows: toRecords(
      read.dataRows,
      read.dataKinds,
      schema,
      opened.workbook.date1904,
    ),
  };
}

/** Reads header and bounded sample rows of the configured worksheet for provisioning. */
export async function readSample(
  config: IExcelConfig,
  signal?: AbortSignal,
): Promise<ISheetSample> {
  const opened = await openConfiguredWorkbook(config, signal);
  const read = readRows(config, opened, SCHEMA_SAMPLE_ROWS);
  const title = read.layout.sheet.name;

  if (read.grid.values.length === 0) {
    throw new Error(
      `Worksheet "${title}" is empty from row ${read.layout.firstRow} down`,
    );
  }
  if (config.isFirstRowHeader && read.dataRows.length === 0) {
    throw new Error(
      `Worksheet "${title}" has a header row but no data rows. Turn ` +
        '"is_first_row_header" off if the first row is data.',
    );
  }

  return {
    schema: buildSchemaFor(config, read),
    title,
    fileName: opened.item.name,
    sampledRows: read.dataRows.length,
  };
}

interface ITableLayout {
  sheet: IWorksheetRef;
  firstRow: number;
  firstDataRow: number;
}

interface IReadRows {
  layout: ITableLayout;
  grid: IWorksheetGrid;
  headerRow: unknown[] | null;
  dataRows: unknown[][];
  dataKinds: IWorksheetGrid['dateKinds'];
}

function readRows(
  config: IExcelConfig,
  opened: IOpenedWorkbook,
  rowBudget: number,
): IReadRows {
  const sheet = resolveSheet(config, opened.workbook, opened.item.name);
  const firstRow = config.skipRows + 1;
  const layout: ITableLayout = {
    sheet,
    firstRow,
    firstDataRow: firstRow + (config.isFirstRowHeader ? 1 : 0),
  };

  const grid = readWorksheetGrid(
    opened.workbook,
    sheet,
    layout.firstRow,
    layout.firstDataRow + rowBudget - 1,
  );

  if (!config.isFirstRowHeader) {
    return {
      layout,
      grid,
      headerRow: null,
      dataRows: grid.values,
      dataKinds: grid.dateKinds,
    };
  }
  return {
    layout,
    grid,
    headerRow: grid.values[0] ?? [],
    dataRows: grid.values.slice(1),
    dataKinds: grid.dateKinds.slice(1),
  };
}

function buildSchemaFor(config: IExcelConfig, read: IReadRows): ISheetSchema {
  return buildSchema({
    rows: read.dataRows,
    headerRow: read.headerRow,
    formats: read.dataKinds,
    identityColumn: config.identityColumn,
  });
}

function toRecords(
  dataRows: unknown[][],
  dataKinds: IWorksheetGrid['dateKinds'],
  schema: ISheetSchema,
  date1904: boolean,
): SheetRecord[] {
  const types = schema.fields.map((field) => field.type);

  return dataRows.map((row, rowIndex) => {
    const kinds = dataKinds[rowIndex];
    const record: SheetRecord = {};
    for (let index = 0; index < schema.names.length; index++) {
      record[schema.names[index]] = convertCell(
        row[index],
        types[index],
        schema.dateKinds[index],
        kinds[index] ?? null,
        date1904,
      );
    }
    return record;
  });
}
