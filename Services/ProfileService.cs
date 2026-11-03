using Microsoft.EntityFrameworkCore;
using MovieDate.Contracts;
using MovieDate.Data;
using Npgsql;

namespace MovieDate.Services;

public enum ProfileOutcome
{
    Success,

    /// <summary>Unknown city (set), or the user row no longer exists (e.g. a deleted account's cookie).</summary>
    NotFound,

    /// <summary>The display name is empty/whitespace or longer than <see cref="AppUser.DisplayNameMaxLength"/> after trimming.</summary>
    InvalidDisplayName,
}

public sealed record ProfileResult(ProfileOutcome Outcome, ProfileResponse? Value = null)
{
    public static ProfileResult Ok(ProfileResponse value) => new(ProfileOutcome.Success, value);

    public static readonly ProfileResult NotFound = new(ProfileOutcome.NotFound);

    public static readonly ProfileResult InvalidDisplayName = new(ProfileOutcome.InvalidDisplayName);
}

/// <summary>
/// The user's profile (email + display name + home city). Every operation is keyed on the given user id (AspNetUsers.Id),
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
                u.DisplayName,
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

        // Direct column update that also rotates Identity's ConcurrencyStamp (SecurityStamp stays untouched,
        // so the user's sessions remain valid). Both directions of a race with Identity's UserStore.UpdateAsync
        // (e.g. /api/auth/manage/info, failed-login lockout counting), which writes every column:
        // - Identity writes first: this update has no stamp check, so it simply wins on CityId.
        // - This update lands between Identity's load and write: the rotated stamp makes Identity's optimistic
        //   concurrency check fail with ConcurrencyFailure instead of silently reverting the city.
        int updated;
        try
        {
            updated = await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.CityId, (int?)cityId)
                    .SetProperty(u => u.ConcurrencyStamp, Guid.NewGuid().ToString()), ct);
        }
        catch (Exception ex) when (IsForeignKeyViolation(ex))
        {
            // The city was deleted between the existence check above and the update.
            return ProfileResult.NotFound;
        }

        return updated == 0 ? ProfileResult.NotFound : await GetAsync(userId, ct);
    }

    public async Task<ProfileResult> SetDisplayNameAsync(string userId, string displayName,
        CancellationToken ct = default)
    {
        // The body binder leaves a missing "displayName" as null despite the non-nullable contract.
        var trimmed = displayName?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > AppUser.DisplayNameMaxLength)
        {
            return ProfileResult.InvalidDisplayName;
        }

        // Same direct update + ConcurrencyStamp rotation as SetCityAsync (see the comment there).
        var updated = await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.DisplayName, trimmed)
                .SetProperty(u => u.ConcurrencyStamp, Guid.NewGuid().ToString()), ct);

        return updated == 0 ? ProfileResult.NotFound : await GetAsync(userId, ct);
    }

    // ExecuteUpdateAsync bypasses SaveChanges, so the provider exception may arrive unwrapped.
    private static bool IsForeignKeyViolation(Exception ex) =>
        (ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.ForeignKeyViolation;
}
