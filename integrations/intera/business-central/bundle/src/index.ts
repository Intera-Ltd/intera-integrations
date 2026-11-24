declare const module: { exports: unknown };
import { businessCentralDataSources } from './data-sources/business-central-data-sources.source';
import { manifest } from './manifest';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: businessCentralDataSources,
  getDataSources: () => businessCentralDataSources,
  testConnection,
};
