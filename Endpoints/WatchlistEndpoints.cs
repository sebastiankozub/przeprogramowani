using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MovieDate.Contracts;
using MovieDate.Data;
using MovieDate.Services;

namespace MovieDate.Endpoints;

public static class WatchlistEndpoints
{
    public const string RoutePrefix = "/api/watchlist";

    public static IServiceCollection AddAppWatchlist(this IServiceCollection services)
    {
        // WatchlistService depends on WatchedMoviesService; TryAdd keeps a single registration if
        // AddAppWatchedMovies is called too.
        services.TryAddScoped<WatchedMoviesService>();
        services.AddScoped<WatchlistService>();
        return services;
    }

    public static IEndpointRouteBuilder MapWatchlistEndpoints(this IEndpointRouteBuilder app)
    {
        // The current user is identified by the NameIdentifier claim (AspNetUsers.Id), never by the email
        // claim — see context/foundation/lessons.md. Foreign entries therefore surface as 404.
        var group = app.MapGroup(RoutePrefix).RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, UserManager<AppUser> users, WatchlistService service,
                CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                return userId is null
                    ? Results.Unauthorized()
                    : Results.Ok(await service.ListAsync(userId, ct));
            })
            .WithName("ListWatchlist");

        group.MapPost("/", async (AddToWatchlistRequest request, ClaimsPrincipal user, UserManager<AppUser> users,
                WatchlistService service, CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await service.AddAsync(userId, request.MovieId, ct);
                return result.Outcome switch
                {
                    // 201 without a Location header: there is no GET for a single entry (only DELETE).
                    WatchlistOutcome.Success => Results.Json(result.Value, statusCode: StatusCodes.Status201Created),
                    WatchlistOutcome.NotFound => Results.NotFound(),
                    WatchlistOutcome.Duplicate => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "This movie is already on your watchlist."),
                    _ => throw new ArgumentOutOfRangeException(nameof(result.Outcome), result.Outcome, null),
                };
            })
            .WithName("AddToWatchlist");

        group.MapDelete("/{movieId:int}", async (int movieId, ClaimsPrincipal user, UserManager<AppUser> users,
                WatchlistService service, CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await service.RemoveAsync(userId, movieId, ct);
                return result.Outcome == WatchlistOutcome.Success ? Results.NoContent() : Results.NotFound();
            })
            .WithName("RemoveFromWatchlist");

        // Moves the entry to the watched list with a rating; on any error the entry stays on the watchlist.
        group.MapPost("/{movieId:int}/watched", async (int movieId, UpdateRatingRequest request, ClaimsPrincipal user,
                UserManager<AppUser> users, WatchlistService service, CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await service.MarkWatchedAsync(userId, movieId, request.Rating, ct);
                return result.Outcome switch
                {
                    WatchedMovieOutcome.Success => Results.Ok(result.Value),
                    WatchedMovieOutcome.NotFound => Results.NotFound(),
                    WatchedMovieOutcome.InvalidRating => Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["rating"] = [$"Rating must be between {WatchedMovie.MinRating} and {WatchedMovie.MaxRating}."],
                    }),
                    WatchedMovieOutcome.Duplicate => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "This movie was rated concurrently; try again."),
                    _ => throw new ArgumentOutOfRangeException(nameof(result.Outcome), result.Outcome, null),
                };
            })
            .WithName("MarkWatchlistItemWatched");

        return app;
    }
}
