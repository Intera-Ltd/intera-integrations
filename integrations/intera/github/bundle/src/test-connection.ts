import { parseGitHubConfig } from './config';
import { GitHubReader } from './github/reader';
import type { IIntegrationTestConnectionContext, IIntegrationTestConnectionResult } from './types';

/**
 * Connection probe for the "Test connection" button.
 *
 * The owner and the repository list are the two calls that prove what an instance depends on — the credential (an
 * app mints its installation token on the first), the owner and every name in the repository list — and both are
 * what the first dashboard reads anyway, so the probe warms the cache rather than costing extra.
 */
export async function testConnection(
  context: IIntegrationTestConnectionContext,
): Promise<IIntegrationTestConnectionResult> {
  const config = parseGitHubConfig(context.config ?? {});
  const reader = new GitHubReader(config);
  const owner = await reader.owner();
  const repositories = await reader.scopedRepositories();
  const archived = repositories.filter((repository) => repository.archived === true).length;
  const { limit, remaining } = reader.client.rateLimit;
  return {
    detail:
      `GitHub (${config.credential.kind === 'token' ? 'token' : 'app'}): ${owner.login} — ` +
      `${repositories.length} repositor${repositories.length === 1 ? 'y' : 'ies'}` +
      (archived === 0 ? '' : ` (${archived} archived)`) +
      (limit === undefined || remaining === undefined ? '' : `; rate limit ${remaining}/${limit} left this hour`),
  };
}
