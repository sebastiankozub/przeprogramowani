namespace MovieDate.Data;

/// <summary>
/// One entry on a user's append-only match list: a candidate and their compatibility score as of the last recompute.
/// One row per (user, candidate); both sides are AspNetUsers.Id, never the email.
/// </summary>
public class Match
{
    public required string UserId { get; set; }

    public required string CandidateId { get; set; }

    public double Score { get; set; }

    public int SharedFavorites { get; set; }

    public int SharedWatchlist { get; set; }

    public DateTimeOffset FirstMatchedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
