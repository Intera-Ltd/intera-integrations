using Intera.ZohoInventory.Api.Options;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace Intera.ZohoInventory.Api.Infrastructure;

/// <summary>
/// Keeps this process under Zoho's per-minute and concurrency limits. A Singleton, so every request
/// the service is serving at once shares one budget.
/// </summary>
/// <remarks>
/// <para>
/// This is not a cache and not a sync: it holds no data and changes no answer, it only paces the
/// calls one query already has to make. It exists because the per-minute limit is not an ordinary
/// quota. Past 100 calls a minute Zoho blocks the <b>organization</b> for a while ("For security
/// reasons your organization has been blocked"), which takes the customer's web app API and every
/// other integration down with this one — and a single unfiltered line query can issue a few hundred
/// detail calls in seconds.
/// </para>
/// <para>
/// A query waiting here is slower rather than failed, which is the right trade for a source that is
/// read rarely. The daily allowance is a different matter and is not smoothed at all: a spent daily
/// allowance comes back as Zoho's own 429.
/// </para>
/// </remarks>
public sealed class ZohoRequestThrottle : IDisposable
{
    private readonly SlidingWindowRateLimiter rateLimiter;
    private readonly SemaphoreSlim concurrency;

    public ZohoRequestThrottle(IOptions<ZohoInventoryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;

        rateLimiter = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = Math.Clamp(value.RequestsPerMinute, 1, 100),
            Window = TimeSpan.FromMinutes(1),
            // Six segments: a burst refills in ten-second steps rather than all at once at the minute
            // mark, which would otherwise let two full windows' worth through across the boundary.
            SegmentsPerWindow = 6,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = int.MaxValue,
            AutoReplenishment = true
        });

        concurrency = new SemaphoreSlim(Math.Clamp(value.MaxParallelRequests, 1, 10));
    }

    /// <summary>Runs one upstream call once both a rate permit and a concurrency slot are free.</summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);

        using var lease = await rateLimiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            throw new ZohoLimitException("The Zoho request queue of this service is full.");
        }

        await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await call(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            concurrency.Release();
        }
    }

    public void Dispose()
    {
        rateLimiter.Dispose();
        concurrency.Dispose();
    }
}
