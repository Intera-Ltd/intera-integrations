import { createXeroClient } from './api/xero-client';
import { parsePluginConfig } from './config';
import type { ITestConnectionContext, ITestConnectionResult } from './types';

export async function testConnection(context: ITestConnectionContext): Promise<ITestConnectionResult> {
  const config = parsePluginConfig(context?.config ?? {});

  const data = await createXeroClient(config).query('/api/Source/organisation/query', {});
  const rows = Array.isArray(data) ? (data as Array<Record<string, unknown>>) : [];
  const name = typeof rows[0]?.name === 'string' ? rows[0].name : undefined;
  const demo = rows[0]?.is_demo_company === true ? ' (Demo Company)' : '';

  return { detail: name ? `Xero: ${name}${demo}` : 'Xero: connected' };
}
