import type { DATA_SOURCE_IDS } from './constants';

export * from '@intera-integrations/shared/contract-types';

export type ISalesforceDataSourceId = (typeof DATA_SOURCE_IDS)[number];

export interface ISalesforceConfig {
  apiBaseUrl: string;
  serviceToken: string;
}

export interface ISalesforceApiResponse<T> {
  ok: boolean;
  data: T | null;
  error: string | null;
}
