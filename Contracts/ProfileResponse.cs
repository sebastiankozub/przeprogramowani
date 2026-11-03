namespace MovieDate.Contracts;

public record ProfileResponse(string Email, string? DisplayName, CityResponse? City);
