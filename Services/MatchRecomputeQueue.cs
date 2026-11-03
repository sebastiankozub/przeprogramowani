using Microsoft.Extensions.Options;

namespace MovieDate.Services;

/// <summary>Debounce settings for background match recomputes, bound from the <c>MatchRecompute</c> section.</summary>
public sealed class MatchRecomputeOptions
{
    public const string SectionName = "MatchRecompute";

    /// <summary>A user is due once no new request has arrived for this long.</summary>
    public TimeSpan QuietPeriod { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>A user is due at the latest this long after their first queued request, even during a burst.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How often the worker looks for due users.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(250);
}

/// <summary>
/// Per-machine, in-memory debounce queue of users whose match list needs a recompute (S-06). Bursts of requests
/// for one user collapse into a single entry; <see cref="TakeDue"/> hands out (and removes) the users whose
/// quiet period or max delay has elapsed. Nothing here is durable: the DB stamps on <c>AspNetUsers</c> are the
/// source of truth for "pending", and the status endpoint re-enqueues users this machine has lost.
/// </summary>
public sealed class MatchRecomputeQueue(IOptions<MatchRecomputeOptions> options, TimeProvider time)
{
    private readonly MatchRecomputeOptions _options = options.Value;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Queues the user, or extends their quiet period when already queued.</summary>
    public void Enqueue(string userId)
    {
        var now = time.GetUtcNow();
        lock (_lock)
        {
            _entries[userId] = _entries.TryGetValue(userId, out var entry)
                ? entry with { LastRequestedAt = now }
                : new Entry(now, now);
        }
    }

    public bool Contains(string userId)
    {
        lock (_lock)
        {
            return _entries.ContainsKey(userId);
        }
    }

    /// <summary>Removes and returns every user whose quiet period or max delay has elapsed.</summary>
    public IReadOnlyList<string> TakeDue()
    {
        var now = time.GetUtcNow();
        lock (_lock)
        {
            var due = _entries
                .Where(e => now - e.Value.LastRequestedAt >= _options.QuietPeriod
                            || now - e.Value.FirstRequestedAt >= _options.MaxDelay)
                .Select(e => e.Key)
                .ToList();
            foreach (var userId in due)
            {
                _entries.Remove(userId);
            }

            return due;
        }
    }

    private sealed record Entry(DateTimeOffset FirstRequestedAt, DateTimeOffset LastRequestedAt);
}
