import { WAREHOUSES_QUERY_API_PATH, createUnleashedClient } from './api/unleashed-client';
import { parseUnleashedConfig } from './config';
import type { ITestConnectionContext, ITestConnectionResult } from './types';

export async function testConnection(context: ITestConnectionContext): Promise<ITestConnectionResult> {
  const client = createUnleashedClient(parseUnleashedConfig(context?.config ?? {}));
  const data = await client.queryRaw(
    WAREHOUSES_QUERY_API_PATH,
    {
      result: {
        expressions: [{ alias: 'warehouses', expression: { type: 'aggregate', action: 'count' } }],
      },
    },
    context?.signal,
  );
  const rows = Array.isArray(data) ? (data as Array<Record<string, unknown>>) : [];
  const count = typeof rows[0]?.warehouses === 'number' ? ` (${rows[0].warehouses} warehouses)` : '';
  return { detail: `Unleashed: connected${count}` };
}
