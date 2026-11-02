using Microsoft.EntityFrameworkCore;
using MovieDate.Contracts;
using MovieDate.Data;

namespace MovieDate.Endpoints;

public static class MoviesEndpoints
{
    public const string RoutePrefix = "/api/movies";
    public const int MinQueryLength = 2;
    public const int MaxResults = 20;
    public const int MaxQueryLength = 100;

    private const string LikeEscape = "\\";

    public static IEndpointRouteBuilder MapMoviesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix).RequireAuthorization();

        // Catalog search: case-insensitive "contains" on the Polish or original title, prefix matches first.
        group.MapGet("/", async (string? q, AppDbContext db, CancellationToken ct) =>
            {
                var term = q?.Trim() ?? string.Empty;
                if (term.Length < MinQueryLength)
                {
                    return Results.Ok(Array.Empty<MovieResponse>());
                }

                // No real title is longer; capping bounds the ILIKE pattern size.
                if (term.Length > MaxQueryLength)
                {
                    term = term[..MaxQueryLength];
                }

                var escaped = EscapeLikePattern(term);
                var contains = $"%{escaped}%";
                var prefix = $"{escaped}%";

                var movies = await db.Movies
                    .AsNoTracking()
                    .Where(m => EF.Functions.ILike(m.Title, contains, LikeEscape)
                                || (m.OriginalTitle != null && EF.Functions.ILike(m.OriginalTitle, contains, LikeEscape)))
                    .OrderByDescending(m => EF.Functions.ILike(m.Title, prefix, LikeEscape)
                                            || (m.OriginalTitle != null && EF.Functions.ILike(m.OriginalTitle, prefix, LikeEscape)))
                    .ThenBy(m => m.Title)
                    .ThenBy(m => m.Id)
                    .Take(MaxResults)
                    .Select(m => new MovieResponse(m.Id, m.Title, m.OriginalTitle, m.Year))
                    .ToListAsync(ct);

                return Results.Ok(movies);
            })
            .WithName("SearchMovies");

        return app;
    }

    /// <summary>Escapes LIKE wildcards so user input is matched literally (escape character: backslash).</summary>
    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
