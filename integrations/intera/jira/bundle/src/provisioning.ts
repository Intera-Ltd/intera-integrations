import { isSignedIn } from './api/connection';
import { createJiraClient } from './api/jira-client';
import { parsePluginConfig } from './config';
import { SOURCE_ID_PREFIX } from './constants';
import { JIRA_SOURCES } from './data-sources/jira-sources.generated';
import type {
  DataSourceFieldType,
  IDataSourceFieldDescriptor,
  IIntegrationProvisionContext,
  IIntegrationProvisionResult,
} from './types';

/** The one source whose rows carry the site's custom fields (`customfield_NNNNN` columns). */
const ISSUES_API_SOURCE = 'issues';

interface ICustomFieldDescriptor {
  name: string;
  label: string;
  type: 'Text' | 'Number' | 'Date' | 'Flag';
  custom_type?: string | null;
}

const TYPES: Record<ICustomFieldDescriptor['type'], DataSourceFieldType> = {
  Text: 'string',
  Number: 'number',
  Date: 'date',
  Flag: 'boolean',
};

/**
 * Called by Core when an instance is created or updated, before the row is persisted.
 *
 * Before anyone has signed in to Jira, saving the instance must succeed so Core can generate and register the
 * `oauth-callback` webhook URL used for OAuth sign-in. In that state, `GET /api/Schema/sources` proves reachability
 * and inbound token auth without requiring an Atlassian grant; once signed in, saving again appends custom fields.
 */
export async function provisionInstance(
  context: IIntegrationProvisionContext,
): Promise<IIntegrationProvisionResult> {
  const client = createJiraClient(parsePluginConfig(context.config));

  if (!isSignedIn(await client.getOAuthStatus())) {
    await client.get('/api/Schema/sources');
    return { dataSources: [] };
  }

  const data = await client.get('/api/Schema/custom-fields');
  const customFields = Array.isArray(data) ? (data as ICustomFieldDescriptor[]) : [];

  const issues = JIRA_SOURCES.find((source) => source.apiSource === ISSUES_API_SOURCE);
  if (!issues) {
    return { dataSources: [] };
  }

  const known = new Set(issues.fields.map((field) => field.name));
  const custom = customFields
    .filter((field) => typeof field.name === 'string' && !known.has(field.name))
    .map(
      (field): IDataSourceFieldDescriptor => ({
        name: field.name,
        type: TYPES[field.type] ?? 'string',
        label: field.label,
      }),
    );

  return {
    dataSources: [{ id: `${SOURCE_ID_PREFIX}${issues.apiSource}`, fields: [...issues.fields, ...custom] }],
  };
}
