import type { DATA_SOURCE_IDS } from './constants';

export * from '@intera-integrations/shared/contract-types';

export type IBusinessCentralDataSourceId = (typeof DATA_SOURCE_IDS)[number];

export interface IBusinessCentralConfig {
  serviceUrl: string;
  serviceToken: string;
}

export interface IBusinessCentralApiResponse<T> {
  ok: boolean;
  data: T | null;
  error: string | null;
}
