import { HEALTH_PATH } from './constants';
import { getJson, resolveServiceUrl } from './service-client';
import type { TIntegrationTestConnection } from './types';

/** "Test connection" in INTERA: the service must answer 2xx on its health path. */
export const testConnection: TIntegrationTestConnection = async (context) => {
  const baseUrl = resolveServiceUrl(context.config);
  await getJson(baseUrl, HEALTH_PATH);
  return { detail: 'Service is healthy' };
};
