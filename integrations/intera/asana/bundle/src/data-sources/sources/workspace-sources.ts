import { REFERENCE_CACHE_TTL_MS } from '../../constants';
import { mapLimited } from '../../asana/map-limited';
import { AsanaApiError, type AsanaObject } from '../../asana/asana-client';
import type { AsanaRecord } from '../../types';
import {
  LOOKUP,
  bool,
  flag,
  joined,
  number,
  num,
  str,
  text,
  type IAsanaSourceDefinition,
} from '../source-definition';

/** Up to this many gids a filter names are read one by one instead of listing the workspace. */
const MAX_BY_ID = 100;

export const workspacesSource: IAsanaSourceDefinition = {
  id: 'asana_workspaces',
  name: 'Workspaces',
  description:
    'Every workspace and organization the token belongs to (`GET /workspaces`) — not only the one the instance ' +
    'reads. `email_domains` is comma-joined.',
  fields: [
    text('workspace_gid', 'Workspace ID', LOOKUP.workspace),
    text('name', 'Workspace'),
    flag('is_organization', 'Organization'),
    text('email_domains', 'Email domains'),
  ],
  async load(reader) {
    const workspaces = await reader.workspaces();
    return workspaces.map((workspace) => ({
      workspace_gid: str(workspace.gid),
      name: str(workspace.name),
      is_organization: bool(workspace.is_organization),
      email_domains: joined(workspace.email_domains, str),
    }));
  },
};

export const teamsSource: IAsanaSourceDefinition = {
  id: 'asana_teams',
  name: 'Teams',
  description:
    'Teams of the workspace (`GET /workspaces/{gid}/teams`) visible to the token\'s user. Only an organization has ' +
    'teams; a plain workspace answers no rows.',
  fields: [
    text('team_gid', 'Team ID', LOOKUP.team),
    text('workspace_gid', 'Workspace ID', LOOKUP.workspace),
    text('name', 'Team'),
    text('description', 'Description'),
    text('visibility', 'Visibility'),
    text('permalink_url', 'Link'),
  ],
  async load(reader) {
    const workspace = await reader.workspace();
    if (!workspace.isOrganization) {
      return [];
    }
    const teams = await reader.client.list(
      `/workspaces/${workspace.gid}/teams`,
      { opt_fields: 'name,description,visibility,permalink_url' },
      REFERENCE_CACHE_TTL_MS,
    );
    return teams.map((team) => ({
      team_gid: str(team.gid),
      workspace_gid: workspace.gid,
      name: str(team.name),
      description: str(team.description),
      visibility: str(team.visibility),
      permalink_url: str(team.permalink_url),
    }));
  },
};

const USER_OPT_FIELDS = 'name,email,photo.image_128x128';

function userRow(user: AsanaObject, workspaceGid: string): AsanaRecord {
  return {
    user_gid: str(user.gid),
    name: str(user.name),
    email: str(user.email),
    photo_url: str((user.photo as AsanaObject | null | undefined)?.image_128x128),
    workspace_gid: workspaceGid,
  };
}

export const usersSource: IAsanaSourceDefinition = {
  id: 'asana_users',
  name: 'Users',
  description:
    'People of the workspace (`GET /users?workspace=`), the lookup behind every `*_gid` that names a person. ' +
    '`email` is empty when the token\'s user may not see it.',
  fields: [
    text('user_gid', 'User ID', LOOKUP.user),
    text('name', 'Name'),
    text('email', 'Email'),
    text('photo_url', 'Photo'),
    text('workspace_gid', 'Workspace ID', LOOKUP.workspace),
  ],
  async load(reader, shape) {
    const workspace = await reader.workspace();
    const named = shape.values('user_gid');
    if (named !== undefined && named.length <= MAX_BY_ID) {
      // A label lookup: a handful of people, rather than a directory that may hold thousands.
      const users = await mapLimited(named, async (gid) => {
        try {
          return await reader.client.get(`/users/${encodeURIComponent(gid)}`, { opt_fields: USER_OPT_FIELDS }, REFERENCE_CACHE_TTL_MS);
        } catch (error) {
          if (error instanceof AsanaApiError && (error.status === 404 || error.status === 403)) {
            return null;
          }
          throw error;
        }
      });
      return users.filter((user): user is AsanaObject => user !== null).map((user) => userRow(user, workspace.gid));
    }
    const users = await reader.client.list(
      '/users',
      { workspace: workspace.gid, opt_fields: USER_OPT_FIELDS },
      REFERENCE_CACHE_TTL_MS,
    );
    return users.map((user) => userRow(user, workspace.gid));
  },
};

export const customFieldsSource: IAsanaSourceDefinition = {
  id: 'asana_custom_fields',
  name: 'Custom fields',
  description:
    'Custom field definitions of the workspace (`GET /workspaces/{gid}/custom_fields`; a paid Asana plan). ' +
    '`enum_options` lists the enabled options, comma-joined. Values per task are `asana_task_custom_field_values`.',
  fields: [
    text('custom_field_gid', 'Custom field ID', LOOKUP.customField),
    text('name', 'Custom field'),
    text('resource_subtype', 'Type'),
    text('representation_type', 'Representation'),
    text('description', 'Description'),
    flag('is_global_to_workspace', 'Global to workspace'),
    flag('is_formula_field', 'Formula field'),
    text('enum_options', 'Options'),
    text('currency_code', 'Currency'),
    text('format', 'Format'),
    number('precision', 'Precision'),
  ],
  async load(reader) {
    const workspace = await reader.workspace();
    const fields = await reader.client.list(
      `/workspaces/${workspace.gid}/custom_fields`,
      {
        opt_fields:
          'name,resource_subtype,representation_type,description,is_global_to_workspace,is_formula_field,' +
          'enum_options.name,enum_options.enabled,currency_code,format,precision',
      },
      REFERENCE_CACHE_TTL_MS,
    );
    return fields.map((field) => ({
      custom_field_gid: str(field.gid),
      name: str(field.name),
      resource_subtype: str(field.resource_subtype),
      representation_type: str(field.representation_type),
      description: str(field.description),
      is_global_to_workspace: bool(field.is_global_to_workspace),
      is_formula_field: bool(field.is_formula_field),
      enum_options: joined(
        Array.isArray(field.enum_options)
          ? (field.enum_options as AsanaObject[]).filter((option) => option.enabled !== false)
          : null,
      ),
      currency_code: str(field.currency_code),
      format: str(field.format),
      precision: num(field.precision),
    }));
  },
};
