import { createSoft1Client } from './api/soft1-client';
import { parsePluginConfig } from './config';
import type { ITestConnectionContext, ITestConnectionResult } from './types';

export async function testConnection(context: ITestConnectionContext): Promise<ITestConnectionResult> {
  const config = parsePluginConfig(context?.config ?? {});

  const data = await createSoft1Client(config).query('/api/Source/companies/query', {}, context?.signal);
  const rows = Array.isArray(data) ? (data as Array<Record<string, unknown>>) : [];
  const name = typeof rows[0]?.name === 'string' ? rows[0].name : undefined;

  return { detail: name ? `Soft1: ${name}` : 'Soft1: connected' };
}
