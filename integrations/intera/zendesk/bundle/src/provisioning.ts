import { isSignedIn } from './api/connection';
import { createZendeskClient } from './api/zendesk-client';
import { parsePluginConfig } from './config';
import { SOURCE_ID_PREFIX } from './constants';
import { ZENDESK_SOURCES } from './data-sources/zendesk-sources.generated';
import type {
  DataSourceFieldType,
  IDataSourceFieldDescriptor,
  IIntegrationProvisionContext,
  IIntegrationProvisionResult,
} from './types';

/** The one source whose rows carry the account's custom fields (`custom_field_<id>` columns). */
const TICKETS_API_SOURCE = 'tickets';

interface ICustomFieldDescriptor {
  name: string;
  label: string;
  type: 'Text' | 'Number' | 'Date' | 'Flag';
  custom_type?: string | null;
}

const TYPES: Record<ICustomFieldDescriptor['type'], DataSourceFieldType> = {
  Text: 'string',
  Number: 'number',
  Date: 'date',
  Flag: 'boolean',
};

/**
 * Called by Core when an instance is created or updated, before the row is persisted.
 *
 * Two jobs, as in intera-jira-integration. It proves the whole chain — this plugin reaches Intera.Zendesk.Api, the
 * access token is right, the API's Zendesk credential works — because `GET /api/Schema/custom-fields` reads the
 * account's ticket fields from Zendesk, and anything wrong throws and rejects the save with the API's own reason.
 * And it appends the ticket custom fields to `zendesk_tickets`: Core replaces a source's static `fields` wholesale
 * with what this returns, so the static list goes first.
 *
 * Which custom fields: only those the API's `Zendesk:TicketCustomFields` names (`*` for all; empty, the default,
 * for none, in which case nothing is appended). The result is a snapshot: a field added in Zendesk later appears
 * after the instance is saved again. It is queryable before that — the API admits any `custom_field_<id>` name.
 *
 * **Before anyone has signed in to Zendesk, the save must not fail** (4.0): the sign-in's redirect URL is the address
 * Core creates for this instance, so it exists only once the instance does. Then only the plugin → API leg and the
 * access token are proven (`GET /api/Schema/sources` needs the token and no Zendesk call), the static field list stays,
 * and the custom fields come with the first save after signing in — which Test connection asks for.
 */
export async function provisionInstance(
  context: IIntegrationProvisionContext,
): Promise<IIntegrationProvisionResult> {
  const client = createZendeskClient(parsePluginConfig(context.config));

  if (!isSignedIn(await client.getOAuthStatus())) {
    await client.get('/api/Schema/sources');
    return { dataSources: [] };
  }

  const data = await client.get('/api/Schema/custom-fields');
  const customFields = Array.isArray(data) ? (data as ICustomFieldDescriptor[]) : [];

  const tickets = ZENDESK_SOURCES.find((source) => source.apiSource === TICKETS_API_SOURCE);
  if (!tickets) {
    return { dataSources: [] };
  }

  const known = new Set(tickets.fields.map((field) => field.name));
  const custom = customFields
    .filter((field) => typeof field.name === 'string' && !known.has(field.name))
    .map(
      (field): IDataSourceFieldDescriptor => ({
        name: field.name,
        type: TYPES[field.type] ?? 'string',
        label: field.label,
      }),
    );

  return {
    dataSources: [{ id: `${SOURCE_ID_PREFIX}${tickets.apiSource}`, fields: [...tickets.fields, ...custom] }],
  };
}
