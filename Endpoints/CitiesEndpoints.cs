using Microsoft.EntityFrameworkCore;
using MovieDate.Contracts;
using MovieDate.Data;

namespace MovieDate.Endpoints;

public static class CitiesEndpoints
{
    public const string RoutePrefix = "/api/cities";
    public const int MinQueryLength = 2;
    public const int MaxResults = 20;
    public const int MaxQueryLength = 100;

    private const string LikeEscape = "\\";

    public static IEndpointRouteBuilder MapCitiesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix).RequireAuthorization();

        // City lookup: case-insensitive "contains" on the name; prefix matches first, then larger places first,
        // so "war" puts Warszawa ahead of small towns. Coordinates/population never leave the server.
        group.MapGet("/", async (string? q, AppDbContext db, CancellationToken ct) =>
            {
                var term = q?.Trim() ?? string.Empty;
                if (term.Length < MinQueryLength)
                {
                    return Results.Ok(Array.Empty<CityResponse>());
                }

                // No real name is longer; capping bounds the ILIKE pattern size.
                if (term.Length > MaxQueryLength)
                {
                    term = term[..MaxQueryLength];
                }

                var escaped = MoviesEndpoints.EscapeLikePattern(term);
                var contains = $"%{escaped}%";
                var prefix = $"{escaped}%";

                // Unknown population sorts last (Postgres puts NULLs first in DESC order, hence ?? -1).
                var cities = await db.Cities
                    .AsNoTracking()
                    .Where(c => EF.Functions.ILike(c.Name, contains, LikeEscape))
                    .OrderByDescending(c => EF.Functions.ILike(c.Name, prefix, LikeEscape))
                    .ThenByDescending(c => c.Population ?? -1)
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.Id)
                    .Take(MaxResults)
                    .Select(c => new CityResponse(c.Id, c.Name, c.Voivodeship))
                    .ToListAsync(ct);

                return Results.Ok(cities);
            })
            .WithName("SearchCities");

        return app;
    }
}
