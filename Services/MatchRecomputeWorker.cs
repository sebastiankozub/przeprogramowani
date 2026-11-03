using Microsoft.Extensions.Options;

namespace MovieDate.Services;

/// <summary>
/// Runs the due recomputes from <see cref="MatchRecomputeQueue"/> off the request path, one at a time per machine
/// (S-06). A failing job is logged and skipped — the user stays pending in the DB and is re-enqueued by the next
/// status check — so one bad recompute never stops the worker. Not started by the <c>--migrate</c> release run,
/// which exits before the host starts.
/// </summary>
public sealed class MatchRecomputeWorker(
    MatchRecomputeQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<MatchRecomputeOptions> options,
    ILogger<MatchRecomputeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var userId in queue.TakeDue())
                {
                    await RecomputeAsync(userId, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown; users taken but not yet recomputed stay pending in the DB.
        }
    }

    private async Task RecomputeAsync(string userId, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<MatchService>();
            var result = await service.RecomputeAsync(userId, stoppingToken);
            logger.LogDebug("Background match recompute for user {UserId}: {Outcome}.", userId, result.Outcome);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background match recompute failed for user {UserId}.", userId);
        }
    }
}
