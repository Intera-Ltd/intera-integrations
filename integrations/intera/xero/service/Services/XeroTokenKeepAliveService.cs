using Intera.Xero.Api.Options;
using Microsoft.Extensions.Options;

namespace Intera.Xero.Api.Services;

/// <summary>
/// Refreshes the stored grant once it is <c>Xero:RefreshTokenKeepAliveDays</c> old, so a deployment nobody queries
/// for two months does not lose its Xero connection: a refresh token unused for 60 days expires, and only a person
/// signing in to Xero again could then restore it.
/// </summary>
/// <remarks>
/// A failure during background keep-alive refresh is
/// only logged: the next query surfaces the same problem as a 401 with what to do about it.
/// </remarks>
public sealed class XeroTokenKeepAliveService(
    XeroTokenService tokens,
    IOptionsMonitor<XeroOptions> options,
    ILogger<XeroTokenKeepAliveService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        do
        {
            try
            {
                await RefreshIfStaleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Keeping the Xero grant alive failed; the next query will report why.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task RefreshIfStaleAsync(CancellationToken cancellationToken)
    {
        var days = options.CurrentValue.RefreshTokenKeepAliveDays;
        if (days <= 0)
        {
            return;
        }

        var lastRefresh = await tokens.GetLastRefreshAsync(cancellationToken).ConfigureAwait(false);
        if (lastRefresh is null || lastRefresh > DateTimeOffset.UtcNow.AddDays(-days))
        {
            return;
        }

        logger.LogInformation("The Xero grant was last refreshed {LastRefresh:u}; refreshing to keep it alive.", lastRefresh);
        await tokens.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
