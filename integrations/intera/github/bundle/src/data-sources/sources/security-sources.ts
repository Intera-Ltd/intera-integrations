import type { GitHubApiError } from '../../github/github-client';
import { repoPath } from '../../github/reader';
import {
  bool,
  date,
  featureUnavailable,
  flag,
  loginOf,
  number,
  num,
  obj,
  repoField,
  str,
  text,
  type IGitHubSourceDefinition,
} from '../source-definition';

/**
 * A security feature that is off, not set up, or not part of the plan for a repository answers in words ("… are
 * disabled for this repository", "no analysis found", "Advanced Security must be enabled …"). Such a repository has
 * no alerts to read and gives no rows; a missing permission still fails the read (`featureUnavailable`).
 */
const securityUnavailable = (error: GitHubApiError) =>
  featureUnavailable(
    error,
    /disabled/i,
    /not enabled/i,
    /must be enabled/i,
    /no analysis found/i,
    /archived/i,
    /not available/i,
    /repository is empty/i,
  );

/** States a filter names, as the comma-separated `state` GitHub's alert listings take; undefined when any. */
function stateParam(values: string[] | undefined, known: string[]): string | undefined {
  if (values === undefined || values.length === 0 || !values.every((value) => known.includes(value))) {
    return undefined;
  }
  return values.join(',');
}

export const dependabotAlertsSource: IGitHubSourceDefinition = {
  id: 'github_dependabot_alerts',
  name: 'Dependabot alerts',
  description:
    'Dependabot alerts of each repository in scope, in every state (`open`, `fixed`, `dismissed`, ' +
    '`auto_dismissed`) unless the filter names some. `vulnerable_version` is the advisory\'s vulnerable range ' +
    '(`< 4.17.21`), not the installed version, which GitHub does not report; `fixed_version` is the first patched ' +
    'one. A repository with Dependabot alerts switched off gives no rows. Needs Dependabot alerts: read.',
  fields: [
    repoField(),
    number('alert_number', 'Alert'),
    text('state', 'State'),
    text('severity', 'Severity'),
    text('package', 'Package'),
    text('ecosystem', 'Ecosystem'),
    text('manifest_path', 'Manifest'),
    text('scope', 'Scope'),
    text('vulnerable_version', 'Vulnerable versions'),
    text('fixed_version', 'Fixed in'),
    text('ghsa_id', 'GHSA'),
    text('cve_id', 'CVE'),
    text('summary', 'Summary'),
    number('cvss_score', 'CVSS score'),
    date('created_at', 'Created'),
    date('updated_at', 'Updated'),
    date('fixed_at', 'Fixed'),
    date('dismissed_at', 'Dismissed'),
    text('dismissed_reason', 'Dismissed reason'),
    text('dismissed_by', 'Dismissed by'),
    date('auto_dismissed_at', 'Auto-dismissed'),
    text('html_url', 'Link'),
  ],
  load(reader, shape) {
    const state = stateParam(shape.values('state'), ['auto_dismissed', 'dismissed', 'fixed', 'open']);
    return reader.perRepository(
      shape,
      async (repository) => {
        const alerts = await reader.client.list(`${repoPath(repository)}/dependabot/alerts`, { state });
        return alerts.map((alert) => {
          const dependency = obj(alert.dependency);
          const advisory = obj(alert.security_advisory);
          const vulnerability = obj(alert.security_vulnerability);
          return {
            repo: str(repository.name),
            alert_number: num(alert.number),
            state: str(alert.state),
            severity: str(vulnerability?.severity) ?? str(advisory?.severity),
            package: str(obj(dependency?.package)?.name) ?? str(obj(vulnerability?.package)?.name),
            ecosystem: str(obj(dependency?.package)?.ecosystem) ?? str(obj(vulnerability?.package)?.ecosystem),
            manifest_path: str(dependency?.manifest_path),
            scope: str(dependency?.scope),
            vulnerable_version: str(vulnerability?.vulnerable_version_range),
            fixed_version: str(obj(vulnerability?.first_patched_version)?.identifier),
            ghsa_id: str(advisory?.ghsa_id),
            cve_id: str(advisory?.cve_id),
            summary: str(advisory?.summary),
            cvss_score: num(obj(advisory?.cvss)?.score),
            created_at: str(alert.created_at),
            updated_at: str(alert.updated_at),
            fixed_at: str(alert.fixed_at),
            dismissed_at: str(alert.dismissed_at),
            dismissed_reason: str(alert.dismissed_reason),
            dismissed_by: loginOf(alert.dismissed_by),
            auto_dismissed_at: str(alert.auto_dismissed_at),
            html_url: str(alert.html_url),
          };
        });
      },
      securityUnavailable,
    );
  },
};

export const codeScanningAlertsSource: IGitHubSourceDefinition = {
  id: 'github_code_scanning_alerts',
  name: 'Code scanning alerts',
  description:
    'Code scanning alerts of each repository in scope, in every state (`open`, `dismissed`, `fixed`) unless the ' +
    "filter names one. `severity` is the rule's security severity (`critical`, `high`, `medium`, `low`) where the " +
    "tool gives one, else the rule's own (`error`, `warning`, `note`), which is `rule_severity`. A repository " +
    'without code scanning set up, or a private one without GitHub Advanced Security, gives no rows. Needs Code ' +
    'scanning alerts: read.',
  fields: [
    repoField(),
    number('alert_number', 'Alert'),
    text('rule', 'Rule'),
    text('rule_name', 'Rule name'),
    text('rule_description', 'Rule description'),
    text('severity', 'Severity'),
    text('rule_severity', 'Rule severity'),
    text('tool', 'Tool'),
    text('state', 'State'),
    text('ref', 'Ref'),
    text('path', 'File'),
    date('created_at', 'Created'),
    date('updated_at', 'Updated'),
    date('fixed_at', 'Fixed'),
    date('dismissed_at', 'Dismissed'),
    text('dismissed_reason', 'Dismissed reason'),
    text('dismissed_by', 'Dismissed by'),
    text('html_url', 'Link'),
  ],
  load(reader, shape) {
    const states = shape.values('state');
    const state = states !== undefined && states.length === 1 ? stateParam(states, ['open', 'closed', 'dismissed', 'fixed']) : undefined;
    return reader.perRepository(
      shape,
      async (repository) => {
        const alerts = await reader.client.list(`${repoPath(repository)}/code-scanning/alerts`, { state });
        return alerts.map((alert) => {
          const rule = obj(alert.rule);
          const instance = obj(alert.most_recent_instance);
          return {
            repo: str(repository.name),
            alert_number: num(alert.number),
            rule: str(rule?.id),
            rule_name: str(rule?.name),
            rule_description: str(rule?.description),
            severity: str(rule?.security_severity_level) ?? str(rule?.severity),
            rule_severity: str(rule?.severity),
            tool: str(obj(alert.tool)?.name),
            state: str(alert.state),
            ref: str(instance?.ref),
            path: str(obj(instance?.location)?.path),
            created_at: str(alert.created_at),
            updated_at: str(alert.updated_at),
            fixed_at: str(alert.fixed_at),
            dismissed_at: str(alert.dismissed_at),
            dismissed_reason: str(alert.dismissed_reason),
            dismissed_by: loginOf(alert.dismissed_by),
            html_url: str(alert.html_url),
          };
        });
      },
      securityUnavailable,
    );
  },
};

export const secretScanningAlertsSource: IGitHubSourceDefinition = {
  id: 'github_secret_scanning_alerts',
  name: 'Secret scanning alerts',
  description:
    'Secret scanning alerts of each repository in scope, `open` and `resolved` (`resolution` says how). The ' +
    'secret itself is never read (`hide_secret`). `validity` is `active`, `inactive` or `unknown` where GitHub ' +
    'checks it with the provider. A repository with secret scanning off gives no rows. Needs Secret scanning ' +
    'alerts: read.',
  fields: [
    repoField(),
    number('alert_number', 'Alert'),
    text('secret_type', 'Secret type'),
    text('secret_type_display_name', 'Secret type name'),
    text('state', 'State'),
    text('resolution', 'Resolution'),
    text('validity', 'Validity'),
    flag('push_protection_bypassed', 'Push protection bypassed'),
    flag('publicly_leaked', 'Publicly leaked'),
    date('created_at', 'Created'),
    date('updated_at', 'Updated'),
    date('resolved_at', 'Resolved'),
    text('resolved_by', 'Resolved by'),
    text('html_url', 'Link'),
  ],
  load(reader, shape) {
    const state = stateParam(shape.values('state'), ['open', 'resolved']);
    return reader.perRepository(
      shape,
      async (repository) => {
        const alerts = await reader.client.list(`${repoPath(repository)}/secret-scanning/alerts`, {
          state: state?.includes(',') ? undefined : state,
          hide_secret: true,
        });
        return alerts.map((alert) => ({
          repo: str(repository.name),
          alert_number: num(alert.number),
          secret_type: str(alert.secret_type),
          secret_type_display_name: str(alert.secret_type_display_name),
          state: str(alert.state),
          resolution: str(alert.resolution),
          validity: str(alert.validity),
          push_protection_bypassed: bool(alert.push_protection_bypassed),
          publicly_leaked: bool(alert.publicly_leaked),
          created_at: str(alert.created_at),
          updated_at: str(alert.updated_at),
          resolved_at: str(alert.resolved_at),
          resolved_by: loginOf(alert.resolved_by),
          html_url: str(alert.html_url),
        }));
      },
      securityUnavailable,
    );
  },
};
