import { parseExcelConfig } from './config';
import { openConfiguredWorkbook, resolveSheet } from './sheets/sheet-reader';
import type {
  IIntegrationTestConnectionContext,
  IIntegrationTestConnectionResult,
} from './types';

/**
 * Verifies Entra ID sign-in, Microsoft Graph item permissions, `.xlsx` archive
 * structure, and worksheet resolution without parsing worksheet cell rows.
 */
export async function testConnection(
  context: IIntegrationTestConnectionContext,
): Promise<IIntegrationTestConnectionResult> {
  const config = parseExcelConfig(context.config);
  const { item, workbook } = await openConfiguredWorkbook(
    config,
    context.signal,
  );
  const sheet = resolveSheet(config, workbook, item.name);

  return {
    detail: `Excel: "${item.name}", worksheet "${sheet.name}"`,
  };
}
