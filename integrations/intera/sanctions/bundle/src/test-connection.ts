import { SANCTIONS_LISTS, loadList } from './data-sources/sanctions-data-sources.source';
import type {
  IIntegrationTestConnectionContext,
  IIntegrationTestConnectionResult,
} from './types';

/**
 * Connection probe for the "Test connection" button.
 *
 * Loads every list through the same cache the data sources read, so a test
 * right after a restart costs one download per list and warms the cache for
 * the dashboards, and a test within the day costs nothing. What it proves is
 * the thing that actually breaks here: that Core can reach the publisher and
 * that the file still parses. The detail line carries the publisher's own
 * generation date, which is the one number worth checking by eye.
 */
export async function testConnection(
  context: IIntegrationTestConnectionContext,
): Promise<IIntegrationTestConnectionResult> {
  const parts: string[] = [];
  for (const list of SANCTIONS_LISTS) {
    const loaded = await loadList(list, context.config);
    if (loaded.rows.length === 0) {
      throw new Error(`${list.name}: the file parsed but lists nobody`);
    }
    parts.push(
      `${list.name}: ${loaded.entityCount} subjects, ${loaded.rows.length} names, ` +
        `generated ${loaded.generatedAt ?? 'at an unstated time'}`,
    );
  }
  return { detail: parts.join('; ') };
}
