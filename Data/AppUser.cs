using Microsoft.AspNetCore.Identity;

namespace MovieDate.Data;

/// <summary>Application user: Identity's own fields plus the profile's display name and home city.</summary>
public class AppUser : IdentityUser
{
    public const int DisplayNameMaxLength = 40;

    /// <summary>Public name shown to matches; null until the user sets it. The email is never shown to others.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Home city; null until the user sets it.</summary>
    public int? CityId { get; set; }

    public City? City { get; set; }

    /// <summary>When a write last asked for this user's match list to be recomputed; null until the first one (S-06).</summary>
    public DateTimeOffset? MatchesRequestedAt { get; set; }

    /// <summary>
    /// Start time of the last successful recompute of this user's match list; only ever moves forward, null until
    /// the first one. The list is pending while <see cref="MatchesRequestedAt"/> is later than this (S-06).
    /// </summary>
    public DateTimeOffset? MatchesComputedAt { get; set; }
}
