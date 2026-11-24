export * from '@intera-integrations/shared/contract-types';

export type IPluginDataSourceId = `zendesk_${string}`;

export interface IPluginConfig {
  apiBaseUrl: string;
  serviceToken: string;
}
