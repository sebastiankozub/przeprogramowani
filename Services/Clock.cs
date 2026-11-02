namespace MovieDate.Services;

internal static class Clock
{
    /// <summary>
    /// The current UTC time truncated to microseconds. Postgres timestamptz stores microseconds, so the
    /// returned value equals what a later read returns.
    /// </summary>
    public static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % 10));
    }
}
