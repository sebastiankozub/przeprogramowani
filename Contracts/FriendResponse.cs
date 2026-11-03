namespace MovieDate.Contracts;

/// <summary>A friend (or someone who added the caller). City-level data only — never the email (NFR-04).</summary>
public record FriendResponse(string UserId, string DisplayName, CityResponse City);
