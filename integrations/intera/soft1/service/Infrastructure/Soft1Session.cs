using Intera.Soft1.Api.Options;
using Microsoft.Extensions.Options;

namespace Intera.Soft1.Api.Infrastructure;

/// <summary>An open Soft1 session and the company/branch it was authenticated into.</summary>
public sealed record Soft1SessionInfo(string ClientId, string? Company, string? Branch);

/// <summary>
/// The process-wide holder of the one Soft1 session (<c>clientID</c>) this deployment uses.
/// </summary>
/// <remarks>
/// <para>
/// A Singleton holding the active session token while the client that opens the session is Scoped, so
/// the typed <c>HttpClient</c> never pins one handler for the life of the process.
/// </para>
/// <para>
/// There is no expiry to track. Soft1 states none in its answer — the lifetime is a setting of the web account —
/// and says to sign in again only when a request fails with a negative code, so the session is kept until
/// Soft1 reports it lost (<see cref="Soft1ServiceException.IsSessionLost"/>). In memory only: the password is in
/// configuration, so a lost session costs one sign-in.
/// </para>
/// </remarks>
public sealed class Soft1SessionCache
{
    private readonly SemaphoreSlim gate = new(1, 1);

    private Soft1SessionInfo? session;

    public async Task<Soft1SessionInfo> GetOrSignInAsync(
        Func<CancellationToken, Task<Soft1SessionInfo>> signIn,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signIn);

        if (Volatile.Read(ref session) is { } cached)
        {
            return cached;
        }

        // A dashboard opening several widgets on a cold cache signs in once, not once per widget.
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref session) is { } current)
            {
                return current;
            }

            var opened = await signIn(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref session, opened);

            return opened;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Forgets <paramref name="lost"/> — only if it is still the current session, so that several requests that
    /// failed on the same expired session do not each throw away the one a sibling has just opened.
    /// </summary>
    public void Invalidate(Soft1SessionInfo lost)
    {
        Interlocked.CompareExchange(ref session, null, lost);
    }
}

/// <summary>
/// Caps the requests in flight to Soft1 across every query this process serves. A Singleton.
/// </summary>
/// <remarks>
/// Soft1 documents no rate limit, but it is the customer's ERP, answering its own users from the same
/// application server, and a line query fans out into one <c>getData</c> per document. A query waiting here is
/// slower rather than failed.
/// </remarks>
public sealed class Soft1RequestGate(IOptions<Soft1Options> options) : IDisposable
{
    private readonly SemaphoreSlim slots = new(Math.Clamp(options.Value.MaxParallelRequests, 1, 16));

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);

        await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await call(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            slots.Release();
        }
    }

    public void Dispose() => slots.Dispose();
}
