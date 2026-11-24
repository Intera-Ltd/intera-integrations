import {
  INTERA_SERVICE_TOKEN_CONFIG_FIELD,
  INTERA_SERVICE_URL_CONFIG_FIELD,
} from './constants';
import type {
  IBusinessCentralConfig,
  IDataQueryContext,
  IDataSourceContext,
  IEntityProviderContext,
} from './types';

function readOptionalString(configJson: Record<string, unknown>, fieldName: string): string | undefined {
  const value = configJson[fieldName];
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : undefined;
}

function isLinkLocalHost(hostname: string): boolean {
  const normalized = hostname.toLowerCase().replace(/^\[|\]$/g, '');
  return (
    normalized === '169.254.169.254' ||
    normalized.startsWith('169.254.') ||
    normalized === '::ffff:169.254.169.254' ||
    normalized === '::ffff:a9fe:a9fe' ||
    normalized === 'metadata.google.internal' ||
    normalized.startsWith('fe80:')
  );
}

function validateServiceUrl(rawUrl: string): string {
  let parsed: URL;
  try {
    parsed = new URL(rawUrl);
  } catch {
    throw new Error(`Service URL ("${INTERA_SERVICE_URL_CONFIG_FIELD}") is not a valid URL.`);
  }

  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    throw new Error(`Service URL ("${INTERA_SERVICE_URL_CONFIG_FIELD}") must use http or https.`);
  }

  if (parsed.username.length > 0 || parsed.password.length > 0) {
    throw new Error(`Service URL ("${INTERA_SERVICE_URL_CONFIG_FIELD}") must not contain credentials.`);
  }

  if (isLinkLocalHost(parsed.hostname)) {
    throw new Error(`Service URL ("${INTERA_SERVICE_URL_CONFIG_FIELD}") must not target link-local addresses.`);
  }

  return rawUrl.replace(/\/+$/, '');
}

function resolveConfigJson(
  context: IDataSourceContext | IEntityProviderContext | IDataQueryContext,
): Record<string, unknown> {
  if ('config' in context && context.config !== undefined) {
    return context.config;
  }

  if ('configJson' in context && context.configJson !== undefined) {
    return context.configJson;
  }

  throw new Error('Integration instance config is required; redeploy the integration.');
}

export function parseBusinessCentralConfig(configJson: Record<string, unknown>): IBusinessCentralConfig {
  const rawUrl = readOptionalString(configJson, INTERA_SERVICE_URL_CONFIG_FIELD);
  if (rawUrl === undefined) {
    throw new Error(
      `Service address ("${INTERA_SERVICE_URL_CONFIG_FIELD}") is missing; redeploy the integration from Marketplace.`,
    );
  }

  const serviceUrl = validateServiceUrl(rawUrl);
  const serviceToken = readOptionalString(configJson, INTERA_SERVICE_TOKEN_CONFIG_FIELD);
  if (serviceToken === undefined) {
    throw new Error(
      `Service token ("${INTERA_SERVICE_TOKEN_CONFIG_FIELD}") is missing; redeploy the integration so Core provisions INTERA_SERVICE_TOKEN.`,
    );
  }

  return {
    serviceUrl,
    serviceToken,
  };
}

export function readBusinessCentralConfig(
  context: IDataSourceContext | IEntityProviderContext | IDataQueryContext,
): IBusinessCentralConfig {
  return parseBusinessCentralConfig(resolveConfigJson(context));
}
