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
}
