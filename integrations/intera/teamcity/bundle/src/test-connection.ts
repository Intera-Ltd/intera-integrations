import { createTeamCityClient } from './api/teamcity-client';
import { parsePluginConfig } from './config';
import type { ITestConnectionContext, ITestConnectionResult } from './types';

export async function testConnection(context: ITestConnectionContext): Promise<ITestConnectionResult> {
  const config = parsePluginConfig(context?.config ?? {});

  const data = await createTeamCityClient(config).queryBuildTypes(
    {
      result: { expressions: [{ alias: 'build_configurations', expression: { type: 'aggregate', action: 'count' } }] },
    },
    context?.signal,
  );

  const row = Array.isArray(data) ? (data[0] as Record<string, unknown> | undefined) : undefined;
  const count = typeof row?.build_configurations === 'number' ? row.build_configurations : 0;

  if (count === 0) {
    throw new Error(
      'TeamCity answered, but no build configurations are visible. Check that the TeamCity token user has Project viewer permissions.',
    );
  }

  return { detail: `TeamCity: ${count} build configuration${count === 1 ? '' : 's'} in scope` };
}
