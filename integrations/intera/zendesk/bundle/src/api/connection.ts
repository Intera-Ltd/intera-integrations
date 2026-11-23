import { STATUS_TIMEOUT_MS } from '../constants';
import type { IOAuthStatus } from '../types';
import type { ZendeskClient } from './zendesk-client';

/** Whether the API holds a Zendesk grant. A client set to non-expiring tokens has an access token and no refresh token. */
export function isSignedIn(status: IOAuthStatus): boolean {
  return status.has_access_token || status.has_refresh_token;
}

/**
 * What an admin has to do before the API can read Zendesk, as one message for Test connection — including a fresh
 * single-use sign-in link once the API knows its redirect URL. Asking for the link takes the access token, so a wrong
 * one fails here with the API's own reason.
 */
export async function describeMissingSignIn(client: ZendeskClient, status: IOAuthStatus): Promise<string> {
  if (!status.configured) {
    return (
      'Intera.Zendesk.Api has no Zendesk OAuth client configured: set ZENDESK_CLIENT_ID and ZENDESK_CLIENT_SECRET in its ' +
      '.env and redeploy it.'
    );
  }

  if (!status.redirect_uri) {
    return (
      'Not connected to Zendesk yet, and INTERA has not registered its callback address with Intera.Zendesk.Api. Check ' +
      'the registration status of the "oauth-callback" address among this instance’s external addresses (INTERA ' +
      'needs a public base address for it), send it again from there, then test the connection again.'
    );
  }

  const data = await client.get('/oauth/authorize-url', STATUS_TIMEOUT_MS);
  const url = typeof data === 'object' && data !== null && typeof (data as { url?: unknown }).url === 'string'
    ? (data as { url: string }).url
    : undefined;
  if (!url) {
    throw new Error('Zendesk API returned no sign-in link for /oauth/authorize-url');
  }

  return (
    'Not connected to Zendesk yet. 1) The OAuth client in Zendesk (Admin Center → Apps and integrations → APIs → ' +
    `OAuth clients) must list this redirect URL: ${status.redirect_uri} ` +
    '2) Open this link within 15 minutes, sign in as the Zendesk user the dashboards should read as, and Allow: ' +
    `${url} 3) Test the connection again, and save the instance once more to pick up the ticket custom fields.`
  );
}
