using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using MovieDate.Contracts;
using MovieDate.Data;

namespace MovieDate.Endpoints;

public static class AuthEndpoints
{
    public const string RoutePrefix = "/api/auth";
    public const string DataProtectionApplicationName = "moviedate";

    public static IServiceCollection AddAppAuth(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddAuthorization();

        services.AddIdentityApiEndpoints<AppUser>(options =>
            {
                // Email verification is deferred: accounts can sign in without confirming.
                options.SignIn.RequireConfirmedAccount = false;
                options.SignIn.RequireConfirmedEmail = false;

                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;

                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            // Fly terminates TLS, so the container sees plain HTTP — force Secure outside Development.
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;

            // API clients (the SPA) need status codes, not redirects to a login page.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // Keys shared across Fly machines and surviving restarts, otherwise cookies break per machine/deploy.
        services.AddDataProtection()
            .PersistKeysToDbContext<AppDbContext>()
            .SetApplicationName(DataProtectionApplicationName);

        return services;
    }

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix);

        // register, login (?useCookies=true for the SPA), refresh, confirmEmail, manage/*, ...
        group.MapIdentityApi<AppUser>();

        group.MapPost("/logout", async (SignInManager<AppUser> signInManager) =>
            {
                await signInManager.SignOutAsync();
                return Results.NoContent();
            })
            .RequireAuthorization()
            .WithName("Logout");

        group.MapGet("/me", (ClaimsPrincipal user) =>
            {
                var email = user.FindFirstValue(ClaimTypes.Email);
                return email is null
                    ? Results.Unauthorized()
                    : Results.Ok(new MeResponse(email));
            })
            .RequireAuthorization()
            .WithName("Me");

        return app;
    }
}
