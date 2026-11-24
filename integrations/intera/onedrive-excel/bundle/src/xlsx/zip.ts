import * as zlib from 'node:zlib';

/**
 * Dependency-free ZIP central-directory reader for `.xlsx` Open Packaging
 * Conventions archives, supporting stored, deflate, and ZIP64 records while
 * enforcing output byte caps directly on `zlib.inflateRawSync`.
 */

interface IZipEntry {
  name: string;
  method: number;
  flags: number;
  compressedSize: number;
  uncompressedSize: number;
  localHeaderOffset: number;
}

const EOCD_SIGNATURE = 0x06054b50;
const ZIP64_EOCD_LOCATOR_SIGNATURE = 0x07064b50;
const ZIP64_EOCD_SIGNATURE = 0x06064b50;
const CENTRAL_HEADER_SIGNATURE = 0x02014b50;
const LOCAL_HEADER_SIGNATURE = 0x04034b50;
const ZIP64_EXTRA_FIELD_ID = 0x0001;

const METHOD_STORED = 0;
const METHOD_DEFLATE = 8;

const EOCD_MIN_LENGTH = 22;
const EOCD_MAX_SEARCH = EOCD_MIN_LENGTH + 0xffff;

export class ZipArchive {
  /** Keyed by lower-cased part name because OPC part names are case-insensitive. */
  private readonly entries = new Map<string, IZipEntry>();

  constructor(private readonly bytes: Buffer) {
    for (const entry of readCentralDirectory(bytes)) {
      this.entries.set(normalizePartName(entry.name), entry);
    }
  }

  has(partName: string): boolean {
    return this.entries.has(normalizePartName(partName));
  }

  /**
   * Decompresses an archive entry up to `maxBytes`, returning `null` if absent.
   */
  read(partName: string, maxBytes: number): Buffer | null {
    const entry = this.entries.get(normalizePartName(partName));
    if (entry === undefined) {
      return null;
    }
    if ((entry.flags & 0x1) !== 0) {
      throw new Error(`Workbook part "${entry.name}" is encrypted`);
    }
    if (entry.uncompressedSize > maxBytes) {
      throw partTooLarge(entry.name, maxBytes);
    }

    const data = this.entryData(entry);
    if (entry.method === METHOD_STORED) {
      if (data.byteLength > maxBytes) {
        throw partTooLarge(entry.name, maxBytes);
      }
      return data;
    }
    if (entry.method !== METHOD_DEFLATE) {
      throw new Error(
        `Workbook part "${entry.name}" uses zip compression method ${entry.method}, which this reader does not support`,
      );
    }
    try {
      return zlib.inflateRawSync(data, { maxOutputLength: maxBytes });
    } catch (err) {
      if ((err as NodeJS.ErrnoException).code === 'ERR_BUFFER_TOO_LARGE') {
        throw partTooLarge(entry.name, maxBytes);
      }
      throw new Error(
        `Workbook part "${entry.name}" is damaged: ${err instanceof Error ? err.message : String(err)}`,
      );
    }
  }

  private entryData(entry: IZipEntry): Buffer {
    const offset = entry.localHeaderOffset;
    if (
      offset + 30 > this.bytes.length ||
      this.bytes.readUInt32LE(offset) !== LOCAL_HEADER_SIGNATURE
    ) {
      throw new Error(
        `Workbook part "${entry.name}" has no valid local header`,
      );
    }
    const nameLength = this.bytes.readUInt16LE(offset + 26);
    const extraLength = this.bytes.readUInt16LE(offset + 28);
    const start = offset + 30 + nameLength + extraLength;
    const end = start + entry.compressedSize;
    if (end > this.bytes.length) {
      throw new Error(
        `Workbook part "${entry.name}" runs past the end of the file`,
      );
    }
    return this.bytes.subarray(start, end);
  }
}

function readCentralDirectory(bytes: Buffer): IZipEntry[] {
  const eocd = findEndOfCentralDirectory(bytes);
  let entryCount = bytes.readUInt16LE(eocd + 10);
  let directoryOffset = bytes.readUInt32LE(eocd + 16);

  if (entryCount === 0xffff || directoryOffset === 0xffffffff) {
    const locator = eocd - 20;
    if (
      locator < 0 ||
      bytes.readUInt32LE(locator) !== ZIP64_EOCD_LOCATOR_SIGNATURE
    ) {
      throw new Error(
        'Workbook is not a valid zip file (ZIP64 locator missing)',
      );
    }
    const record = readUInt64(bytes, locator + 8);
    if (
      record + 56 > bytes.length ||
      bytes.readUInt32LE(record) !== ZIP64_EOCD_SIGNATURE
    ) {
      throw new Error(
        'Workbook is not a valid zip file (ZIP64 record missing)',
      );
    }
    entryCount = readUInt64(bytes, record + 32);
    directoryOffset = readUInt64(bytes, record + 48);
  }

  const entries: IZipEntry[] = [];
  let offset = directoryOffset;
  for (let index = 0; index < entryCount; index++) {
    if (
      offset + 46 > bytes.length ||
      bytes.readUInt32LE(offset) !== CENTRAL_HEADER_SIGNATURE
    ) {
      throw new Error(
        'Workbook is not a valid zip file (central directory damaged)',
      );
    }
    const flags = bytes.readUInt16LE(offset + 8);
    const method = bytes.readUInt16LE(offset + 10);
    let compressedSize = bytes.readUInt32LE(offset + 20);
    let uncompressedSize = bytes.readUInt32LE(offset + 24);
    const nameLength = bytes.readUInt16LE(offset + 28);
    const extraLength = bytes.readUInt16LE(offset + 30);
    const commentLength = bytes.readUInt16LE(offset + 32);
    let localHeaderOffset = bytes.readUInt32LE(offset + 42);

    const nameStart = offset + 46;
    const name = bytes.toString('utf8', nameStart, nameStart + nameLength);

    const extraStart = nameStart + nameLength;
    const extraEnd = extraStart + extraLength;
    let cursor = extraStart;
    while (cursor + 4 <= extraEnd) {
      const id = bytes.readUInt16LE(cursor);
      const size = bytes.readUInt16LE(cursor + 2);
      if (id === ZIP64_EXTRA_FIELD_ID) {
        let field = cursor + 4;
        if (uncompressedSize === 0xffffffff) {
          uncompressedSize = readUInt64(bytes, field);
          field += 8;
        }
        if (compressedSize === 0xffffffff) {
          compressedSize = readUInt64(bytes, field);
          field += 8;
        }
        if (localHeaderOffset === 0xffffffff) {
          localHeaderOffset = readUInt64(bytes, field);
        }
        break;
      }
      cursor += 4 + size;
    }

    entries.push({
      name,
      method,
      flags,
      compressedSize,
      uncompressedSize,
      localHeaderOffset,
    });
    offset = extraEnd + commentLength;
  }
  return entries;
}

function findEndOfCentralDirectory(bytes: Buffer): number {
  const lowest = Math.max(0, bytes.length - EOCD_MAX_SEARCH);
  for (
    let offset = bytes.length - EOCD_MIN_LENGTH;
    offset >= lowest;
    offset--
  ) {
    if (bytes.readUInt32LE(offset) === EOCD_SIGNATURE) {
      return offset;
    }
  }
  throw new Error(
    'Workbook is not a valid zip file (no end of central directory)',
  );
}

function readUInt64(bytes: Buffer, offset: number): number {
  return (
    bytes.readUInt32LE(offset) + bytes.readUInt32LE(offset + 4) * 0x100000000
  );
}

function partTooLarge(name: string, maxBytes: number): Error {
  return new Error(
    `Workbook part "${name}" decompresses to more than ${Math.round(maxBytes / (1024 * 1024))} MB, ` +
      'which this integration does not read',
  );
}

function normalizePartName(name: string): string {
  return name.replace(/^\/+/, '').toLowerCase();
}
