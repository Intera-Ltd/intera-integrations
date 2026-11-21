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
 * Two jobs, as in intera-zoho-inventory-integration. It proves the whole chain — this plugin reaches
 * Intera.Jira.Api, the access token is right, the API's Atlassian grant works and the site choice resolves —
 * because `GET /api/Schema/custom-fields` reads the site's fields from Jira, and anything wrong throws and rejects
 * the save with the API's own reason. And it appends the site's custom fields to `jira_issues`: Core replaces a
 * source's static `fields` wholesale with what this returns, so the static list goes first.
 *
 * The result is a snapshot: a custom field added in Jira later appears after the instance is saved again. It is
 * queryable before that — the API admits any `customfield_NNNNN` name.
 */
export async function provisionInstance(
  context: IIntegrationProvisionContext,
): Promise<IIntegrationProvisionResult> {
  const client = createJiraClient(parsePluginConfig(context.config));
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
