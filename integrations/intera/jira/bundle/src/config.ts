import {
  INTERA_SERVICE_TOKEN_CONFIG_KEY,
  INTERA_SERVICE_URL_CONFIG_KEY,
  JQL_SCOPE_CONFIG_FIELD,
  SITE_URL_CONFIG_FIELD,
} from './constants';
import type { IDataQueryContext, IDataSourceContext, IEntityProviderContext, IPluginConfig } from './types';

function readOptionalString(configJson: Record<string, unknown>, fieldName: string): string | undefined {
  const value = configJson[fieldName];
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : undefined;
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

function validateServiceUrl(rawUrl: string): string {
  let parsed: URL;
  try {
    parsed = new URL(rawUrl);
  } catch {
    throw new Error(`Service URL "${rawUrl}" is not a valid URL.`);
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    throw new Error('Service URL must use http or https.');
  }
  if (parsed.username !== '' || parsed.password !== '') {
    throw new Error('Service URL must not embed credentials.');
  }
  if (isBlockedMetadataHost(parsed.hostname)) {
    throw new Error('Service URL must not target link-local metadata endpoints.');
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

  throw new Error('Integration instance config is required.');
}

export function parsePluginConfig(configJson: Record<string, unknown>): IPluginConfig {
  const rawUrl = readOptionalString(configJson, INTERA_SERVICE_URL_CONFIG_KEY);
  if (rawUrl === undefined) {
    throw new Error(
      `The service address is missing (${INTERA_SERVICE_URL_CONFIG_KEY}); install or redeploy the integration from Marketplace.`,
    );
  }

  const apiBaseUrl = validateServiceUrl(rawUrl);
  const serviceToken = readOptionalString(configJson, INTERA_SERVICE_TOKEN_CONFIG_KEY);
  if (serviceToken === undefined) {
    throw new Error(
      `The service token is missing (${INTERA_SERVICE_TOKEN_CONFIG_KEY}); redeploy the integration.`,
    );
  }

  return {
    apiBaseUrl,
    serviceToken,
    siteUrl: readOptionalString(configJson, SITE_URL_CONFIG_FIELD) ?? readOptionalString(configJson, 'site_url'),
    jqlScope: readOptionalString(configJson, JQL_SCOPE_CONFIG_FIELD) ?? readOptionalString(configJson, 'jql_scope'),
  };
}

export function readPluginConfig(
  context: IDataSourceContext | IEntityProviderContext | IDataQueryContext,
): IPluginConfig {
  return parsePluginConfig(resolveConfigJson(context));
}
