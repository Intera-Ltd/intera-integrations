import { MAX_PART_BYTES } from '../constants';
import { classifyNumberFormat, type TDateKind } from './number-format';
import { decodeXstring, XmlCursor } from './xml';
import { ZipArchive } from './zip';

/**
 * Opens an `.xlsx` workbook archive through OPC relationships, loading the
 * worksheet list, 1900/1904 date-system flag, shared strings, and `cellXfs`
 * date-format classifications.
 */

export type TSheetState = 'visible' | 'hidden' | 'veryHidden';

export interface IWorksheetRef {
  name: string;
  state: TSheetState;
  /** Resolved ZIP part name of the worksheet XML. */
  partName: string;
}

export interface IWorkbook {
  /** Worksheets in tab order (excluding chart sheets). */
  sheets: IWorksheetRef[];
  /** True when the workbook uses the 1904 date system (`<workbookPr date1904>`). */
  date1904: boolean;
  sharedStrings: string[];
  /** Date kind per `cellXfs` style index (`<c s="…">`). */
  styleDateKinds: Array<TDateKind | null>;
  archive: ZipArchive;
}

const OLE_SIGNATURE = Buffer.from([
  0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1,
]);
const ZIP_SIGNATURE = Buffer.from([0x50, 0x4b, 0x03, 0x04]);

export function openWorkbook(bytes: Buffer, fileName: string): IWorkbook {
  if (bytes.subarray(0, OLE_SIGNATURE.length).equals(OLE_SIGNATURE)) {
    throw new Error(
      `"${fileName}" is either a legacy .xls workbook or an encrypted .xlsx ` +
        '(password-protected, or under a sensitivity label that encrypts it). ' +
        'Neither can be read without Excel — save it as a plain .xlsx.',
    );
  }
  if (!bytes.subarray(0, ZIP_SIGNATURE.length).equals(ZIP_SIGNATURE)) {
    throw new Error(`"${fileName}" is not an Excel workbook (.xlsx)`);
  }

  const archive = new ZipArchive(bytes);
  const workbookPart = findOfficeDocument(archive);
  if (workbookPart.toLowerCase().endsWith('.bin')) {
    throw new Error(
      `"${fileName}" is a binary workbook (.xlsb), which this integration does ` +
        'not read — save it as .xlsx.',
    );
  }

  const workbookXml = readPartText(archive, workbookPart);
  if (workbookXml === null) {
    throw new Error(`"${fileName}" has no workbook part (${workbookPart})`);
  }
  const relationships = readRelationships(archive, workbookPart);

  let date1904 = false;
  const sheets: IWorksheetRef[] = [];
  const cursor = new XmlCursor(workbookXml);
  while (cursor.next()) {
    if (cursor.kind === 'close') continue;
    if (cursor.name === 'workbookPr') {
      const value = cursor.attr('date1904');
      date1904 = value === '1' || value === 'true';
    } else if (cursor.name === 'sheet') {
      const relationship = relationships.get(cursor.attr('id') ?? '');
      if (
        relationship === undefined ||
        !relationship.type.endsWith('/worksheet')
      ) {
        continue;
      }
      const state = cursor.attr('state');
      sheets.push({
        name: cursor.attr('name') ?? '',
        state:
          state === 'hidden' || state === 'veryHidden' ? state : 'visible',
        partName: relationship.target,
      });
    }
  }

  let sharedStringsPart: string | null = null;
  let stylesPart: string | null = null;
  for (const relationship of relationships.values()) {
    if (relationship.type.endsWith('/sharedStrings')) {
      sharedStringsPart = relationship.target;
    }
    if (relationship.type.endsWith('/styles')) {
      stylesPart = relationship.target;
    }
  }

  return {
    sheets,
    date1904,
    sharedStrings:
      sharedStringsPart === null
        ? []
        : readSharedStrings(archive, sharedStringsPart),
    styleDateKinds:
      stylesPart === null ? [] : readStyleDateKinds(archive, stylesPart),
    archive,
  };
}

/** Reads an XML part as UTF-8 text (stripping any leading UTF-8 BOM), or `null` if absent. */
export function readPartText(
  archive: ZipArchive,
  partName: string,
): string | null {
  const bytes = archive.read(partName, MAX_PART_BYTES);
  if (bytes === null) {
    return null;
  }
  const start =
    bytes.length >= 3 &&
    bytes[0] === 0xef &&
    bytes[1] === 0xbb &&
    bytes[2] === 0xbf
      ? 3
      : 0;
  return bytes.toString('utf8', start);
}

interface IRelationship {
  type: string;
  target: string;
}

function findOfficeDocument(archive: ZipArchive): string {
  for (const relationship of readRelationships(archive, '').values()) {
    if (relationship.type.endsWith('/officeDocument')) {
      return relationship.target;
    }
  }
  if (archive.has('xl/workbook.bin')) return 'xl/workbook.bin';
  return 'xl/workbook.xml';
}

function readRelationships(
  archive: ZipArchive,
  sourcePart: string,
): Map<string, IRelationship> {
  const slash = sourcePart.lastIndexOf('/');
  const directory = slash < 0 ? '' : sourcePart.slice(0, slash);
  const fileName = sourcePart.slice(slash + 1);
  const relsPart = `${directory === '' ? '' : `${directory}/`}_rels/${fileName}.rels`;

  const relationships = new Map<string, IRelationship>();
  const xml = readPartText(archive, relsPart);
  if (xml === null) {
    return relationships;
  }
  const cursor = new XmlCursor(xml);
  while (cursor.next()) {
    if (cursor.kind === 'close' || cursor.name !== 'Relationship') continue;
    if (cursor.attr('TargetMode') === 'External') continue;
    const id = cursor.attr('Id');
    const target = cursor.attr('Target');
    if (id === null || target === null) continue;
    relationships.set(id, {
      type: cursor.attr('Type') ?? '',
      target: resolvePartName(directory, target),
    });
  }
  return relationships;
}

function resolvePartName(directory: string, target: string): string {
  const joined = target.startsWith('/')
    ? target.slice(1)
    : directory === ''
      ? target
      : `${directory}/${target}`;
  let decoded = joined;
  try {
    decoded = decodeURI(joined);
  } catch {
    // Keep literal target when malformed percent-encoding is present.
  }
  const segments: string[] = [];
  for (const segment of decoded.split('/')) {
    if (segment === '..') segments.pop();
    else if (segment !== '.' && segment !== '') segments.push(segment);
  }
  return segments.join('/');
}

function readSharedStrings(archive: ZipArchive, partName: string): string[] {
  const xml = readPartText(archive, partName);
  if (xml === null) {
    return [];
  }
  const strings: string[] = [];
  const cursor = new XmlCursor(xml);
  let current: string | null = null;
  let phoneticDepth = 0;
  while (cursor.next()) {
    switch (cursor.name) {
      case 'si':
        if (cursor.kind === 'open') current = '';
        else if (cursor.kind === 'empty') strings.push('');
        else if (current !== null) {
          strings.push(decodeXstring(current));
          current = null;
        }
        break;
      case 'rPh':
        if (cursor.kind === 'open') phoneticDepth++;
        else if (cursor.kind === 'close') phoneticDepth--;
        break;
      case 't':
        if (cursor.kind === 'open' && current !== null && phoneticDepth === 0) {
          current += cursor.text();
        }
        break;
    }
  }
  return strings;
}

function readStyleDateKinds(
  archive: ZipArchive,
  partName: string,
): Array<TDateKind | null> {
  const xml = readPartText(archive, partName);
  if (xml === null) {
    return [];
  }
  const formatCodes = new Map<number, string>();
  const styleFormatIds: number[] = [];
  let inCellXfs = false;

  const cursor = new XmlCursor(xml);
  while (cursor.next()) {
    if (cursor.name === 'cellXfs') {
      inCellXfs = cursor.kind === 'open';
    } else if (cursor.name === 'numFmt' && cursor.kind !== 'close') {
      const id = Number(cursor.attr('numFmtId'));
      const code = cursor.attr('formatCode');
      if (Number.isInteger(id) && code !== null) {
        formatCodes.set(id, code);
      }
    } else if (inCellXfs && cursor.name === 'xf' && cursor.kind !== 'close') {
      const id = Number(cursor.attr('numFmtId') ?? '0');
      styleFormatIds.push(Number.isInteger(id) ? id : 0);
    }
  }

  return styleFormatIds.map((id) =>
    classifyNumberFormat(id, formatCodes.get(id)),
  );
}
