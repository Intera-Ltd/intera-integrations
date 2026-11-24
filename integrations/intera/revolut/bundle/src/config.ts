import {
  INTERA_SERVICE_TOKEN_CONFIG_FIELD,
  INTERA_SERVICE_URL_CONFIG_FIELD,
} from './constants';
import type {
  IDataQueryContext,
  IDataSourceContext,
  IEntityProviderContext,
  IRevolutConfig,
} from './types';

export function readRevolutConfig(
  context: IDataSourceContext | IEntityProviderContext | IDataQueryContext,
): IRevolutConfig {
  return parseRevolutConfig(resolveConfigJson(context));
}

export function parseRevolutConfig(configJson: Record<string, unknown>): IRevolutConfig {
  const rawUrl = readOptionalString(configJson, INTERA_SERVICE_URL_CONFIG_FIELD);
  if (rawUrl === undefined) {
    throw new Error(
      `Service address ("${INTERA_SERVICE_URL_CONFIG_FIELD}") is missing — redeploy the integration.`,
    );
  }

  const apiBaseUrl = validateServiceUrl(rawUrl);

  const serviceToken = readOptionalString(configJson, INTERA_SERVICE_TOKEN_CONFIG_FIELD);
  if (serviceToken === undefined) {
    throw new Error(
      `Service token ("${INTERA_SERVICE_TOKEN_CONFIG_FIELD}") is missing — redeploy the integration.`,
    );
  }

  return {
    apiBaseUrl,
    serviceToken,
  };
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

  throw new Error('Integration instance config is required.');
}

function readOptionalString(configJson: Record<string, unknown>, fieldName: string): string | undefined {
  const value = configJson[fieldName];
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : undefined;
}

function validateServiceUrl(rawUrl: string): string {
  let parsed: URL;
  try {
    parsed = new URL(rawUrl);
  } catch {
    throw new Error(`Service URL "${rawUrl}" is not a valid URL`);
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    throw new Error('Service URL must use http or https');
  }
  if (parsed.username !== '' || parsed.password !== '' || parsed.hash !== '') {
    throw new Error('Service URL must not contain userinfo or fragments');
  }
  if (isBlockedMetadataHost(parsed.hostname)) {
    throw new Error('Service URL must not target link-local metadata endpoints');
  }
  return rawUrl.replace(/\/+$/, '');
}

function isBlockedMetadataHost(hostname: string): boolean {
  const host = hostname.toLowerCase();
  return (
    host === '169.254.169.254' ||
    host.startsWith('169.254.') ||
    host === '[::ffff:169.254.169.254]' ||
    host === '[::ffff:a9fe:a9fe]' ||
    host === 'metadata.google.internal'
  );
}
