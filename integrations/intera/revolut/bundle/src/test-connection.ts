import { isSignedIn, resolvePendingAuthorization } from './api/connection';
import { createRevolutClient } from './api/revolut-client';
import { parseRevolutConfig } from './config';
import type { ITestConnectionContext, ITestConnectionResult } from './types';

export async function testConnection(context: ITestConnectionContext): Promise<ITestConnectionResult> {
  const client = createRevolutClient(parseRevolutConfig(context?.config ?? {}));

  const status = await client.getOAuthStatus(context?.signal);
  if (!isSignedIn(status)) {
    return resolvePendingAuthorization(client, status, context);
  }

  const data = await client.queryAccountsRaw({}, context?.signal);
  const rows = Array.isArray(data) ? data : [];

  return {
    status: 'ok',
    detail: rows.length > 0 ? `Revolut Business: ${rows.length} account(s)` : 'Revolut Business: connected',
  };
}
