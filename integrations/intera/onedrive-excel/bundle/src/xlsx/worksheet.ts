import type { TDateKind } from './number-format';
import { readPartText, type IWorkbook, type IWorksheetRef } from './workbook';
import { decodeXstring, XmlCursor } from './xml';

/**
 * Reads cell values and per-cell style date kinds over a bounded physical row
 * window (`firstRow..lastRow`, 1-based inclusive) of one worksheet.
 */
export interface IWorksheetGrid {
  /** Row `i` is physical row `firstRow + i`. Ragged; empty rows are `[]`. */
  values: unknown[][];
  /** Same shape as `values`, carrying each cell's number-format date kind. */
  dateKinds: Array<Array<TDateKind | null>>;
}

export function readWorksheetGrid(
  workbook: IWorkbook,
  sheet: IWorksheetRef,
  firstRow: number,
  lastRow: number,
): IWorksheetGrid {
  const xml = readPartText(workbook.archive, sheet.partName);
  if (xml === null) {
    throw new Error(
      `Worksheet "${sheet.name}" is missing from the workbook (${sheet.partName})`,
    );
  }

  const values: unknown[][] = [];
  const dateKinds: Array<Array<TDateKind | null>> = [];
  const { sharedStrings, styleDateKinds } = workbook;

  const cursor = new XmlCursor(xml);
  let inSheetData = false;
  let rowNumber = 0;
  let rowValues: unknown[] | null = null;
  let rowKinds: Array<TDateKind | null> | null = null;

  let column = -1;
  let cellType: string | null = null;
  let cellKind: TDateKind | null = null;
  let valueText: string | null = null;
  let inlineText: string | null = null;
  let phoneticDepth = 0;

  const commitRow = (): void => {
    if (rowValues !== null && rowValues.length > 0) {
      const index = rowNumber - firstRow;
      while (values.length < index) {
        values.push([]);
        dateKinds.push([]);
      }
      values.push(rowValues);
      dateKinds.push(rowKinds ?? []);
    }
    rowValues = null;
    rowKinds = null;
  };

  while (cursor.next()) {
    const { name, kind } = cursor;

    if (name === 'sheetData') {
      if (kind === 'open') {
        inSheetData = true;
        continue;
      }
      break;
    }
    if (!inSheetData) continue;

    if (name === 'row') {
      if (kind === 'close') {
        commitRow();
        continue;
      }
      const explicit = Number(cursor.attr('r'));
      rowNumber =
        Number.isInteger(explicit) && explicit > 0 ? explicit : rowNumber + 1;
      column = -1;
      if (rowNumber > lastRow) break;
      rowValues = rowNumber >= firstRow ? [] : null;
      rowKinds = rowValues === null ? null : [];
      if (kind === 'empty') commitRow();
      continue;
    }

    if (rowValues === null || rowKinds === null) continue;

    if (name === 'c') {
      if (kind === 'close') {
        const value = cellValue(cellType, valueText, inlineText, sharedStrings);
        if (value !== undefined) {
          rowValues[column] = value;
          rowKinds[column] = cellKind;
        }
        continue;
      }
      const reference = cursor.attr('r');
      const explicit = reference === null ? -1 : columnIndex(reference);
      column = explicit >= 0 ? explicit : column + 1;
      cellType = cursor.attr('t');
      const style = Number(cursor.attr('s') ?? '0');
      cellKind = styleDateKinds[Number.isInteger(style) ? style : 0] ?? null;
      valueText = null;
      inlineText = null;
      phoneticDepth = 0;
      continue;
    }

    if (name === 'v' && kind !== 'close') {
      valueText = cursor.text();
    } else if (name === 'is') {
      if (kind === 'open') inlineText = '';
      else if (kind === 'empty') inlineText = '';
    } else if (name === 'rPh') {
      if (kind === 'open') phoneticDepth++;
      else if (kind === 'close') phoneticDepth--;
    } else if (
      name === 't' &&
      kind !== 'close' &&
      inlineText !== null &&
      phoneticDepth === 0
    ) {
      inlineText += cursor.text();
    }
  }
  commitRow();

  return { values, dateKinds };
}

function cellValue(
  type: string | null,
  valueText: string | null,
  inlineText: string | null,
  sharedStrings: string[],
): unknown {
  switch (type) {
    case 's': {
      if (valueText === null) return undefined;
      const text = sharedStrings[Number(valueText)];
      return text === undefined || text === '' ? undefined : text;
    }
    case 'inlineStr':
      return inlineText === null || inlineText === ''
        ? undefined
        : decodeXstring(inlineText);
    case 'str':
      return valueText === null || valueText === ''
        ? undefined
        : decodeXstring(valueText);
    case 'b':
      return valueText === null ? undefined : valueText.trim() === '1';
    case 'e':
    case 'd':
      return valueText === null || valueText === '' ? undefined : valueText;
    default: {
      if (valueText === null || valueText.trim() === '') return undefined;
      const number = Number(valueText);
      return Number.isFinite(number) ? number : valueText;
    }
  }
}

function columnIndex(reference: string): number {
  let index = 0;
  let letters = 0;
  for (let position = 0; position < reference.length; position++) {
    const code = reference.charCodeAt(position) & ~0x20;
    if (code < 0x41 || code > 0x5a) break;
    index = index * 26 + (code - 0x40);
    letters++;
  }
  return letters === 0 ? -1 : index - 1;
}
