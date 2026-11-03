using Microsoft.EntityFrameworkCore;
using MovieDate.Data;

namespace MovieDate.Services;

/// <summary>
/// Called by every write that can change a user's matches, after it succeeded (S-06): stamps
/// <see cref="AppUser.MatchesRequestedAt"/> so "pending" is visible on every machine, then queues the debounced
/// background recompute on this one. Keyed on AspNetUsers.Id, never on the email.
/// </summary>
public sealed class MatchRecomputeTrigger(AppDbContext db, MatchRecomputeQueue queue)
{
    public async Task RequestAsync(string userId, CancellationToken ct = default)
    {
        // Plain column update; no ConcurrencyStamp rotation needed for a bookkeeping stamp. Deliberately not
        // cancellable: the write already succeeded, so a client aborting now must not skip the stamp — a missing
        // stamp is the one case the status self-heal cannot detect.
        _ = ct;
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.MatchesRequestedAt, Clock.Now()), CancellationToken.None);

        queue.Enqueue(userId);
    }
}
