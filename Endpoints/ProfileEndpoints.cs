using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MovieDate.Contracts;
using MovieDate.Data;
using MovieDate.Services;

namespace MovieDate.Endpoints;

public static class ProfileEndpoints
{
    public const string RoutePrefix = "/api/profile";

    public static IServiceCollection AddAppProfile(this IServiceCollection services)
    {
        services.AddScoped<ProfileService>();
        return services;
    }

    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        // The current user is identified by the NameIdentifier claim (AspNetUsers.Id), never by the email
        // claim — see context/foundation/lessons.md.
        var group = app.MapGroup(RoutePrefix).RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, UserManager<AppUser> users, ProfileService service,
                CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                return userId is null
                    ? Results.Unauthorized()
                    : ToResult(await service.GetAsync(userId, ct));
            })
            .WithName("GetProfile");

        // Both setters change who the user can be matched with: a success requests a background match
        // recompute (S-06); error outcomes request nothing.
        group.MapPut("/city", async (SetCityRequest request, ClaimsPrincipal user, UserManager<AppUser> users,
                ProfileService service, MatchRecomputeTrigger trigger, CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                return userId is null
                    ? Results.Unauthorized()
                    : await ToTriggeringResult(await service.SetCityAsync(userId, request.CityId, ct), userId,
                        trigger, ct);
            })
            .WithName("SetProfileCity");

        group.MapPut("/display-name", async (SetDisplayNameRequest request, ClaimsPrincipal user,
                UserManager<AppUser> users, ProfileService service, MatchRecomputeTrigger trigger,
                CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                return userId is null
                    ? Results.Unauthorized()
                    : await ToTriggeringResult(await service.SetDisplayNameAsync(userId, request.DisplayName, ct),
                        userId, trigger, ct);
            })
            .WithName("SetProfileDisplayName");

        return app;
    }

    private static async Task<IResult> ToTriggeringResult(ProfileResult result, string userId,
        MatchRecomputeTrigger trigger, CancellationToken ct)
    {
        if (result.Outcome == ProfileOutcome.Success)
        {
            await trigger.RequestAsync(userId, ct);
        }

        return ToResult(result);
    }

    private static IResult ToResult(ProfileResult result) => result.Outcome switch
    {
        ProfileOutcome.Success => Results.Ok(result.Value),
        ProfileOutcome.NotFound => Results.NotFound(),
        ProfileOutcome.InvalidDisplayName => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["displayName"] = [$"Display name must be 1–{AppUser.DisplayNameMaxLength} characters (not just spaces)."],
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Unknown outcome."),
    };
}
