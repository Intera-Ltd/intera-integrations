import { OAUTH_CALLBACK_HANDLER_KEY, REGISTRATION_TIMEOUT_MS, STATUS_TIMEOUT_MS } from '../constants';
import type { IOAuthStatus, ITestConnectionContext, ITestConnectionResult } from '../types';
import type { ZendeskClient } from './zendesk-client';

/** Whether the API holds a Zendesk grant. A client set to non-expiring tokens has an access token and no refresh token. */
export function isSignedIn(status: IOAuthStatus): boolean {
  return status.has_access_token || status.has_refresh_token;
}

/**
 * Resolves the `needs_authorization` handshake when the service does not hold a Zendesk grant yet.
 */
export async function resolvePendingAuthorization(
  client: ZendeskClient,
  status: IOAuthStatus,
  context?: ITestConnectionContext,
): Promise<ITestConnectionResult> {
  if (!status.configured) {
    throw new Error(
      'Intera.Zendesk.Api has no Zendesk OAuth client configured: set ZENDESK__CLIENTID and ZENDESK__CLIENTSECRET in the container settings and redeploy.',
    );
  }

  let redirectUri = status.redirect_uri?.trim() ?? '';
  if (!redirectUri) {
    const webhookUrl = context?.webhookUrls?.find((w) => w.handlerKey === OAUTH_CALLBACK_HANDLER_KEY)?.url?.trim() ?? '';
    if (webhookUrl) {
      await client.put('/oauth/redirect-uri', { redirect_uri: webhookUrl }, REGISTRATION_TIMEOUT_MS);
      redirectUri = webhookUrl;
    }
  }

  if (!redirectUri) {
    throw new Error(
      'Not connected to Zendesk yet, and INTERA has no public callback address registered for "oauth-callback". ' +
        'Configure a public base URL in INTERA Core (or set ZENDESK__REDIRECTURI in the container settings) and test the connection again.',
    );
  }

  const data = await client.get('/oauth/authorize-url', STATUS_TIMEOUT_MS);
  const url =
    typeof data === 'object' && data !== null && typeof (data as { url?: unknown }).url === 'string'
      ? (data as { url: string }).url
      : undefined;
  if (!url) {
    throw new Error('Zendesk API returned no sign-in link for /oauth/authorize-url');
  }

  return {
    status: 'needs_authorization',
    authorizeUrl: url,
    detail:
      'Authorization required. Ensure your OAuth client in Zendesk (Admin Center -> Apps and integrations -> APIs -> ' +
      `OAuth clients) lists ${redirectUri}, open the sign-in link within 15 minutes, then test the connection and save once more to pick up ticket custom fields.`,
  };
}
