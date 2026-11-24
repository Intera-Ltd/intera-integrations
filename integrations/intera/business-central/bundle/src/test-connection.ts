import { createBusinessCentralClient } from './api/business-central-client';
import { parseBusinessCentralConfig } from './config';
import type { ITestConnectionContext, ITestConnectionResult } from './types';

export async function testConnection(context: ITestConnectionContext): Promise<ITestConnectionResult> {
  const config = parseBusinessCentralConfig(context?.config ?? {});
  const data = await createBusinessCentralClient(config).queryCompaniesRaw({}, context?.signal);
  const rows = Array.isArray(data) ? (data as Array<Record<string, unknown>>) : [];
  const first = rows[0];
  const label =
    typeof first?.display_name === 'string' && first.display_name.trim().length > 0
      ? first.display_name.trim()
      : typeof first?.name === 'string' && first.name.trim().length > 0
        ? first.name.trim()
        : undefined;

  if (rows.length > 0) {
    const suffix = label ? ` (${label})` : '';
    return {
      detail: `Business Central: ${rows.length} ${rows.length === 1 ? 'company' : 'companies'}${suffix}`,
    };
  }

  return { detail: 'Business Central: connected' };
}
