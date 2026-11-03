using Microsoft.EntityFrameworkCore;
using MovieDate.Contracts;
using MovieDate.Data;

namespace MovieDate.Services;

public enum ProfileOutcome
{
    Success,

    /// <summary>Unknown city (set), or the user row no longer exists (e.g. a deleted account's cookie).</summary>
    NotFound,
}

public sealed record ProfileResult(ProfileOutcome Outcome, ProfileResponse? Value = null)
{
    public static ProfileResult Ok(ProfileResponse value) => new(ProfileOutcome.Success, value);

    public static readonly ProfileResult NotFound = new(ProfileOutcome.NotFound);
}

/// <summary>
/// The user's profile (email + home city). Every operation is keyed on the given user id (AspNetUsers.Id),
/// never on the email claim.
/// </summary>
public sealed class ProfileService(AppDbContext db)
{
    public async Task<ProfileResult> GetAsync(string userId, CancellationToken ct = default)
    {
        var profile = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new ProfileResponse(
                u.Email ?? string.Empty,
                u.City == null ? null : new CityResponse(u.City.Id, u.City.Name, u.City.Voivodeship)))
            .FirstOrDefaultAsync(ct);

        return profile is null ? ProfileResult.NotFound : ProfileResult.Ok(profile);
    }

    public async Task<ProfileResult> SetCityAsync(string userId, int cityId, CancellationToken ct = default)
    {
        if (!await db.Cities.AnyAsync(c => c.Id == cityId, ct))
        {
            return ProfileResult.NotFound;
        }

        // Direct column update: leaves Identity's ConcurrencyStamp/SecurityStamp untouched, so a concurrent
        // Identity change cannot make this fail with a concurrency exception. Cities are never deleted.
        var updated = await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.CityId, (int?)cityId), ct);

        return updated == 0 ? ProfileResult.NotFound : await GetAsync(userId, ct);
    }
}
