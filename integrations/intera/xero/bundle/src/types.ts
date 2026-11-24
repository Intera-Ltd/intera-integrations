export * from '@intera-integrations/shared/contract-types';

export type IPluginDataSourceId = `xero_${string}`;

export interface IPluginConfig {
  apiBaseUrl: string;
  serviceToken: string;
  tenantId?: string;
}
