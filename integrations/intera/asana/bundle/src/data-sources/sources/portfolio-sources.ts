import { AsanaApiError, type AsanaObject } from '../../asana/asana-client';
import { mapLimited } from '../../asana/map-limited';
import type { AsanaReader } from '../../asana/reader';
import {
  LOOKUP,
  bool,
  date,
  flag,
  gidOf,
  nameOf,
  num,
  str,
  text,
  number,
  type IAsanaSourceDefinition,
} from '../source-definition';

const PORTFOLIO_OPT_FIELDS =
  'name,owner.name,archived,created_at,start_on,due_on,permalink_url,' +
  'current_status_update.status_type,current_status_update.title';

const GOAL_OPT_FIELDS =
  'name,owner.name,notes,start_on,due_on,status,is_workspace_level,team.name,time_period.display_name,' +
  'metric.current_number_value,metric.target_number_value,metric.unit';

/**
 * The workspace's portfolios. Asana lists them without `owner` only to a service account; any other token is
 * answered 400 "owner: Value missing" (seen 2026-10-09) and may list only its own user's, so that is the retry.
 */
export async function readPortfolios(reader: AsanaReader): Promise<AsanaObject[]> {
  const workspace = await reader.workspace();
  const query = { workspace: workspace.gid, opt_fields: PORTFOLIO_OPT_FIELDS };
  try {
    return await reader.client.list('/portfolios', query);
  } catch (error) {
    if (!(error instanceof AsanaApiError && error.status === 400 && error.message.includes('owner'))) throw error;
    return reader.client.list('/portfolios', { ...query, owner: 'me' });
  }
}

export async function readGoals(reader: AsanaReader): Promise<AsanaObject[]> {
  const workspace = await reader.workspace();
  return reader.client.list('/goals', { workspace: workspace.gid, opt_fields: GOAL_OPT_FIELDS });
}

export const portfoliosSource: IAsanaSourceDefinition = {
  id: 'asana_portfolios',
  name: 'Portfolios',
  description:
    'Portfolios of the workspace (`GET /portfolios`; a paid Asana plan). A personal access token sees only the ' +
    'portfolios its user owns; a service account token sees every one. `current_status` is the type of the latest ' +
    'status update.',
  fields: [
    text('portfolio_gid', 'Portfolio ID', LOOKUP.portfolio),
    text('name', 'Portfolio'),
    text('owner_gid', 'Owner ID', LOOKUP.user),
    text('owner_name', 'Owner'),
    flag('archived', 'Archived'),
    text('current_status', 'Current status'),
    text('current_status_title', 'Status title'),
    date('start_on', 'Start date'),
    date('due_on', 'Due date'),
    date('created_at', 'Created'),
    text('permalink_url', 'Link'),
  ],
  async load(reader) {
    const portfolios = await readPortfolios(reader);
    return portfolios.map((portfolio) => {
      const status = portfolio.current_status_update as AsanaObject | null | undefined;
      return {
        portfolio_gid: str(portfolio.gid),
        name: str(portfolio.name),
        owner_gid: gidOf(portfolio.owner),
        owner_name: nameOf(portfolio.owner),
        archived: bool(portfolio.archived),
        current_status: str(status?.status_type),
        current_status_title: str(status?.title),
        start_on: str(portfolio.start_on),
        due_on: str(portfolio.due_on),
        created_at: str(portfolio.created_at),
        permalink_url: str(portfolio.permalink_url),
      };
    });
  },
};

export const portfolioItemsSource: IAsanaSourceDefinition = {
  id: 'asana_portfolio_items',
  name: 'Portfolio items',
  description:
    'What each portfolio holds (`GET /portfolios/{gid}/items`), one call per portfolio. An item is usually a ' +
    'project; a portfolio nested in another is an item too, with `item_type` = `portfolio` and its gid in ' +
    '`project_gid`. `project_status` is the type of the item\'s latest status update.',
  fields: [
    text('portfolio_gid', 'Portfolio ID', LOOKUP.portfolio),
    text('item_type', 'Item type'),
    text('project_gid', 'Project ID', LOOKUP.project),
    text('project_name', 'Project'),
    text('project_owner_gid', 'Project owner ID', LOOKUP.user),
    text('project_owner_name', 'Project owner'),
    text('project_status', 'Project status'),
    date('project_due_on', 'Project due date'),
  ],
  async load(reader, shape) {
    const named = shape.values('portfolio_gid');
    const portfolios = (await readPortfolios(reader)).filter(
      (portfolio) => named === undefined || named.includes(String(portfolio.gid)),
    );
    const lists = await mapLimited(portfolios, async (portfolio) => ({
      portfolio,
      items: await reader.client.list(`/portfolios/${portfolio.gid}/items`, {
        opt_fields: 'name,resource_type,owner.name,due_on,current_status_update.status_type',
      }),
    }));
    return lists.flatMap(({ portfolio, items }) =>
      items.map((item) => ({
        portfolio_gid: str(portfolio.gid),
        item_type: str(item.resource_type),
        project_gid: str(item.gid),
        project_name: str(item.name),
        project_owner_gid: gidOf(item.owner),
        project_owner_name: nameOf(item.owner),
        project_status: str((item.current_status_update as AsanaObject | null | undefined)?.status_type),
        project_due_on: str(item.due_on),
      })),
    );
  },
};

export const goalsSource: IAsanaSourceDefinition = {
  id: 'asana_goals',
  name: 'Goals',
  description:
    'Goals of the workspace (`GET /goals?workspace=`; a paid Asana plan). `status` is `green`, `yellow` or `red` ' +
    'while a goal is open and `achieved`, `partial`, `missed` or `dropped` once closed; the `metric_*` columns are ' +
    'its progress metric, where it has one.',
  fields: [
    text('goal_gid', 'Goal ID'),
    text('name', 'Goal'),
    text('owner_gid', 'Owner ID', LOOKUP.user),
    text('owner_name', 'Owner'),
    text('notes', 'Description'),
    date('start_on', 'Start date'),
    date('due_on', 'Due date'),
    text('status', 'Status'),
    flag('is_workspace_level', 'Workspace-level'),
    text('team_gid', 'Team ID', LOOKUP.team),
    text('team_name', 'Team'),
    text('time_period', 'Time period'),
    number('metric_current_value', 'Metric: current'),
    number('metric_target_value', 'Metric: target'),
    text('metric_unit', 'Metric unit'),
  ],
  async load(reader) {
    const goals = await readGoals(reader);
    return goals.map((goal) => {
      const metric = goal.metric as AsanaObject | null | undefined;
      return {
        goal_gid: str(goal.gid),
        name: str(goal.name),
        owner_gid: gidOf(goal.owner),
        owner_name: nameOf(goal.owner),
        notes: str(goal.notes),
        start_on: str(goal.start_on),
        due_on: str(goal.due_on),
        status: str(goal.status),
        is_workspace_level: bool(goal.is_workspace_level),
        team_gid: gidOf(goal.team),
        team_name: nameOf(goal.team),
        time_period: str((goal.time_period as AsanaObject | null | undefined)?.display_name),
        metric_current_value: num(metric?.current_number_value),
        metric_target_value: num(metric?.target_number_value),
        metric_unit: str(metric?.unit),
      };
    });
  },
};
