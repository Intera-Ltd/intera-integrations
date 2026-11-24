/**
 * Streaming pull tokenizer for SpreadsheetML XML parts inside `.xlsx` archives.
 *
 * Compares element and attribute names by local name (handling both default and
 * prefixed Open XML namespaces), normalizes XML line endings before entity
 * decoding, and decodes OOXML `_xHHHH_` character escapes.
 */

export type TXmlTokenKind = 'open' | 'close' | 'empty';

const ENTITY_RE = /&(?:#x([0-9a-fA-F]+)|#(\d+)|(amp|lt|gt|quot|apos));/g;
const NAMED_ENTITIES: Record<string, string> = {
  amp: '&',
  lt: '<',
  gt: '>',
  quot: '"',
  apos: "'",
};
const XSTRING_ESCAPE_RE = /_x([0-9A-Fa-f]{4})_/g;

export class XmlCursor {
  /** Local name of the current tag. */
  name = '';
  kind: TXmlTokenKind = 'open';
  private attrStart = 0;
  private attrEnd = 0;
  private position = 0;

  constructor(private readonly xml: string) {}

  /** Advances to the next element tag. Returns `false` at end of document. */
  next(): boolean {
    const xml = this.xml;
    for (;;) {
      const lt = xml.indexOf('<', this.position);
      if (lt < 0) {
        this.position = xml.length;
        return false;
      }
      const marker = xml.charCodeAt(lt + 1);

      if (marker === 0x3f /* ? */) {
        this.position = skipPast(xml, lt, '?>');
        continue;
      }
      if (marker === 0x21 /* ! */) {
        if (xml.startsWith('<!--', lt)) {
          this.position = skipPast(xml, lt, '-->');
        } else if (xml.startsWith('<![CDATA[', lt)) {
          this.position = skipPast(xml, lt, ']]>');
        } else {
          this.position = skipPast(xml, lt, '>');
        }
        continue;
      }

      if (marker === 0x2f /* / */) {
        const gt = xml.indexOf('>', lt);
        if (gt < 0) {
          throw new Error('Workbook XML is truncated');
        }
        this.kind = 'close';
        this.name = localName(xml.slice(lt + 2, gt).trim());
        this.attrStart = this.attrEnd = gt;
        this.position = gt + 1;
        return true;
      }

      const gt = findTagEnd(xml, lt + 1);
      let nameEnd = lt + 1;
      while (nameEnd < gt && !isNameTerminator(xml.charCodeAt(nameEnd))) {
        nameEnd++;
      }
      this.name = localName(xml.slice(lt + 1, nameEnd));
      const selfClosing = xml.charCodeAt(gt - 1) === 0x2f; /* / */
      this.kind = selfClosing ? 'empty' : 'open';
      this.attrStart = nameEnd;
      this.attrEnd = selfClosing ? gt - 1 : gt;
      this.position = gt + 1;
      return true;
    }
  }

  /** Reads an attribute of the current tag by local name, entity-decoded. */
  attr(name: string): string | null {
    const xml = this.xml;
    let index = this.attrStart;
    const end = this.attrEnd;
    while (index < end) {
      while (index < end && isWhitespace(xml.charCodeAt(index))) index++;
      const nameStart = index;
      while (
        index < end &&
        xml.charCodeAt(index) !== 0x3d /* = */ &&
        !isWhitespace(xml.charCodeAt(index))
      ) {
        index++;
      }
      const attrName = localName(xml.slice(nameStart, index));
      while (index < end && xml.charCodeAt(index) !== 0x3d) index++;
      index++;
      while (index < end && isWhitespace(xml.charCodeAt(index))) index++;
      const quote = xml[index];
      if (quote !== '"' && quote !== "'") {
        return null;
      }
      const valueEnd = xml.indexOf(quote, index + 1);
      if (valueEnd < 0 || valueEnd > end) {
        return null;
      }
      if (attrName === name) {
        return decodeEntities(xml.slice(index + 1, valueEnd));
      }
      index = valueEnd + 1;
    }
    return null;
  }

  /** Reads leaf element character data up to the next tag. */
  text(): string {
    if (this.kind !== 'open') {
      return '';
    }
    const xml = this.xml;
    let result = '';
    for (;;) {
      const lt = xml.indexOf('<', this.position);
      if (lt < 0) {
        result += decodeCharacterData(xml.slice(this.position));
        this.position = xml.length;
        return result;
      }
      result += decodeCharacterData(xml.slice(this.position, lt));
      if (xml.startsWith('<![CDATA[', lt)) {
        const end = xml.indexOf(']]>', lt);
        if (end < 0) {
          throw new Error('Workbook XML is truncated');
        }
        result += xml.slice(lt + 9, end).replace(/\r\n?/g, '\n');
        this.position = end + 3;
        continue;
      }
      if (xml.startsWith('<!--', lt)) {
        this.position = skipPast(xml, lt, '-->');
        continue;
      }
      this.position = lt;
      return result;
    }
  }
}

/** Decodes OOXML `ST_Xstring` `_xHHHH_` character escapes. */
export function decodeXstring(text: string): string {
  if (text.indexOf('_x') < 0) {
    return text;
  }
  return text.replace(XSTRING_ESCAPE_RE, (_, hex: string) =>
    String.fromCharCode(parseInt(hex, 16)),
  );
}

function skipPast(xml: string, from: number, terminator: string): number {
  const end = xml.indexOf(terminator, from);
  if (end < 0) {
    throw new Error('Workbook XML is truncated');
  }
  return end + terminator.length;
}

function findTagEnd(xml: string, from: number): number {
  let quote = 0;
  for (let index = from; index < xml.length; index++) {
    const code = xml.charCodeAt(index);
    if (quote !== 0) {
      if (code === quote) quote = 0;
    } else if (code === 0x22 || code === 0x27) {
      quote = code;
    } else if (code === 0x3e /* > */) {
      return index;
    }
  }
  throw new Error('Workbook XML is truncated');
}

function localName(qualified: string): string {
  const colon = qualified.indexOf(':');
  return colon < 0 ? qualified : qualified.slice(colon + 1);
}

function isWhitespace(code: number): boolean {
  return code === 0x20 || code === 0x09 || code === 0x0a || code === 0x0d;
}

function isNameTerminator(code: number): boolean {
  return isWhitespace(code) || code === 0x2f /* / */ || code === 0x3e; /* > */
}

function decodeCharacterData(text: string): string {
  const normalized =
    text.indexOf('\r') < 0 ? text : text.replace(/\r\n?/g, '\n');
  return decodeEntities(normalized);
}

export function decodeEntities(text: string): string {
  if (text.indexOf('&') < 0) {
    return text;
  }
  return text.replace(ENTITY_RE, (_match, hex, decimal, named) => {
    if (named !== undefined) return NAMED_ENTITIES[named as string];
    const codePoint =
      hex !== undefined ? parseInt(hex as string, 16) : Number(decimal);
    if (
      !Number.isInteger(codePoint) ||
      codePoint < 0 ||
      codePoint > 0x10ffff ||
      (codePoint >= 0xd800 && codePoint <= 0xdfff)
    ) {
      return '\uFFFD';
    }
    return String.fromCodePoint(codePoint);
  });
}
