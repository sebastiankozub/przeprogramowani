using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MovieDate.Data;
using MovieDate.Services;

namespace MovieDate.Endpoints;

public static class MatchesEndpoints
{
    public const string RoutePrefix = "/api/matches";

    /// <summary>Problem title the SPA keys on to send the user to /profile.</summary>
    public const string ProfileIncompleteTitle = "profile_incomplete";

    public static IServiceCollection AddAppMatches(this IServiceCollection services)
    {
        services.AddScoped<MatchService>();
        return services;
    }

    public static IEndpointRouteBuilder MapMatchesEndpoints(this IEndpointRouteBuilder app)
    {
        // The current user is identified by the NameIdentifier claim (AspNetUsers.Id), never by the email
        // claim — see context/foundation/lessons.md.
        var group = app.MapGroup(RoutePrefix).RequireAuthorization();

        // The stored list as of the last recompute — a pure read (NFR-01).
        group.MapGet("/", async (ClaimsPrincipal user, UserManager<AppUser> users, MatchService service,
                CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                return userId is null
                    ? Results.Unauthorized()
                    : Results.Ok(await service.ListAsync(userId, ct));
            })
            .WithName("ListMatches");

        group.MapPost("/recompute", async (ClaimsPrincipal user, UserManager<AppUser> users, MatchService service,
                CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await service.RecomputeAsync(userId, ct);
                return result.Outcome switch
                {
                    MatchOutcome.Success => Results.Ok(result.Value),
                    MatchOutcome.ProfileIncomplete => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: ProfileIncompleteTitle,
                        detail: "Set a home city and a display name in your profile to search for matches."),
                    MatchOutcome.NotFound => Results.NotFound(),
                    _ => throw new ArgumentOutOfRangeException(nameof(result.Outcome), result.Outcome, null),
                };
            })
            .WithName("RecomputeMatches");

        return app;
    }
}
