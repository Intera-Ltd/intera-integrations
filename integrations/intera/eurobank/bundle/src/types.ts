import type { DATA_SOURCE_IDS } from './constants';

export * from '@intera-integrations/shared/contract-types';

export type IEurobankDataSourceId = (typeof DATA_SOURCE_IDS)[number];

export interface IEurobankConfig {
  apiBaseUrl: string;
  serviceToken: string;
}

export interface IEurobankApiResponse<T> {
  ok: boolean;
  data: T | null;
  error: string | null;
}
