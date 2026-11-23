import { describeMissingSignIn, isSignedIn } from './api/connection';
import { createZendeskClient } from './api/zendesk-client';
import { parsePluginConfig } from './config';
import type { ITestConnectionContext, ITestConnectionResult } from './types';

export async function testConnection(context: ITestConnectionContext): Promise<ITestConnectionResult> {
  const client = createZendeskClient(parsePluginConfig(context?.config ?? {}));

  const status = await client.getOAuthStatus();
  if (!isSignedIn(status)) {
    throw new Error(await describeMissingSignIn(client, status));
  }

  const data = await client.query('/api/Source/groups/query', {
    result: {
      expressions: [
        { alias: 'zendesk_subdomain', expression: { type: 'field', field: 'zendesk_subdomain' } },
        { alias: 'groups', expression: { type: 'aggregate', action: 'count' } },
      ],
      groupBy: ['zendesk_subdomain'],
    },
  });
  const rows = Array.isArray(data) ? (data as Array<Record<string, unknown>>) : [];
  const account = typeof rows[0]?.zendesk_subdomain === 'string' ? rows[0].zendesk_subdomain : undefined;
  const groups = typeof rows[0]?.groups === 'number' ? ` (${rows[0].groups} groups)` : '';

  return { detail: account ? `Zendesk: ${account}${groups}` : 'Zendesk: connected, no groups visible' };
}
