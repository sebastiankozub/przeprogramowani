using Microsoft.AspNetCore.Identity;

namespace MovieDate.Data;

/// <summary>Application user: Identity's own fields plus the profile's home city.</summary>
public class AppUser : IdentityUser
{
    /// <summary>Home city; null until the user sets it.</summary>
    public int? CityId { get; set; }

    public City? City { get; set; }
}
