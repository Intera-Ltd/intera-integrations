import { createTeamCityClient, type TeamCityClient } from '../api/teamcity-client';
import { readPluginConfig } from '../config';
import {
  BUILD_TYPES_SOURCE_ID,
  BUILD_TYPES_SOURCE_NAME,
  TEST_RESULTS_SOURCE_ID,
  TEST_RESULTS_SOURCE_NAME,
} from '../constants';
import type {
  IDataQuery,
  IDataSource,
  IDataSourceContext,
  IDataSourceFieldDescriptor,
  IDataSourceParams,
} from '../types';
import { normalizeDataQuery } from '@intera-integrations/shared';

function text(name: string, label: string): IDataSourceFieldDescriptor {
  return { name, type: 'string', label };
}

function num(name: string, label: string): IDataSourceFieldDescriptor {
  return { name, type: 'number', label };
}

function date(name: string, label: string): IDataSourceFieldDescriptor {
  return { name, type: 'date', label };
}

function bool(name: string, label: string): IDataSourceFieldDescriptor {
  return { name, type: 'boolean', label };
}

const BUILD_STATUSES = ['SUCCESS', 'FAILURE', 'ERROR', 'UNKNOWN'];

function buildTypeColumns(withLookup: boolean): IDataSourceFieldDescriptor[] {
  return [
    text('project_id', 'Project ID'),
    text('project_name', 'Project (full path)'),
    withLookup
      ? {
          name: 'build_type_id',
          type: 'string',
          label: 'Build configuration ID',
          lookup: { datasource: BUILD_TYPES_SOURCE_ID, valueField: 'build_type_id', labelField: 'build_type_name' },
        }
      : text('build_type_id', 'Build configuration ID'),
    text('build_type_name', 'Build configuration'),
    text('build_type_web_url', 'Build configuration URL'),
    bool('paused', 'Paused'),
  ];
}

export const BUILD_TYPE_FIELDS: IDataSourceFieldDescriptor[] = [
  ...buildTypeColumns(false),
  num('last_build_id', 'Last build ID'),
  text('last_build_number', 'Last build number'),
  { name: 'last_build_status', type: 'enum', label: 'Last build status', enumValues: BUILD_STATUSES },
  bool('last_build_success', 'Last build successful'),
  text('last_build_status_text', 'Last build status text'),
  text('last_build_branch', 'Last build branch'),
  date('last_build_start_date', 'Last build started'),
  date('last_build_finish_date', 'Last build finished'),
  text('last_build_web_url', 'Last build URL'),
  num('last_build_tests_total', 'Last build: tests'),
  num('last_build_tests_passed', 'Last build: tests passed'),
  num('last_build_tests_failed', 'Last build: tests failed'),
  num('last_build_tests_ignored', 'Last build: tests ignored'),
  num('last_build_tests_muted', 'Last build: tests muted'),
  num('last_build_tests_new_failed', 'Last build: new failures'),
  num('running_builds', 'Running builds'),
  num('queued_builds', 'Queued builds'),
];

export const TEST_RESULT_FIELDS: IDataSourceFieldDescriptor[] = [
  ...buildTypeColumns(true),
  num('build_id', 'Build ID'),
  text('build_number', 'Build number'),
  { name: 'build_status', type: 'enum', label: 'Build status', enumValues: BUILD_STATUSES },
  text('build_status_text', 'Build status text'),
  text('build_branch', 'Build branch'),
  date('build_start_date', 'Build started'),
  date('build_finish_date', 'Build finished'),
  text('build_web_url', 'Build URL'),
  bool('from_latest_build', 'From the latest build'),
  text('test_id', 'Test ID'),
  text('test_name', 'Test'),
  text('test_package', 'Test package'),
  text('test_suite', 'Test suite'),
  text('test_class', 'Test class'),
  text('test_method', 'Test method'),
  { name: 'status', type: 'enum', label: 'Test status', enumValues: ['SUCCESS', 'FAILURE', 'UNKNOWN'] },
  bool('passed', 'Passed'),
  bool('ignored', 'Ignored'),
  bool('muted', 'Muted'),
  bool('currently_muted', 'Currently muted'),
  bool('currently_investigated', 'Under investigation'),
  bool('new_failure', 'New failure'),
  num('duration_ms', 'Duration, ms'),
  text('details', 'Failure details'),
  text('ignore_details', 'Ignore reason'),
];

type QueryExecutor = (client: TeamCityClient, query: IDataQuery, signal?: AbortSignal) => Promise<unknown>;

function createQueryDataSource(
  id: string,
  name: string,
  description: string,
  fields: IDataSourceFieldDescriptor[],
  execute: QueryExecutor,
): IDataSource {
  return {
    id,
    name,
    description,
    fields,
    async getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown> {
      const config = readPluginConfig(context);
      const query = normalizeDataQuery(params);
      const client = createTeamCityClient(config);
      return execute(client, query, context?.signal);
    },
  };
}

export const buildTypesDataSource: IDataSource = createQueryDataSource(
  BUILD_TYPES_SOURCE_ID,
  BUILD_TYPES_SOURCE_NAME,
  'One row per build configuration with the state of its **latest finished build** on the default branch: ' +
    'status, status text, test counters, dates — plus running and queued builds.',
  BUILD_TYPE_FIELDS,
  (client, query, signal) => client.queryBuildTypes(query, signal),
);

export const testResultsDataSource: IDataSource = createQueryDataSource(
  TEST_RESULTS_SOURCE_ID,
  TEST_RESULTS_SOURCE_NAME,
  'One row per test in the **latest finished build that ran tests** of each configuration.\n' +
    'Filter by `project_name` / `build_type_name` first — each configuration read costs a TeamCity call. ' +
    'A test that did not run in that build is absent.',
  TEST_RESULT_FIELDS,
  (client, query, signal) => client.queryTestResults(query, signal),
);

export const teamCityDataSources: IDataSource[] = [buildTypesDataSource, testResultsDataSource];
