import { createZohoInventoryClient } from './api/zoho-inventory-client';
import { parseZohoInventoryConfig } from './config';
import type {
  IIntegrationTestConnectionContext,
  IIntegrationTestConnectionResult,
} from './types';

export async function testConnection(
  context: IIntegrationTestConnectionContext,
): Promise<IIntegrationTestConnectionResult> {
  const client = createZohoInventoryClient(parseZohoInventoryConfig(context?.config ?? {}));
  const organization = await client.getOrganization();

  const name = organization.name ?? organization.organization_id ?? 'unnamed organization';
  const currency = organization.currency_code ? `, ${organization.currency_code}` : '';

  return { detail: `Zoho Inventory: ${name}${currency}` };
}
