import type { DATA_SOURCE_IDS } from './constants';

export * from '@intera-integrations/shared/contract-types';

export type IUnleashedDataSourceId = (typeof DATA_SOURCE_IDS)[number];

export interface IUnleashedConfig {
  serviceUrl: string;
  serviceToken: string;
}

export interface IUnleashedApiResponse<T> {
  ok: boolean;
  data: T | null;
  error: string | null;
}
