import { createZendeskClient } from './api/zendesk-client';
import { parsePluginConfig } from './config';
import { OAUTH_CALLBACK_HANDLER_KEY, REGISTRATION_TIMEOUT_MS } from './constants';
import type {
  IWebhookHandlerContext,
  IWebhookRegistrationContext,
  IWebhookRegistrationResult,
  IWebhookRequest,
  IWebhookResponse,
} from './types';

/**
 * Zendesk's OAuth sign-in, relayed through Core (4.0).
 *
 * Core gives the `oauth-callback` handler a public address, and that address is the redirect URL: the OAuth client in
 * Zendesk lists it, `registerWebhook` hands it to Intera.Zendesk.Api, which builds the sign-in link with it, and Zendesk
 * sends the browser back to it. `onWebhook` then forwards the redirect's parameters to the API's `/oauth/callback`,
 * which checks the single-use `state`, exchanges the code and keeps the grant in its own file. Core cannot keep it:
 * an instance's config is read-only to a plugin, and Zendesk rotates the refresh token on every use.
 *
 * So the API needs no public host of its own — only Core has to reach it, as it already does for every query.
 */

/** The only parameters Zendesk's redirect carries; nothing else is passed on to the API. */
const CALLBACK_PARAMETERS = ['code', 'state', 'error', 'error_description'] as const;

const NO_STORE = 'no-store';

/** Hands the handler's public address to the API as its redirect URL. Called by Core whenever the instance is saved. */
export async function registerWebhook(context: IWebhookRegistrationContext): Promise<IWebhookRegistrationResult> {
  if (context.handlerKey !== OAUTH_CALLBACK_HANDLER_KEY) {
    return { ok: false, message: `Unknown webhook handler "${context.handlerKey}"` };
  }

  try {
    const client = createZendeskClient(parsePluginConfig(context.config));
    await client.put('/oauth/redirect-uri', { redirect_uri: context.webhookUrl }, REGISTRATION_TIMEOUT_MS);

    return { ok: true, providerRef: context.webhookUrl };
  } catch (error) {
    // Core keeps neither `message` nor the exception's text, only a fixed label; Test connection reports the API's
    // own reason (it reads the same status), and the API logs it.
    return { ok: false, message: error instanceof Error ? error.message : String(error) };
  }
}

/** Zendesk's redirect, arriving at Core's public address: forwarded to the API and its page handed back as it is. */
export async function onWebhook(request: IWebhookRequest, context: IWebhookHandlerContext): Promise<IWebhookResponse> {
  if (context.handlerKey !== OAUTH_CALLBACK_HANDLER_KEY || request.method !== 'GET') {
    return { status: 405 };
  }

  const received = new URLSearchParams(request.rawQuery);
  const forwarded = new URLSearchParams();
  for (const name of CALLBACK_PARAMETERS) {
    const value = received.get(name);
    if (value !== null) {
      forwarded.set(name, value);
    }
  }

  if (!forwarded.has('code') && !forwarded.has('error')) {
    return page(400, 'Zendesk did not connect', 'This address only receives Zendesk’s sign-in redirect, and this request carried none.');
  }

  try {
    const answer = await createZendeskClient(parsePluginConfig(context.config)).relayOAuthCallback(forwarded);

    return {
      status: answer.status,
      headers: { 'content-type': answer.contentType, 'cache-control': NO_STORE },
      body: answer.body,
    };
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    return page(502, 'Zendesk did not connect', `INTERA could not reach Intera.Zendesk.Api: ${reason}`);
  }
}

function page(status: number, title: string, message: string): IWebhookResponse {
  return {
    status,
    headers: { 'content-type': 'text/html; charset=utf-8', 'cache-control': NO_STORE },
    body:
      `<!doctype html><html><head><meta charset="utf-8"><title>${escapeHtml(title)}</title></head>` +
      `<body style="font-family:sans-serif;margin:3em"><h1>${escapeHtml(title)}</h1><p>${escapeHtml(message)}</p></body></html>`,
  };
}

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}
