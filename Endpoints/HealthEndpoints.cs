using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MovieDate.Data;

namespace MovieDate.Endpoints;

public static class HealthEndpoints
{
    public const string DbTag = "db";

    public static IServiceCollection AddAppHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>(tags: [DbTag]);

        return services;
    }

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // DB-free liveness probe — Fly's health check target; must not depend on the database.
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithName("HealthCheck");

        // Database readiness: 200 Healthy / 503 Unhealthy.
        app.MapHealthChecks("/health/db", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(DbTag),
        });

        return app;
    }
}
