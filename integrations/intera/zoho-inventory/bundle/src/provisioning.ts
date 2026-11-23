import { createZohoInventoryClient } from './api/zoho-inventory-client';
import { parseZohoInventoryConfig } from './config';
import { ZOHO_INVENTORY_SOURCES } from './data-sources/zoho-inventory-data-sources.source';
import type {
  DataSourceFieldType,
  IDataSourceFieldDescriptor,
  IIntegrationProvisionContext,
  IIntegrationProvisionResult,
} from './types';

/**
 * Called by Core when an instance is created or updated, before the row is persisted.
 *
 * Two jobs. It proves the whole chain — this plugin reaches Intera.ZohoInventory.Api, the access
 * token is right, and the API's Zoho refresh token works — because `GET /api/Schema` reads Zoho, and
 * anything wrong throws and rejects the save with the API's own reason. And it adds the
 * organization's custom fields to each source's field list: Core replaces a source's static
 * `fields` wholesale with what this returns, so the static list goes first and the custom fields
 * are appended.
 *
 * The result is a snapshot of the organization's custom-field settings, so a field added in Zoho
 * later appears after the instance is saved again. It is queryable before that: the API admits any
 * `cf_…` name.
 */
export async function provisionInstance(
  context: IIntegrationProvisionContext,
): Promise<IIntegrationProvisionResult> {
  const client = createZohoInventoryClient(parseZohoInventoryConfig(context.config));
  const schema = await client.getSchema();

  return {
    dataSources: ZOHO_INVENTORY_SOURCES.map((source) => {
      const known = new Set(source.fields.map((field) => field.name));
      const custom = (schema.custom_fields[source.schemaKey] ?? [])
        .filter((field) => typeof field.name === 'string' && !known.has(field.name))
        .map(
          (field): IDataSourceFieldDescriptor => ({
            name: field.name,
            type: toFieldType(field.type),
            label: field.label,
          }),
        );

      return { id: source.id, fields: [...source.fields, ...custom] };
    }),
  };
}

function toFieldType(type: string): DataSourceFieldType {
  return type === 'number' || type === 'date' || type === 'boolean' ? type : 'string';
}
