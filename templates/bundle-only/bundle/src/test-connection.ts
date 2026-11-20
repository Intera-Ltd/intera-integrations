import { HEALTH_PATH } from './constants';
import { getJson, resolveTarget } from './service-client';
import type { TIntegrationTestConnection } from './types';

/** "Test connection" in INTERA: the external API must answer 2xx. */
export const testConnection: TIntegrationTestConnection = async (context) => {
  const target = resolveTarget(context.config);
  await getJson(target, HEALTH_PATH);
  return { detail: 'API is reachable' };
};
