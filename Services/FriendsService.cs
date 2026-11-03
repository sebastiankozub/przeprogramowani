using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MovieDate.Contracts;
using MovieDate.Data;
using Npgsql;

namespace MovieDate.Services;

public enum FriendOutcome
{
    Success,

    /// <summary>The user is not on the caller's match list (also covers unknown ids and the caller themself).</summary>
    NotFound,

    /// <summary>The caller has already added this user.</summary>
    Duplicate,
}

public sealed record FriendResult(FriendOutcome Outcome, FriendResponse? Value = null)
{
    public static FriendResult Ok(FriendResponse value) => new(FriendOutcome.Success, value);

    public static readonly FriendResult NotFound = new(FriendOutcome.NotFound);
    public static readonly FriendResult Duplicate = new(FriendOutcome.Duplicate);
}

/// <summary>
/// One-sided friendships. A user can only add someone from their own match list. Every operation is keyed on
/// AspNetUsers.Id, never on the email.
/// </summary>
public sealed class FriendsService(AppDbContext db)
{
    /// <summary>
    /// The public face of a user (display name and city, never the email). Only applied to users with a city and
    /// a display name — the only ones who can be on a match list.
    /// </summary>
    internal static readonly Expression<Func<AppUser, FriendResponse>> ToResponse = u =>
        new FriendResponse(u.Id, u.DisplayName!, new CityResponse(u.City!.Id, u.City.Name, u.City.Voivodeship));

    public async Task<FriendResult> AddAsync(string userId, string? friendId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(friendId))
        {
            return FriendResult.NotFound;
        }

        var friend = await MatchedCandidates(userId)
            .Where(u => u.Id == friendId)
            .Select(ToResponse)
            .FirstOrDefaultAsync(ct);
        if (friend is null)
        {
            return FriendResult.NotFound;
        }

        if (await db.Friendships.AnyAsync(f => f.UserId == userId && f.FriendId == friendId, ct))
        {
            return FriendResult.Duplicate;
        }

        db.Friendships.Add(new Friendship { UserId = userId, FriendId = friendId, CreatedAt = Clock.Now() });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent add of the same friend (e.g. a double click) won the race.
            db.ChangeTracker.Clear();
            return FriendResult.Duplicate;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // Either account was deleted between the checks above and the insert.
            db.ChangeTracker.Clear();
            return FriendResult.NotFound;
        }

        return FriendResult.Ok(friend);
    }

    /// <summary>Whom the caller added, and who added the caller without being added back; both by display name.</summary>
    public async Task<FriendsResponse> ListAsync(string userId, CancellationToken ct = default)
    {
        var mine = db.Friendships.Where(f => f.UserId == userId).Select(f => f.FriendId);
        var addedMeIds = db.Friendships
            .Where(f => f.FriendId == userId && !mine.Contains(f.UserId))
            .Select(f => f.UserId);

        var friends = await Profiles(mine).ToListAsync(ct);
        var addedMe = await Profiles(addedMeIds).ToListAsync(ct);
        return new FriendsResponse(friends, addedMe);
    }

    /// <summary>Users on the caller's match list, with the city and display name the list requires.</summary>
    private IQueryable<AppUser> MatchedCandidates(string userId) =>
        db.Users
            .AsNoTracking()
            .Where(u => u.CityId != null && u.DisplayName != null)
            .Where(u => db.Matches.Any(m => m.UserId == userId && m.CandidateId == u.Id));

    private IQueryable<FriendResponse> Profiles(IQueryable<string> ids) =>
        db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.CityId != null && u.DisplayName != null)
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Id)
            .Select(ToResponse);
}
