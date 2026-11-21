declare const module: { exports: unknown };
import { sanctionsDataSources } from './data-sources/sanctions-data-sources.source';
import { manifest } from './manifest';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: sanctionsDataSources,
  getDataSources: () => sanctionsDataSources,
  // Invoked by Core when an admin presses "Test connection" on an instance.
  testConnection,
};
