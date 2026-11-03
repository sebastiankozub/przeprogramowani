namespace MovieDate.Contracts;

/// <summary>
/// Whether the caller's match list is waiting for a background recompute, and whether their profile (home city +
/// display name) is complete enough to be matched at all.
/// </summary>
public record MatchStatusResponse(bool Pending, bool ProfileComplete);
