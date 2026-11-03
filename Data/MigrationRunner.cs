using Microsoft.EntityFrameworkCore;

namespace MovieDate.Data;

public static class MigrationRunner
{
    public const string Flag = "--migrate";

    /// <summary>
    /// Applies pending EF Core migrations, then upserts the movie and city catalog seeds;
    /// returns a process exit code (0 = success). A seed failure fails the release like a migration failure.
    /// </summary>
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

            var (inserted, updated) = await MovieCatalogSeeder.SeedAsync(db);
            logger.LogInformation("Movie catalog seeded: {Inserted} inserted, {Updated} updated.", inserted, updated);

            (inserted, updated) = await CityCatalogSeeder.SeedAsync(db);
            logger.LogInformation("City catalog seeded: {Inserted} inserted, {Updated} updated.", inserted, updated);

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration or seeding failed.");
            return 1;
        }
    }
}
