import type { DATA_SOURCE_IDS } from './constants';

export * from '@intera-integrations/shared/contract-types';

export type IRevolutDataSourceId = (typeof DATA_SOURCE_IDS)[number];

export interface IRevolutConfig {
  apiBaseUrl: string;
  serviceToken: string;
}

export interface IRevolutApiResponse<T> {
  ok: boolean;
  data: T | null;
  error: string | null;
}
