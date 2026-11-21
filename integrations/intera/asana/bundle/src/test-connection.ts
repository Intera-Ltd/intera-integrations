import { AsanaReader } from './asana/reader';
import { parseAsanaConfig } from './config';
import type { IIntegrationTestConnectionContext, IIntegrationTestConnectionResult } from './types';

/**
 * Connection probe for the "Test connection" button.
 *
 * The workspace list and the project list are the two calls that prove what an instance depends on — the token,
 * the workspace choice and every name in the project list — and both are what the first dashboard reads anyway, so
 * the probe warms the cache rather than costing extra.
 */
export async function testConnection(
  context: IIntegrationTestConnectionContext,
): Promise<IIntegrationTestConnectionResult> {
  const reader = new AsanaReader(parseAsanaConfig(context.config ?? {}));
  const workspace = await reader.workspace();
  const projects = await reader.scopedProjects();
  const active = projects.filter((project) => project.archived !== true).length;
  return {
    detail:
      `Asana: ${workspace.name} — ${projects.length} project${projects.length === 1 ? '' : 's'}` +
      (active === projects.length ? '' : ` (${active} active)`),
  };
}
