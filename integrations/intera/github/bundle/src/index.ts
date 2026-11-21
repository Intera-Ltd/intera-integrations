declare const module: { exports: unknown };
import { githubDataSources } from './data-sources/github-data-sources.source';
import { manifest } from './manifest';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: githubDataSources,
  getDataSources: () => githubDataSources,
  // Invoked by Core when an admin presses "Test connection" on an instance.
  testConnection,
};
