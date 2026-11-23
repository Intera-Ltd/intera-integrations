import { MULTI_VALUE_SEPARATOR } from '../../constants';
import type { IDataSourceFieldDescriptor, SanctionsRecord } from '../../types';
import { COMMON_FIELDS } from '../common-fields';
import { downloadText } from '../download';
import type { ISanctionsList } from '../list-cache';
import { normalizeName } from '../names';
import { childrenNamed, firstChild, parseXml, type IXmlElement } from '../xml';

/**
 * The EU Consolidated List of Financial Sanctions (FSF), read from its full
 * XML export (schema 1.1, root `<export generationDate=…>`, one
 * `<sanctionEntity>` per listed subject).
 *
 * What the file holds, measured on the 2026-08-05 export: 6 234 subjects —
 * 4 462 `person`, 1 772 `enterprise`, no third kind — and 31 053 name
 * variants. **There is no vessel subject type.** The 41 identifications typed
 * `imo` ("IMO (vessel identification)") are, all but two, the IMO *company*
 * numbers of shipping companies (5xxxxxx / 0xxxxxx); only `Unica` 8514306 and
 * `New Konk` 9036387 carry what looks like a ship's number, and both are
 * listed as enterprises. The EU's vessel designations (e.g. Annex XLII to
 * Regulation 833/2014) are published in the acts themselves and are not part
 * of this file. So vessels are searchable here only through `imo_numbers`,
 * and only as far as the file happens to carry them.
 */
export const EU_LIST_SOURCE = 'EU';

export const EU_FIELDS: IDataSourceFieldDescriptor[] = [
  ...COMMON_FIELDS,
  { name: 'eu_reference_number', type: 'string', label: 'EU reference number' },
  { name: 'un_reference_number', type: 'string', label: 'UN reference number' },
  {
    name: 'designation_details',
    type: 'string',
    label: 'Designation details',
  },
  {
    name: 'regulation_number',
    type: 'string',
    label: 'Legal act (number and OJ reference)',
  },
  {
    name: 'regulation_type',
    type: 'string',
    label: 'Legal act type (regulation, decision, amendment, …)',
  },
  {
    name: 'regulation_published_on',
    type: 'date',
    label: 'Legal act publication date',
  },
  {
    name: 'regulation_in_force_on',
    type: 'date',
    label: 'Legal act entry into force',
  },
  { name: 'regulation_url', type: 'string', label: 'Legal act URL (EUR-Lex)' },
  { name: 'first_name', type: 'string', label: 'First name' },
  { name: 'middle_name', type: 'string', label: 'Middle name' },
  { name: 'last_name', type: 'string', label: 'Last name' },
  { name: 'name_language', type: 'string', label: 'Language of this variant' },
  {
    name: 'name_strong',
    type: 'boolean',
    label: 'Strong name variant (false: a weak alias)',
  },
  { name: 'gender', type: 'string', label: 'Gender (M / F)' },
  { name: 'title', type: 'string', label: 'Title' },
  { name: 'function', type: 'string', label: 'Function / position' },
  { name: 'birth_places', type: 'string', label: 'Places of birth' },
  { name: 'citizenships', type: 'string', label: 'Citizenships (country names)' },
  { name: 'addresses', type: 'string', label: 'Addresses' },
  {
    name: 'identifications',
    type: 'string',
    label: 'Identity documents and registration numbers ("type: number (country)")',
  },
];

/** The FSF's subject kinds, in the vocabulary every list here shares. */
const SUBJECT_TYPE_BY_CODE: Record<string, string> = {
  person: 'individual',
  enterprise: 'company',
};

export async function loadEuList(url: string): Promise<ISanctionsList> {
  const xml = await downloadText(url);
  return readEuList(xml);
}

/** Separate from the download so it can be run over a saved file. */
export function readEuList(xml: string): ISanctionsList {
  let root: IXmlElement;
  try {
    root = parseXml(xml);
  } catch (error) {
    // An HTML error or login page is the usual culprit, and "closing tag
    // </head> does not match <meta>" alone does not say so.
    const reason = error instanceof Error ? error.message : String(error);
    throw new Error(
      `The EU sanctions file is not well-formed XML (${reason}) — the ` +
        'download address may be answering with a web page instead of the export.',
    );
  }
  if (root.name !== 'export') {
    throw new Error(
      `The EU sanctions file has root <${root.name}>, expected <export> — ` +
        'the download address may be answering with something else.',
    );
  }

  const generatedAt = blankToNull(root.attributes.generationDate);
  const rows: SanctionsRecord[] = [];
  let entityCount = 0;
  for (const child of root.children) {
    if (child.name === 'sanctionEntity') {
      entityCount += 1;
      readEntity(child, generatedAt, rows);
      child.children.length = 0;
    }
  }
  root.children.length = 0;
  return { rows, entityCount, generatedAt };
}

function readEntity(
  entity: IXmlElement,
  generatedAt: string | null,
  rows: SanctionsRecord[],
): void {
  const attributes = entity.attributes;
  const subjectCode = firstChild(entity, 'subjectType')?.attributes.code ?? '';
  const regulation = firstChild(entity, 'regulation');
  const aliases = childrenNamed(entity, 'nameAlias');

  // Everything that belongs to the subject rather than to one of its names is
  // built once and shared by every row of the subject.
  const shared: SanctionsRecord = {
    entity_id: blankToNull(attributes.logicalId),
    list_source: EU_LIST_SOURCE,
    list_generated_at: generatedAt,
    subject_type: SUBJECT_TYPE_BY_CODE[subjectCode] ?? blankToNull(subjectCode),
    primary_name: blankToNull(aliases[0]?.attributes.wholeName),
    programme: blankToNull(regulation?.attributes.programme),
    listed_on: blankToNull(attributes.designationDate),
    birth_dates: joinValues(childrenNamed(entity, 'birthdate').map(birthDate)),
    citizenship_countries: joinValues(
      childrenNamed(entity, 'citizenship').map(countryCode),
    ),
    address_countries: joinValues(
      childrenNamed(entity, 'address').map(countryCode),
    ),
    imo_numbers: joinValues(
      childrenNamed(entity, 'identification')
        .filter((id) => id.attributes.identificationTypeCode === 'imo')
        .map((id) => id.attributes.number),
    ),
    remark: joinValues(childrenNamed(entity, 'remark').map((r) => r.text)),
    eu_reference_number: blankToNull(attributes.euReferenceNumber),
    un_reference_number: blankToNull(attributes.unitedNationId),
    designation_details: blankToNull(attributes.designationDetails),
    regulation_number: blankToNull(regulation?.attributes.numberTitle),
    regulation_type: blankToNull(regulation?.attributes.regulationType),
    regulation_published_on: blankToNull(regulation?.attributes.publicationDate),
    regulation_in_force_on: blankToNull(
      regulation?.attributes.entryIntoForceDate,
    ),
    regulation_url: regulation
      ? blankToNull(firstChild(regulation, 'publicationUrl')?.text)
      : null,
    birth_places: joinValues(childrenNamed(entity, 'birthdate').map(birthPlace)),
    citizenships: joinValues(
      childrenNamed(entity, 'citizenship').map((c) => c.attributes.countryDescription),
    ),
    addresses: joinValues(childrenNamed(entity, 'address').map(address)),
    identifications: joinValues(
      childrenNamed(entity, 'identification').map(identification),
    ),
  };

  for (const alias of aliases) {
    const name = blankToNull(alias.attributes.wholeName);
    rows.push({
      ...shared,
      name_id: blankToNull(alias.attributes.logicalId),
      name,
      name_normalized: name === null ? null : detachString(normalizeName(name)),
      first_name: blankToNull(alias.attributes.firstName),
      middle_name: blankToNull(alias.attributes.middleName),
      last_name: blankToNull(alias.attributes.lastName),
      name_language: blankToNull(alias.attributes.nameLanguage),
      name_strong: parseFlag(alias.attributes.strong),
      gender: blankToNull(alias.attributes.gender),
      title: blankToNull(alias.attributes.title),
      function: blankToNull(alias.attributes.function),
    });
  }
}

/**
 * `YYYY-MM-DD` when the day is known, the year alone when only that is, a
 * `from-to` range when the act gives one. Never a synthesized January 1st: a
 * year-only birth date matched as a full date would be a false precision
 * nobody could see.
 */
function birthDate(element: IXmlElement): string | undefined {
  const a = element.attributes;
  let value = blankToNull(a.birthdate) ?? blankToNull(a.year);
  if (value === null && (a.yearRangeFrom || a.yearRangeTo)) {
    value = `${a.yearRangeFrom ?? ''}-${a.yearRangeTo ?? ''}`;
  }
  if (value === null) {
    return undefined;
  }
  return a.circa === 'true' ? `circa ${value}` : value;
}

function birthPlace(element: IXmlElement): string | undefined {
  const a = element.attributes;
  return joinParts([a.place, a.city, a.region, knownCountry(a.countryDescription)]);
}

/** `00` is the file's "unknown country", which is not a country to filter on. */
function countryCode(element: IXmlElement): string | undefined {
  const code = element.attributes.countryIso2Code;
  return code && code !== '00' ? code : undefined;
}

function knownCountry(description: string | undefined): string | undefined {
  return description && description !== 'UNKNOWN' ? description : undefined;
}

function address(element: IXmlElement): string | undefined {
  const a = element.attributes;
  return joinParts([
    a.street,
    a.poBox ? `PO Box ${a.poBox}` : undefined,
    a.place,
    a.city,
    a.zipCode,
    a.region,
    knownCountry(a.countryDescription),
  ]);
}

function identification(element: IXmlElement): string | undefined {
  const a = element.attributes;
  const number = blankToNull(a.number) ?? blankToNull(a.latinNumber);
  if (number === null) {
    return undefined;
  }
  const kind = a.identificationTypeCode || 'id';
  const country = a.countryIso2Code && a.countryIso2Code !== '00'
    ? ` (${a.countryIso2Code})`
    : '';
  return `${kind}: ${number}${country}`;
}

function joinParts(parts: Array<string | undefined>): string | undefined {
  const present = parts
    .map((part) => part?.trim())
    .filter((part): part is string => !!part);
  return present.length > 0 ? present.join(', ') : undefined;
}

/** Distinct non-blank values, in document order, or null for none. */
function joinValues(values: Array<string | undefined>): string | null {
  const seen = new Set<string>();
  for (const value of values) {
    const trimmed = value?.trim();
    if (trimmed) {
      seen.add(trimmed);
    }
  }
  return seen.size > 0 ? detachString([...seen].join(MULTI_VALUE_SEPARATOR)) : null;
}

function blankToNull(value: string | undefined): string | null {
  const trimmed = value?.trim();
  return trimmed ? detachString(trimmed) : null;
}

function parseFlag(value: string | undefined): boolean | null {
  if (value === 'true') return true;
  if (value === 'false') return false;
  return null;
}

/**
 * Allocates an independent V8 string so substrings sliced out of the 25 MB XML
 * source cannot retain the entire XML buffer via SlicedString parent pointers.
 */
function detachString(value: string): string {
  return value.length === 0 ? '' : Buffer.from(value, 'utf8').toString('utf8');
}
