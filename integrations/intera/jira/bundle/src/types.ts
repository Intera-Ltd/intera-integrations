export * from '@intera-integrations/shared/contract-types';

export type IPluginDataSourceId = `jira_${string}`;

export interface IPluginConfig {
  apiBaseUrl: string;
  serviceToken: string;
  siteUrl?: string;
  jqlScope?: string;
}
