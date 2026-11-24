import { parseGsheetConfig } from './config';
import { getSpreadsheetProperties } from './google/sheets-client';
import { resolveTab } from './sheets/sheet-reader';
import type {
  IIntegrationTestConnectionContext,
  IIntegrationTestConnectionResult,
} from './types';

/**
 * Verifies the service-account key, OAuth token exchange, Sheets API access,
 * spreadsheet sharing permissions, and target tab resolution using lightweight
 * spreadsheet metadata.
 */
export async function testConnection(
  context: IIntegrationTestConnectionContext,
): Promise<IIntegrationTestConnectionResult> {
  const config = parseGsheetConfig(context.config);
  const properties = await getSpreadsheetProperties(config, context.signal);
  const tab = resolveTab(config, properties.sheets);

  return {
    detail: `Google Sheets: "${properties.title}", tab "${tab.title}"`,
  };
}
