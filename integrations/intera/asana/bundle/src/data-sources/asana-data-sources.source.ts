import {
  analyzeQuery,
  awaitWithSignal,
  buildEvaluationSchema,
  executeInMemoryQuery,
} from '@intera-integrations/shared';
import { AsanaReader } from '../asana/reader';
import { readAsanaConfig } from '../config';
import type { IDataSource, IDataSourceContext, IDataSourcePage, IDataSourceParams } from '../types';
import type { IAsanaSourceDefinition } from './source-definition';
import { goalsSource, portfolioItemsSource, portfoliosSource } from './sources/portfolio-sources';
import { projectsSource, sectionsSource, statusUpdatesSource } from './sources/project-sources';
import {
  taskActivitySource,
  taskCustomFieldValuesSource,
  taskDependenciesSource,
  taskMembershipsSource,
  tasksSource,
} from './sources/task-sources';
import { timeTrackingSource } from './sources/time-sources';
import { customFieldsSource, teamsSource, usersSource, workspacesSource } from './sources/workspace-sources';

/** Ordered from workspace down to tasks and companions, then portfolios and goals. */
export const ASANA_SOURCES: IAsanaSourceDefinition[] = [
  workspacesSource,
  teamsSource,
  projectsSource,
  sectionsSource,
  tasksSource,
  taskMembershipsSource,
  taskDependenciesSource,
  usersSource,
  statusUpdatesSource,
  customFieldsSource,
  taskCustomFieldValuesSource,
  taskActivitySource,
  timeTrackingSource,
  portfoliosSource,
  portfolioItemsSource,
  goalsSource,
];

function createDataSource(definition: IAsanaSourceDefinition): IDataSource {
  const evaluationSchema = buildEvaluationSchema(definition.fields);

  return {
    id: definition.id,
    name: definition.name,
    description: definition.description,
    fields: definition.fields,

    async getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown[] | IDataSourcePage> {
      const reader = new AsanaReader(readAsanaConfig(context));
      const rows = await awaitWithSignal(
        definition.load(reader, analyzeQuery(params, params.weekStart)),
        context.signal,
      );
      return executeInMemoryQuery(rows, params, evaluationSchema);
    },
  };
}

export const asanaDataSources: IDataSource[] = ASANA_SOURCES.map(createDataSource);
