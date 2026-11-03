using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MovieDate.Contracts;
using MovieDate.Data;
using MovieDate.Services;

namespace MovieDate.Endpoints;

public static class WatchedMoviesEndpoints
{
    public const string RoutePrefix = "/api/watched";

    public static IServiceCollection AddAppWatchedMovies(this IServiceCollection services)
    {
        services.AddScoped<WatchedMoviesService>();
        return services;
    }

    public static IEndpointRouteBuilder MapWatchedMoviesEndpoints(this IEndpointRouteBuilder app)
    {
        // The current user is identified by the NameIdentifier claim (AspNetUsers.Id), never by the email
        // claim — see context/foundation/lessons.md. Foreign entries therefore surface as 404.
        // Successful writes request a background match recompute (S-06); error outcomes request nothing.
        var group = app.MapGroup(RoutePrefix).RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, UserManager<AppUser> users, WatchedMoviesService service,
                CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                return userId is null
                    ? Results.Unauthorized()
                    : Results.Ok(await service.ListAsync(userId, ct));
            })
            .WithName("ListWatchedMovies");

        group.MapPost("/", async (AddWatchedMovieRequest request, ClaimsPrincipal user, UserManager<AppUser> users,
                WatchedMoviesService service, MatchRecomputeTrigger trigger, CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await service.AddAsync(userId, request.MovieId, request.Rating, ct);
                if (result.Outcome != WatchedMovieOutcome.Success)
                {
                    return ToErrorResult(result.Outcome);
                }

                await trigger.RequestAsync(userId, ct);
                // 201 without a Location header: there is no GET for a single entry (only PUT/DELETE).
                return Results.Json(result.Value, statusCode: StatusCodes.Status201Created);
            })
            .WithName("AddWatchedMovie");

        group.MapPut("/{movieId:int}", async (int movieId, UpdateRatingRequest request, ClaimsPrincipal user,
                UserManager<AppUser> users, WatchedMoviesService service, MatchRecomputeTrigger trigger,
                CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await service.UpdateRatingAsync(userId, movieId, request.Rating, ct);
                if (result.Outcome != WatchedMovieOutcome.Success)
                {
                    return ToErrorResult(result.Outcome);
                }

                await trigger.RequestAsync(userId, ct);
                return Results.Ok(result.Value);
            })
            .WithName("UpdateWatchedMovieRating");

        group.MapDelete("/{movieId:int}", async (int movieId, ClaimsPrincipal user, UserManager<AppUser> users,
                WatchedMoviesService service, MatchRecomputeTrigger trigger, CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await service.RemoveAsync(userId, movieId, ct);
                if (result.Outcome != WatchedMovieOutcome.Success)
                {
                    return ToErrorResult(result.Outcome);
                }

                await trigger.RequestAsync(userId, ct);
                return Results.NoContent();
            })
            .WithName("RemoveWatchedMovie");

        return app;
    }

    private static IResult ToErrorResult(WatchedMovieOutcome outcome) => outcome switch
    {
        WatchedMovieOutcome.InvalidRating => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["rating"] = [$"Rating must be between {WatchedMovie.MinRating} and {WatchedMovie.MaxRating}."],
        }),
        WatchedMovieOutcome.NotFound => Results.NotFound(),
        WatchedMovieOutcome.Duplicate => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "This movie is already on your watched list."),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not an error outcome."),
    };
}
