import { ACCOUNTS_QUERY_API_PATH, createSalesforceClient } from './api/salesforce-client';
import { parseSalesforceConfig } from './config';
import type { ITestConnectionContext, ITestConnectionResult } from './types';

export async function testConnection(context: ITestConnectionContext): Promise<ITestConnectionResult> {
  const client = createSalesforceClient(parseSalesforceConfig(context?.config ?? {}));
  const data = await client.queryRaw(ACCOUNTS_QUERY_API_PATH, {
    result: {
      expressions: [{ alias: 'accounts', expression: { type: 'aggregate', action: 'count' } }],
    },
  });
  const rows = Array.isArray(data) ? (data as Array<Record<string, unknown>>) : [];
  const count = typeof rows[0]?.accounts === 'number' ? ` (${rows[0].accounts} accounts)` : '';
  return { detail: `Salesforce: connected${count}` };
}
