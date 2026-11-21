using System.Collections.Concurrent;

namespace Intera.Xero.Api.Infrastructure;

/// <summary>
/// Reuses Xero's answers for a short while, and lets concurrent requests for the same read share one call.
/// </summary>
/// <remarks>
/// <para>
/// Xero allows 60 calls a minute and 5000 a day per organisation, and one dashboard asks for the same source several
/// times at once — the rows, a page, its total — while a document's headers and its lines are one and the same read.
/// Keys carry the organisation and the full request, so nothing crosses tenants or filters.
/// </para>
/// <para>
/// The shared call is not cancelled by the first caller's token: others may be waiting on it. Each caller stops
/// waiting on its own cancellation, and the call itself is bounded by the HTTP timeout. A failed call is dropped at
/// once, so a refusal is not served from cache.
/// </para>
/// </remarks>
public sealed class XeroResponseCache
{
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private long lastPurgeTicks = DateTimeOffset.UtcNow.UtcTicks;

    public async Task<T> GetOrAddAsync<T>(
        string key,
        TimeSpan lifetime,
        Func<CancellationToken, Task<T>> load,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(load);

        if (lifetime <= TimeSpan.Zero)
        {
            return await load(cancellationToken).ConfigureAwait(false);
        }

        PurgeExpired();

        var now = DateTimeOffset.UtcNow;

        // Lazy: AddOrUpdate may run its factories more than once under contention, and only the entry that won may
        // start a call to Xero.
        var candidate = new Entry(new Lazy<Task<object>>(() => StartLoad(load)), now + lifetime);
        var entry = entries.AddOrUpdate(
            key,
            candidate,
            (_, existing) => existing.ExpiresAt > now && !existing.Value.Value.IsFaulted && !existing.Value.Value.IsCanceled
                ? existing
                : candidate);

        var task = entry.Value.Value;
        try
        {
            return (T)await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (task.IsFaulted || task.IsCanceled)
        {
            entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            throw;
        }
    }

    /// <summary>Drops everything — after a new grant, whose organisations may differ.</summary>
    public void Clear() => entries.Clear();

    private static Task<object> StartLoad<T>(Func<CancellationToken, Task<T>> load)
        where T : class =>
        Task.Run(async () => (object)await load(CancellationToken.None).ConfigureAwait(false));

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var last = Interlocked.Read(ref lastPurgeTicks);
        if (now.UtcTicks - last < PurgeInterval.Ticks
            || Interlocked.CompareExchange(ref lastPurgeTicks, now.UtcTicks, last) != last)
        {
            return;
        }

        foreach (var pair in entries)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                entries.TryRemove(pair);
            }
        }
    }

    private sealed record Entry(Lazy<Task<object>> Value, DateTimeOffset ExpiresAt);
}
