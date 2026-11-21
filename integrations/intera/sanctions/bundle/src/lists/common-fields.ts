import type { IDataSourceFieldDescriptor } from '../types';

/**
 * The columns every list of this package carries under the same names and
 * with the same meaning, so a check written against one list — "name LIKE
 * x, subject type company" — can be pointed at the next one unchanged. A
 * list-specific column goes into that list's own field list, after these.
 *
 * Grain: one row per NAME VARIANT of a listed subject, with the subject's own
 * attributes repeated on each. A person listed under five transliterations is
 * five rows; `countDistinct(entity_id)` counts subjects.
 */
export const SUBJECT_TYPES = ['individual', 'company'] as const;

export const COMMON_FIELDS: IDataSourceFieldDescriptor[] = [
  {
    name: 'name_id',
    type: 'string',
    label: 'Name variant id (unique per row)',
    identity: true,
  },
  { name: 'entity_id', type: 'string', label: 'Listed subject id' },
  {
    name: 'list_source',
    type: 'string',
    label: 'List the row comes from (EU, …)',
  },
  {
    name: 'list_generated_at',
    type: 'date',
    label: 'When the publisher generated the list file',
  },
  {
    name: 'subject_type',
    type: 'enum',
    label: 'Subject type',
    enumValues: [...SUBJECT_TYPES],
  },
  { name: 'name', type: 'string', label: 'Name (this variant)' },
  {
    name: 'name_normalized',
    type: 'string',
    label: 'Name, lower case without diacritics or punctuation — for search',
  },
  {
    name: 'primary_name',
    type: 'string',
    label: 'Name the subject is listed under (its first variant)',
  },
  { name: 'programme', type: 'string', label: 'Sanctions programme' },
  { name: 'listed_on', type: 'date', label: 'Designation date' },
  {
    name: 'birth_dates',
    type: 'string',
    label: 'Birth dates (YYYY-MM-DD, or the year alone; several separated by "; ")',
  },
  {
    name: 'citizenship_countries',
    type: 'string',
    label: 'Citizenship country codes (ISO 3166 alpha-2, "; "-separated)',
  },
  {
    name: 'address_countries',
    type: 'string',
    label: 'Address country codes (ISO 3166 alpha-2, "; "-separated)',
  },
  {
    name: 'imo_numbers',
    type: 'string',
    label: 'IMO numbers (company or ship, "; "-separated)',
  },
  { name: 'remark', type: 'string', label: 'Remarks' },
];
