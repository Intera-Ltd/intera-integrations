import {
  ACCESS_TOKEN_CONFIG_FIELD,
  API_BASE_URL_CONFIG_FIELD,
  INTERA_SERVICE_URL_CONFIG_FIELD,
} from './constants';
import type {
  IDataQueryContext,
  IDataSourceContext,
  IEntityProviderContext,
  IZohoInventoryConfig,
} from './types';

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
    throw new Error(`Service URL must use http or https`);
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

export function parseZohoInventoryConfig(configJson: Record<string, unknown>): IZohoInventoryConfig {
  const rawUrl =
    readOptionalString(configJson, API_BASE_URL_CONFIG_FIELD) ??
    readOptionalString(configJson, INTERA_SERVICE_URL_CONFIG_FIELD);

  if (rawUrl === undefined) {
    throw new Error(
      `Service address is required ("${INTERA_SERVICE_URL_CONFIG_FIELD}" or "${API_BASE_URL_CONFIG_FIELD}")`,
    );
  }

  return {
    apiBaseUrl: validateServiceUrl(rawUrl),
    accessToken: readOptionalString(configJson, ACCESS_TOKEN_CONFIG_FIELD),
  };
}

export function readZohoInventoryConfig(
  context: IDataSourceContext | IEntityProviderContext | IDataQueryContext,
): IZohoInventoryConfig {
  return parseZohoInventoryConfig(resolveConfigJson(context));
}
