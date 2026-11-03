using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MovieDate.Contracts;
using MovieDate.Data;
using MovieDate.Services;

namespace MovieDate.Endpoints;

public static class FriendsEndpoints
{
    public const string RoutePrefix = "/api/friends";

    /// <summary>Machine-readable problem title for an existing friendship.</summary>
    public const string AlreadyFriendTitle = "already_friend";

    public static IServiceCollection AddAppFriends(this IServiceCollection services)
    {
        services.AddScoped<FriendsService>();
        return services;
    }

    public static IEndpointRouteBuilder MapFriendsEndpoints(this IEndpointRouteBuilder app)
    {
        // The current user is identified by the NameIdentifier claim (AspNetUsers.Id), never by the email
        // claim — see context/foundation/lessons.md. Anyone off the caller's match list surfaces as 404.
        var group = app.MapGroup(RoutePrefix).RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, UserManager<AppUser> users, FriendsService service,
                CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                return userId is null
                    ? Results.Unauthorized()
                    : Results.Ok(await service.ListAsync(userId, ct));
            })
            .WithName("ListFriends");

        group.MapPost("/", async (AddFriendRequest request, ClaimsPrincipal user, UserManager<AppUser> users,
                FriendsService service, CancellationToken ct) =>
            {
                var userId = users.GetUserId(user);
                if (userId is null)
                {
                    return Results.Unauthorized();
                }

                var result = await service.AddAsync(userId, request.UserId, ct);
                return result.Outcome switch
                {
                    // 201 without a Location header: there is no GET for a single friend.
                    FriendOutcome.Success => Results.Json(result.Value, statusCode: StatusCodes.Status201Created),
                    FriendOutcome.NotFound => Results.NotFound(),
                    FriendOutcome.Duplicate => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: AlreadyFriendTitle,
                        detail: "This user is already on your friends list."),
                    _ => throw new ArgumentOutOfRangeException(nameof(result.Outcome), result.Outcome, null),
                };
            })
            .WithName("AddFriend");

        return app;
    }
}
