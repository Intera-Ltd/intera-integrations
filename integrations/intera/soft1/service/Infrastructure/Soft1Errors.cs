namespace Intera.Soft1.Api.Infrastructure;

/// <summary>
/// Soft1 refused this deployment's web account: a wrong password, a missing Web Service Connector licence, a
/// module the account may not use. Surfaces as 401 rather than the global filter's 502 — Soft1 answered, and
/// the thing to fix is this deployment's configuration.
/// </summary>
public sealed class Soft1AuthenticationException(string message) : Exception(message);

/// <summary>
/// Soft1 refused the request itself (a business error, an unknown object or browser, a filter it cannot read).
/// Surfaces as 400 carrying Soft1's own text.
/// </summary>
public sealed class Soft1RequestException(string message) : Exception(message);

/// <summary>
/// The query would read more than this deployment allows (<c>Soft1:MaxRowsPerRead</c> /
/// <c>Soft1:MaxRecordReadsPerQuery</c>). Surfaces as 400 naming what would narrow it; the rows are not
/// truncated, because a dashboard cannot tell a truncated sum from a correct one.
/// </summary>
public sealed class Soft1QueryTooBroadException(string message) : Exception(message);

/// <summary>A <c>success: false</c> answer, before it is decided what it means.</summary>
public sealed class Soft1ServiceException(int? code, string message)
    : Exception(code is null ? message : $"Soft1 error {code}: {message}")
{
    public int? Code { get; } = code;

    public string Detail { get; } = message;

    /// <summary>
    /// -101 is the documented "session expired"; -1 ("invalid request - not logged in") is what a clientID the
    /// server no longer knows gets, e.g. after the installation restarted. Both are cured by signing in again.
    /// </summary>
    public bool IsSessionLost => Code is -101 or -1;
}

/// <summary>Soft1's documented error codes, turned into the exception that carries the right status.</summary>
public static class Soft1Errors
{
    public static Exception Translate(Soft1ServiceException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var detail = exception.Detail;

        return exception.Code switch
        {
            -2 => new Soft1AuthenticationException($"Soft1 refused the sign-in (Soft1:Username / Soft1:Password): {detail}"),
            -3 => new Soft1AuthenticationException($"The Soft1 module is not activated for this web account (Soft1:Module): {detail}"),
            -4 or -5 => new Soft1AuthenticationException($"Soft1 refused the sign-in for licensing reasons (devices or licences exceeded): {detail}"),
            -6 => new Soft1AuthenticationException($"Soft1 rejected the application id (Soft1:AppId): {detail}"),
            -10 => new Soft1AuthenticationException($"Soft1 refused the user name (Soft1:Username): {detail}"),
            -11 => new Soft1AuthenticationException($"The Soft1 installation has no Web Service Connector licence: {detail}"),
            -103 => new Soft1AuthenticationException($"Soft1 rejected the request by company policy: {detail}"),
            -101 or -1 => new Soft1AuthenticationException($"Soft1 dropped the session and signing in again did not help: {detail}"),
            2001 => new Soft1RequestException($"The Soft1 record does not exist: {detail}"),
            _ => new Soft1RequestException($"Soft1 refused the request: {detail}")
        };
    }
}
