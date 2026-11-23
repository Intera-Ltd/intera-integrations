/**
 * A minimal, dependency-free XML reader — enough for a data export, nothing
 * more.
 *
 * Plugins ship without `node_modules`, and Node has no XML parser of its own.
 * What an export like the EU FSF file needs is elements, attributes and text;
 * so that is all this builds. Not supported, deliberately: DTDs and custom
 * entities (skipped), namespaces beyond dropping the prefix, mixed content
 * order (an element's text is its text nodes concatenated).
 *
 * Attributes are scanned quote to quote rather than cut at the next `>`: XML
 * allows a raw `>` inside an attribute value, and a regex over `<[^>]*>` would
 * split such a tag in two.
 */

export interface IXmlElement {
  /** Local name — any namespace prefix is dropped. */
  name: string;
  attributes: Record<string, string>;
  children: IXmlElement[];
  /** Concatenated text content, entity-decoded, not trimmed. */
  text: string;
}

const PREDEFINED_ENTITIES: Record<string, string> = {
  amp: '&',
  lt: '<',
  gt: '>',
  quot: '"',
  apos: "'",
};

function decodeEntities(text: string): string {
  if (!text.includes('&')) {
    return text;
  }
  return text.replace(/&(#x[0-9a-fA-F]+|#[0-9]+|[A-Za-z]+);/g, (match, body: string) => {
    if (body.startsWith('#x')) {
      return String.fromCodePoint(parseInt(body.slice(2), 16));
    }
    if (body.startsWith('#')) {
      return String.fromCodePoint(parseInt(body.slice(1), 10));
    }
    return PREDEFINED_ENTITIES[body] ?? match;
  });
}

function localName(qualified: string): string {
  const colon = qualified.indexOf(':');
  return colon < 0 ? qualified : qualified.slice(colon + 1);
}

function isNameChar(code: number): boolean {
  // Anything that is not whitespace, `/`, `>` or `=` — permissive on purpose:
  // a reader of trusted exports has no reason to reject a Unicode name.
  return !(
    code === 0x20 ||
    code === 0x09 ||
    code === 0x0a ||
    code === 0x0d ||
    code === 0x2f ||
    code === 0x3e ||
    code === 0x3d
  );
}

function isWhitespace(code: number): boolean {
  return code === 0x20 || code === 0x09 || code === 0x0a || code === 0x0d;
}

/**
 * Parses a whole document and returns its root element. Throws on a document
 * that is not well-formed where that matters for reading it (an unclosed tag,
 * a closing tag that does not match, no root at all).
 */
export function parseXml(xml: string): IXmlElement {
  const stack: IXmlElement[] = [];
  let root: IXmlElement | null = null;
  let position = xml.charCodeAt(0) === 0xfeff ? 1 : 0;

  const fail = (message: string): never => {
    throw new Error(`XML: ${message} at offset ${position}`);
  };

  const appendText = (text: string): void => {
    const current = stack[stack.length - 1];
    if (current !== undefined) {
      current.text += text;
    }
  };

  while (position < xml.length) {
    const tagStart = xml.indexOf('<', position);
    if (tagStart < 0) {
      appendText(decodeEntities(xml.slice(position)));
      break;
    }
    if (tagStart > position) {
      appendText(decodeEntities(xml.slice(position, tagStart)));
    }
    position = tagStart;

    if (xml.startsWith('<?', position)) {
      const end = xml.indexOf('?>', position);
      if (end < 0) fail('unterminated processing instruction');
      position = end + 2;
      continue;
    }
    if (xml.startsWith('<!--', position)) {
      const end = xml.indexOf('-->', position);
      if (end < 0) fail('unterminated comment');
      position = end + 3;
      continue;
    }
    if (xml.startsWith('<![CDATA[', position)) {
      const end = xml.indexOf(']]>', position);
      if (end < 0) fail('unterminated CDATA section');
      appendText(xml.slice(position + 9, end));
      position = end + 3;
      continue;
    }
    if (xml.startsWith('<!', position)) {
      // DOCTYPE and friends. An internal subset (`[ … ]`) may itself hold `>`.
      const bracket = xml.indexOf('[', position);
      const close = xml.indexOf('>', position);
      if (close < 0) fail('unterminated declaration');
      if (bracket >= 0 && bracket < close) {
        const end = xml.indexOf(']>', bracket);
        if (end < 0) fail('unterminated internal subset');
        position = end + 2;
      } else {
        position = close + 1;
      }
      continue;
    }

    if (xml.charCodeAt(position + 1) === 0x2f) {
      const end = xml.indexOf('>', position);
      if (end < 0) fail('unterminated closing tag');
      const name = localName(xml.slice(position + 2, end).trim());
      const open = stack.pop();
      if (open === undefined || open.name !== name) {
        fail(`closing tag </${name}> does not match <${open?.name ?? ''}>`);
      }
      position = end + 1;
      continue;
    }

    // Opening or self-closing tag.
    position += 1;
    const nameStart = position;
    while (position < xml.length && isNameChar(xml.charCodeAt(position))) {
      position += 1;
    }
    const element: IXmlElement = {
      name: localName(xml.slice(nameStart, position)),
      attributes: {},
      children: [],
      text: '',
    };
    if (element.name.length === 0) fail('empty tag name');

    let selfClosing = false;
    for (;;) {
      while (position < xml.length && isWhitespace(xml.charCodeAt(position))) {
        position += 1;
      }
      if (position >= xml.length) fail(`unterminated tag <${element.name}>`);
      const code = xml.charCodeAt(position);
      if (code === 0x3e) {
        position += 1;
        break;
      }
      if (code === 0x2f && xml.charCodeAt(position + 1) === 0x3e) {
        position += 2;
        selfClosing = true;
        break;
      }

      const attrStart = position;
      while (position < xml.length && isNameChar(xml.charCodeAt(position))) {
        position += 1;
      }
      const attrName = localName(xml.slice(attrStart, position));
      while (isWhitespace(xml.charCodeAt(position))) position += 1;
      if (xml.charCodeAt(position) !== 0x3d) {
        fail(`attribute "${attrName}" of <${element.name}> has no value`);
      }
      position += 1;
      while (isWhitespace(xml.charCodeAt(position))) position += 1;
      const quote = xml[position];
      if (quote !== '"' && quote !== "'") {
        fail(`attribute "${attrName}" of <${element.name}> is not quoted`);
      }
      const valueEnd = xml.indexOf(quote, position + 1);
      if (valueEnd < 0) fail(`unterminated attribute "${attrName}"`);
      element.attributes[attrName] = decodeEntities(
        xml.slice(position + 1, valueEnd),
      );
      position = valueEnd + 1;
    }

    const parent = stack[stack.length - 1];
    if (parent !== undefined) {
      parent.children.push(element);
    } else if (root === null) {
      root = element;
    } else {
      fail('more than one root element');
    }
    if (!selfClosing) {
      stack.push(element);
    }
  }

  if (stack.length > 0) {
    throw new Error(`XML: <${stack[stack.length - 1].name}> is never closed`);
  }
  if (root === null) {
    throw new Error('XML: the document has no root element');
  }
  return root;
}

/** The children of `element` named `name`, in document order. */
export function childrenNamed(element: IXmlElement, name: string): IXmlElement[] {
  return element.children.filter((child) => child.name === name);
}

/** The first child named `name`, if any. */
export function firstChild(
  element: IXmlElement,
  name: string,
): IXmlElement | undefined {
  return element.children.find((child) => child.name === name);
}
