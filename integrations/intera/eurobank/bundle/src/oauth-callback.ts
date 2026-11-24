import { createEurobankClient } from './api/eurobank-client';
import { parseEurobankConfig } from './config';
import { OAUTH_CALLBACK_HANDLER_KEY, REGISTRATION_TIMEOUT_MS } from './constants';
import type {
  IWebhookHandlerContext,
  IWebhookRegistrationContext,
  IWebhookRegistrationResult,
  IWebhookRequest,
  IWebhookResponse,
} from './types';

const CALLBACK_PARAMETERS = ['code', 'state', 'error', 'error_description'] as const;
const NO_STORE = 'no-store';

export async function registerWebhook(context: IWebhookRegistrationContext): Promise<IWebhookRegistrationResult> {
  if (context.handlerKey !== OAUTH_CALLBACK_HANDLER_KEY) {
    return { ok: false, message: `Unknown webhook handler "${context.handlerKey}"` };
  }

  try {
    const client = createEurobankClient(parseEurobankConfig(context.config));
    await client.put('/oauth/redirect-uri', { redirect_uri: context.webhookUrl }, REGISTRATION_TIMEOUT_MS);

    return { ok: true, providerRef: context.webhookUrl };
  } catch (error) {
    return { ok: false, message: error instanceof Error ? error.message : String(error) };
  }
}

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
    return page(
      400,
      'Eurobank did not connect',
      "This address only receives Eurobank's sign-in redirect, and this request carried none.",
    );
  }

  try {
    const answer = await createEurobankClient(parseEurobankConfig(context.config)).relayOAuthCallback(forwarded);

    return {
      status: answer.status,
      headers: { 'content-type': answer.contentType, 'cache-control': NO_STORE },
      body: answer.body,
    };
  } catch {
    return page(
      502,
      'Eurobank did not connect',
      'INTERA could not reach Intera.Eurobank.Api while relaying the sign-in callback.',
    );
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
