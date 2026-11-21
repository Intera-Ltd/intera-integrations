declare const module: { exports: unknown };
import { asanaDataSources } from './data-sources/asana-data-sources.source';
import { manifest } from './manifest';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: asanaDataSources,
  getDataSources: () => asanaDataSources,
  // Invoked by Core when an admin presses "Test connection" on an instance.
  testConnection,
};
