using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MovieDate.Contracts;
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

        // Background recompute (S-06): writes call the trigger, the per-machine queue debounces, the worker runs.
        services.AddOptions<MatchRecomputeOptions>()
            .BindConfiguration(MatchRecomputeOptions.SectionName)
            .Validate(
                o => o.PollInterval > TimeSpan.Zero && o.QuietPeriod >= TimeSpan.Zero && o.MaxDelay >= TimeSpan.Zero,
                $"{MatchRecomputeOptions.SectionName}: PollInterval must be positive and delays non-negative.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<MatchRecomputeQueue>();
        services.AddScoped<MatchRecomputeTrigger>();
        services.AddHostedService<MatchRecomputeWorker>();
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

        // Is the stored list up to date? A pending user this machine has nothing queued for (restart, deploy,
        // write served by the other machine, never-computed pre-S-06 account) is queued here — self-heal.
        group.MapGet("/status", async (ClaimsPrincipal user, UserManager<AppUser> users, MatchService service,
                MatchRecomputeQueue queue, CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var status = await service.GetStatusAsync(userId, ct);
                if (status is null)
                {
                    return Results.NotFound();
                }

                if (status.Pending && !queue.Contains(userId))
                {
                    queue.Enqueue(userId);
                }

                return Results.Ok(new MatchStatusResponse(status.Pending, status.ProfileComplete));
            })
            .WithName("GetMatchStatus");

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
