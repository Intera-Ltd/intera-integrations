const LOOPBACK_HOSTNAMES = new Set(['localhost', '127.0.0.1', '[::1]']);

/**
 * Resolves an optional download URL override from instance configuration.
 * Empty or omitted values fall back to `fallbackUrl`; configured URLs must
 * satisfy {@link validateListUrl}.
 */
export function resolveConfigUrl(
  config: Record<string, unknown> | undefined,
  fieldName: string,
  fallbackUrl: string,
): string {
  const raw = config?.[fieldName];
  if (raw === undefined || raw === null) {
    return fallbackUrl;
  }
  const text = String(raw).trim();
  if (text.length === 0) {
    return fallbackUrl;
  }
  return validateListUrl(text, `Config field "${fieldName}"`);
}

/**
 * Validates that a sanctions download URL uses `https:` (or `http:` exclusively
 * on loopback `localhost`, `127.0.0.1`, `[::1]`) and carries neither userinfo
 * credentials nor a URL fragment.
 */
export function validateListUrl(rawUrl: string, label = 'Download URL'): string {
  const text = rawUrl.trim();
  let parsed: URL;
  try {
    parsed = new URL(text);
  } catch {
    throw new Error(`${label} is not a valid URL`);
  }
  if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') {
    throw new Error(
      `${label} must be an https URL (http(s) URL is only allowed on loopback)`,
    );
  }
  if (parsed.protocol === 'http:' && !isLoopbackHostname(parsed.hostname)) {
    throw new Error(
      `${label} must use https (plain http is only allowed for loopback hosts localhost, 127.0.0.1, [::1])`,
    );
  }
  if (
    parsed.username !== '' ||
    parsed.password !== '' ||
    hasAuthorityUserinfo(text, parsed.protocol)
  ) {
    throw new Error(`${label} must not contain userinfo credentials`);
  }
  if (parsed.hash !== '' || text.includes('#')) {
    throw new Error(`${label} must not contain a URL fragment`);
  }
  return text;
}

export function isLoopbackHostname(hostname: string): boolean {
  return LOOPBACK_HOSTNAMES.has(hostname.toLowerCase());
}

function hasAuthorityUserinfo(rawUrl: string, protocol: string): boolean {
  const rest = rawUrl.slice(protocol.length + 2);
  const boundary = rest.search(/[/?#]/);
  const authority = boundary < 0 ? rest : rest.slice(0, boundary);
  return authority.includes('@');
}
