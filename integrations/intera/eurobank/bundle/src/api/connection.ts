import { OAUTH_CALLBACK_HANDLER_KEY, REGISTRATION_TIMEOUT_MS, STATUS_TIMEOUT_MS } from '../constants';
import type { IOAuthStatus, ITestConnectionContext, ITestConnectionResult } from '../types';
import type { EurobankClient } from './eurobank-client';

export function isSignedIn(status: IOAuthStatus): boolean {
  return status.has_access_token || status.has_refresh_token;
}

export async function resolvePendingAuthorization(
  client: EurobankClient,
  status: IOAuthStatus,
  context?: ITestConnectionContext,
): Promise<ITestConnectionResult> {
  if (!status.configured) {
    throw new Error(
      'Intera.Eurobank.Api has no Eurobank OAuth client configured: set EUROBANK__CLIENTID and EUROBANK__CLIENTSECRET in the container settings and redeploy.',
    );
  }

  let redirectUri = status.redirect_uri?.trim() ?? '';
  if (!redirectUri) {
    const webhookUrl =
      context?.webhookUrls?.find((w) => w.handlerKey === OAUTH_CALLBACK_HANDLER_KEY)?.url?.trim() ?? '';
    if (webhookUrl) {
      await client.put('/oauth/redirect-uri', { redirect_uri: webhookUrl }, REGISTRATION_TIMEOUT_MS, context?.signal);
      redirectUri = webhookUrl;
    }
  }

  if (!redirectUri) {
    throw new Error(
      'Not connected to Eurobank yet, and INTERA has no public callback address registered for "oauth-callback". ' +
        'Configure a public base URL in INTERA Core (or set EUROBANK__REDIRECTURI in the container settings) and test the connection again.',
    );
  }

  const data = await client.get('/oauth/authorize-url', STATUS_TIMEOUT_MS, context?.signal);
  const url =
    typeof data === 'object' && data !== null && typeof (data as { url?: unknown }).url === 'string'
      ? (data as { url: string }).url
      : undefined;
  if (!url) {
    throw new Error('Eurobank API returned no sign-in link for /oauth/authorize-url');
  }

  return {
    status: 'needs_authorization',
    authorizeUrl: url,
    detail:
      `Authorization required. Ensure your Eurobank Developer Portal application lists ${redirectUri} as a Redirect URI, ` +
      'open the sign-in link within 15 minutes to approve access, then test the connection again.',
  };
}
