namespace MovieDate.Contracts;

/// <summary>A city as exposed by the API — deliberately without coordinates or population.</summary>
public record CityResponse(int Id, string Name, string Voivodeship);
