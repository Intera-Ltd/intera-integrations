import { isSignedIn, resolvePendingAuthorization } from './api/connection';
import { createEurobankClient } from './api/eurobank-client';
import { parseEurobankConfig } from './config';
import type { ITestConnectionContext, ITestConnectionResult } from './types';

export async function testConnection(context: ITestConnectionContext): Promise<ITestConnectionResult> {
  const client = createEurobankClient(parseEurobankConfig(context?.config ?? {}));

  const status = await client.getOAuthStatus(context?.signal);
  if (!isSignedIn(status)) {
    return resolvePendingAuthorization(client, status, context);
  }

  const data = await client.queryAccountsRaw({}, context?.signal);
  const rows = Array.isArray(data) ? data : [];

  return {
    status: 'ok',
    detail: rows.length > 0 ? `Eurobank: ${rows.length} account(s)` : 'Eurobank: connected',
  };
}
