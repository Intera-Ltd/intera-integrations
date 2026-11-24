import { SHEET_ROWS_SOURCE_ID, TAB_SOURCE_ID_PREFIX } from '../constants';

/**
 * Datasource id of a secondary tab: `gsheet-tab-<gid>`.
 *
 * Keyed by Google's numeric `sheetId` (`gid`) rather than the tab's title so
 * renaming a tab preserves its datasource id and any saved metrics or widgets.
 */
export function tabSourceId(sheetId: number): string {
  return `${TAB_SOURCE_ID_PREFIX}${sheetId}`;
}

/**
 * Resolves which tab a `getData` call targets: `null` for the primary tab
 * (`gsheet-rows`), or the numeric `sheetId` for an added `gsheet-tab-<gid>`
 * datasource.
 */
export function sheetIdOfSource(
  dataSourceId: string | undefined,
): number | null {
  if (dataSourceId === undefined || dataSourceId === SHEET_ROWS_SOURCE_ID) {
    return null;
  }
  const gid = dataSourceId.startsWith(TAB_SOURCE_ID_PREFIX)
    ? dataSourceId.slice(TAB_SOURCE_ID_PREFIX.length)
    : '';
  if (!/^\d+$/.test(gid)) {
    throw new Error(
      `Datasource "${dataSourceId}" is not a tab of this integration`,
    );
  }
  return Number(gid);
}
