using Intera.Jira.Api.Options;
using Microsoft.Extensions.Options;

namespace Intera.Jira.Api.Services;

/// <summary>
/// Refreshes the stored grant once it is <c>Jira:RefreshTokenKeepAliveDays</c> old, so a deployment nobody queries
/// for three months does not lose its Jira connection: a rotating refresh token unused for 90 days expires, and only a person
/// signing in to Atlassian again could then restore it.
/// </summary>
/// <remarks>
/// Copied from XeroTokenKeepAliveService (Xero: 60 days, Atlassian: 90 days of inactivity). A failure is
/// only logged: the next query surfaces the same problem as a 401 with what to do about it.
/// </remarks>
public sealed class JiraTokenKeepAliveService(
    JiraTokenService tokens,
    IOptionsMonitor<JiraOptions> options,
    ILogger<JiraTokenKeepAliveService> logger) : BackgroundService
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
                logger.LogWarning(exception, "Keeping the Jira grant alive failed; the next query will report why.");
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

        logger.LogInformation("The Jira grant was last refreshed {LastRefresh:u}; refreshing to keep it alive.", lastRefresh);
        await tokens.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
