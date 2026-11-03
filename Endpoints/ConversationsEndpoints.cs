using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MovieDate.Contracts;
using MovieDate.Data;
using MovieDate.Services;

namespace MovieDate.Endpoints;

public static class ConversationsEndpoints
{
    public const string RoutePrefix = "/api/conversations";

    public static IServiceCollection AddAppConversations(this IServiceCollection services)
    {
        services.AddScoped<ConversationService>();
        return services;
    }

    public static IEndpointRouteBuilder MapConversationsEndpoints(this IEndpointRouteBuilder app)
    {
        // The current user is identified by the NameIdentifier claim (AspNetUsers.Id), never by the email
        // claim — see context/foundation/lessons.md. A pair that fails the gate (matched + friends in either
        // direction) surfaces as 404, checked before any body validation.
        var group = app.MapGroup(RoutePrefix).RequireAuthorization();

        group.MapGet("/{userId}", async (string userId, ClaimsPrincipal user, UserManager<AppUser> users,
                ConversationService service, CancellationToken ct) =>
            {
                var me = users.GetUserId(user);
                if (me is null)
                {
                    return Results.Unauthorized();
                }

                var conversation = await service.GetAsync(me, userId, ct);
                return conversation is null ? Results.NotFound() : Results.Ok(conversation);
            })
            .WithName("GetConversation");

        group.MapPost("/{userId}/messages", async (string userId, SendMessageRequest request, ClaimsPrincipal user,
                UserManager<AppUser> users, ConversationService service, CancellationToken ct) =>
            {
                var me = users.GetUserId(user);
                if (me is null)
                {
                    return Results.Unauthorized();
                }

                var result = await service.SendAsync(me, userId, request.Body, ct);
                return result.Outcome switch
                {
                    // 201 without a Location header: there is no GET for a single message.
                    ConversationOutcome.Success => Results.Json(result.Value, statusCode: StatusCodes.Status201Created),
                    ConversationOutcome.NotFound => Results.NotFound(),
                    ConversationOutcome.InvalidBody => Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["body"] = [$"Message must be between 1 and {Message.BodyMaxLength} characters."],
                    }),
                    _ => throw new ArgumentOutOfRangeException(nameof(result.Outcome), result.Outcome, null),
                };
            })
            .WithName("SendMessage");

        return app;
    }
}
