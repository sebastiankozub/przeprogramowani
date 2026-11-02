using Microsoft.EntityFrameworkCore;

namespace MovieDate.Data;

public static class MigrationRunner
{
    public const string Flag = "--migrate";

    /// <summary>Applies pending EF Core migrations; returns a process exit code (0 = success).</summary>
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigrationRunner));

        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                pending.Count, string.Join(", ", pending));

            await db.Database.MigrateAsync();

            logger.LogInformation("Database migrations applied successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration failed.");
            return 1;
        }
    }
}
