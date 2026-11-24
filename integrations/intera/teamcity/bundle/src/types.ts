import type { DATA_SOURCE_IDS } from './constants';

export * from '@intera-integrations/shared/contract-types';

export type ITeamCityDataSourceId = (typeof DATA_SOURCE_IDS)[number];

export interface IPluginConfig {
  serviceUrl: string;
  serviceToken: string;
}
